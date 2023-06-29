using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Profiling;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;

// [Audio stuff is a bit of a mess right now.
// I don't really get why using BizHawk's SoundOutputProvider gives crackly sound
// Currently the manual PreserveSampleRate option sounds perfect but it's basically worthless because it accumulates lag whenever the emulator runs faster than 1x.]

public class UHEmulator : MonoBehaviour
{
    IEmulator emulator;
    IVideoProvider videoProvider;
    ISoundProvider soundProvider;
    InputManager inputManager;

    IDialogParent dialogParent;

    MovieSession movieSession; // [annoying that we need this at all]

    RomLoader loader;
    Config config;
    FirmwareManager firmwareManager = new FirmwareManager();

    public string romFileName = "mario.nes";
    public string currentCore = "nul";

    public Renderer targetRenderer;
    Texture2D targetTexture;

    public TextureFormat textureFormat = TextureFormat.BGRA32;
    public bool linearTexture; // [seems so make no difference visually]
    public bool forceReinitTexture;
    public bool blitTexture = true;

    public bool useManualAudioHandling;
    static int AudioChunkSize = 734; // [Hard to explain right now but for 'Accumulate' AudioStretchMethod, preserve audio chunks of this many samples (734 ~= 1 frame at 60fps at SR=44100)]
    static int AudioBufferSize = 44100*10;
    short[] audioBuffer; // circular buffer (queue) to store audio samples accumulated from the emulator
    static int ChannelCount = 2; // Seems to be always 2, for all BizHawk sound and for Unity audiosource
    int audioBufferStart, audioBufferEnd;
    SoundOutputProvider bufferedSoundProvider;
    public enum AudioStretchMethod {
        Truncate,
        PreserveSampleRate,
        Stretch,
        Overlap
    }
    public AudioStretchMethod audioStretchMethod = AudioStretchMethod.PreserveSampleRate;

    public int frame = 0;

    JobHandle frameAdvanceJobHandle;

    ProfilerMarker s_FrameAdvanceMarker;

    void Start()
    {
        s_FrameAdvanceMarker = new ProfilerMarker($"FrameAdvance {GetInstanceID()}");

        // Check if there is an AudioSource attached
        if (!GetComponent<AudioSource>()) {
            Debug.LogWarning("No AudioSource component, will not play emulator audio");
        }

        // Initialize stuff
        audioBuffer = new short[AudioBufferSize];
        ClearAudioBuffer();

        inputManager = new InputManager();

        dialogParent = new UHDialogParent();

        // Load config
        var configPath = Path.Combine(UnityHawk.bizhawkDir, "config.ini");

        config = ConfigService.Load<Config>(configPath);

        // Init controls
        inputManager.ControllerInputCoalescer = new(); // [seems to be necessary]

        // Set up movie session
        // [this is kind of stupid because we don't really need a movie session
        //  but InputManager expects a MovieSession as part of the input chain :/
        //  i guess the alternative would be to reimplement InputManager]
        movieSession = new MovieSession(
                config.Movies,
                config.PathEntries.MovieBackupsAbsolutePath(),
                dialogParent,
                null,
                () => {},
                () => {}
        );

        loader = new RomLoader(config);

        loader.OnLoadError += ShowLoadError;
        loader.OnLoadSettings += CoreSettings;
        loader.OnLoadSyncSettings += CoreSyncSettings;

        var romPath = Path.Combine(UnityHawk.romsDir, romFileName);

        var nextComm = CreateCoreComm();

        bool loaded = loader.LoadRom(romPath, nextComm, null);

        if (loaded) {
            emulator = loader.LoadedEmulator;
            currentCore = emulator.Attributes().CoreName;

            videoProvider = emulator.AsVideoProviderOrDefault();
            soundProvider = emulator.AsSoundProviderOrDefault();

            Debug.Log($"virtual: {videoProvider.VirtualWidth} x {videoProvider.VirtualHeight} = {videoProvider.VirtualWidth * videoProvider.VirtualHeight}");
            Debug.Log($"buffer: {videoProvider.BufferWidth} x {videoProvider.BufferHeight} = {videoProvider.BufferWidth * videoProvider.BufferHeight}");
            InitTargetTexture();

            // Turns out BizHawk provides this wonderful SyncToAsyncProvider to asynchronously provide audio, which is what we need for the way Unity handles sound
            // It also does some resampling so the sound works ok when the emulator is running at speeds other than 1x
            soundProvider.SetSyncMode(SyncSoundMode.Sync); // [we could also use Async directly if the emulator provides it]
            bufferedSoundProvider = new SoundOutputProvider(() => emulator.VsyncRate(), standaloneMode: true); // [idk why but standalone mode seems to be less distorted]
            bufferedSoundProvider.BaseSoundProvider = soundProvider;
            // bufferedSoundProvider.MaxSamplesDeficit = 1985;

            // [not sure what this does but seems important]
            inputManager.SyncControls(emulator, movieSession, config);
        } else {
            Debug.LogWarning($"Failed to load {romPath}.");
        }
    }

    // [Not really sure what the framerate of this should be tbh - should check what BizHawk does]
    public void FrameAdvance(IInputProvider inputProvider)
    {
        if (emulator != null) {
            s_FrameAdvanceMarker.Begin();
            var finalHostController = inputManager.ControllerInputCoalescer;
            // InputManager.ActiveController.PrepareHapticsForHost(finalHostController);
            ProcessInput(finalHostController, inputProvider);
            inputManager.ActiveController.LatchFromPhysical(finalHostController); //[idk what this does]
            // [there's a bunch more input chain processing stuff in MainForm.cs that we are leaving out for now]

            // [For later:]
            // if (Tools.Has<LuaConsole>())
            // {
            //     Tools.LuaConsole.ResumeScripts(false);
            // }
            
            // [gotta call this to make sure the input gets through]
            movieSession.HandleFrameBefore();

            // Emulator step forward
            emulator.FrameAdvance(inputManager.ControllerOutput, true, true);

            // [maybe not needed]
            movieSession.HandleFrameAfter();

            frame++;
            s_FrameAdvanceMarker.End();
        }
    }

    // [we do the texture blitting in a separate method because it has to run on the main thread (and FrameAdvance runs in parallel)]
    public void AfterFrameAdvance() { 
        // Re-init the target texture if needed (if dimensions have changed, as happens on PSX)
        if (forceReinitTexture || (targetTexture.width != videoProvider.BufferWidth || targetTexture.height != videoProvider.BufferHeight)) {
            InitTargetTexture();
            forceReinitTexture = false;
        }

        if (blitTexture) {
            // copy the texture from the emulator to the target renderer
            // [any faster way to do this?]
            int[] videoBuffer = videoProvider.GetVideoBuffer();
            // Debug.Log($"Actual video buffer size: {videoBuffer.Length}");
            // [note: for e.g. PSX, the videoBuffer array is much larger than the actual current pixel data (BufferWidth x BufferHeight)
            //  can possibly optimize a lot by truncating the buffer before this call:]
            targetTexture.SetPixelData(videoBuffer, 0);
            targetTexture.Apply(/*updateMipmaps: false*/);
        }
        // get audio samples for the emulated frame
        // [maybe this should happen in FrameAdvance, idk]
        short[] lastFrameAudioBuffer;
        int nSamples;
        soundProvider.GetSamplesSync(out lastFrameAudioBuffer, out nSamples);
        // NOTE! there are actually 2*nSamples values in the buffer because it's stereo sound
        // Debug.Log($"Adding {nSamples} samples to the buffer.");
        // [Seems to be ~734 samples each frame for mario.nes]
        // append them to running buffer
        lock (audioBuffer) {
            for (int i = 0; i < nSamples*ChannelCount; i++) {
                // Debug.Log($"Adding sample, audioBufferLength={AudioBufferLength()}");
                if (AudioBufferLength() == audioBuffer.Length - 1) {
                    // Debug.LogWarning("audio buffer full, dropping samples");
                    break;
                }
                audioBuffer[audioBufferEnd] = lastFrameAudioBuffer[i];
                audioBufferEnd++;
                audioBufferEnd %= audioBuffer.Length;
            }
        }
    }

    // Init/re-init the texture for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTargetTexture() {
        targetTexture = new Texture2D(videoProvider.BufferWidth, videoProvider.BufferHeight, textureFormat, linearTexture);
        targetRenderer.material.mainTexture = targetTexture;
    }

    
    // Send audio from the emulator to the AudioSource
    // (will only run if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] data, int channels) {
        if (useManualAudioHandling) {
            Old_OnAudioFilterRead(data, channels);
        } else {
            New_OnAudioFilterRead(data, channels);
        }
    }

    // Use Bizhawk's internal resampling engine - idk why this doesn't sound as good as EmuHawk does
    void New_OnAudioFilterRead(float[] data, int channels) {
        if (channels != ChannelCount) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }
        // Just have to grab the samples from the SyncToAsyncProvider and convert it from short to float
        short[] short_buf = new short[data.Length];
        int sampleCount = -1;
        // bufferedSoundProvider.GetSamples(data.Length/channels, out short_buf, out sampleCount); // for non-standalone mode
        bufferedSoundProvider.GetSamples(short_buf);
        // Debug.Log($"Requested {data.Length/channels}, got {short_buf.Length/channels} samples from bufferedSoundProvider");
        // Debug.Log($"short_buf.Length = {short_buf.Length}");
        for (int i = 0; i < short_buf.Length; i++) {
            data[i] = short_buf[i]/32767f;
        }
    }

    // Manual audio handling - sounds bad but in a different way
    void Old_OnAudioFilterRead(float[] out_buffer, int channels) {
        // Debug.Log($"n channels: {channels}");
        if (channels != ChannelCount) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }
        lock(audioBuffer) {
            int n_samples = AudioBufferLength();
            // Debug.Log($"Unity buffer size: {out_buffer.Length}; Emulated audio buffer size: {n_samples}");

            // Currently this assumes the input is mono and the output is interleaved stereo
            // which seems true for NES but almost certainly not always true

            // Unity needs 2048 samples right now, and depending on the speed the emulator is running,
            // we might have anywhere from 0 to like 10000 accumulated.
            
            // If the emulator isn't running at 'native' speed (e.g running at 0.5x or 2x), we need to do some kind of rudimentary timestretching
            // to play the audio faster/slower without distorting too much

            if (audioStretchMethod == AudioStretchMethod.PreserveSampleRate) {
                // Play back the samples at 1:1 sample rate, which means audio will lag behind if the emulator runs faster than 1x
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    // Debug.Log($"attempting access audioBuffer[{audioBufferStart}]");
                    if (AudioBufferLength() == 0) {
                        Debug.LogWarning("Emulator audio buffer has no samples to consume");
                        return;
                    }
                    out_buffer[out_i] = audioBuffer[audioBufferStart]/32767f;
                    audioBufferStart = (audioBufferStart+1)%audioBuffer.Length;
                }
                // leave remaining samples for next time
            } else if (audioStretchMethod == AudioStretchMethod.Overlap) {
                // experimental attempt to do pitch-neutral timestretching by preserving the sample rate of audio chunks of a certain length (AudioChunkSize)
                // but playing those chunks either overlapping (if emulator is faster than native speed) or with gaps (if slower)
                // [there may be better ways to do this]
                // (it seems like EmuHawk does something similar to this maybe?)
                // [currently sounds really bad i think there must be something wrong with the code below]
                    
                int n_chunks = (n_samples-1)/AudioChunkSize + 1;
                int chunk_sep = (out_buffer.Length - AudioChunkSize)/n_chunks;
                
                for (int out_i = 0; out_i < out_buffer.Length; out_i++ ) {
                    out_buffer[out_i] = 0f;

                    // Add contribution from each chunk
                    // [might be better to take the mean of all chunks here, idk.]
                    for (int chunk_i = 0; chunk_i < n_chunks; chunk_i++) {
                        int chunk_start = chunk_i*chunk_sep; // in output space
                        if (chunk_start <= out_i && out_i < chunk_start + AudioChunkSize) {
                            // This chunk is contributing
                            int src_i = (out_i - chunk_start) + (chunk_i*AudioChunkSize);
                            short sample = GetAudioBufferAt(src_i);
                            out_buffer[out_i] += sample/32767f; // convert short (-32768 to 32767) to float (-1f to 1f)
                        }
                    }
                }
                ClearAudioBuffer(); // all samples consumed
            } else if (audioStretchMethod == AudioStretchMethod.Stretch) {
                // No pitch adjustment, just stretch the accumulated audio to fit unity's audio buffer
                // [sounds ok but a little weird, and means the pitch changes if the emulator framerate changes]
                // [although in reality it doesn't actually sound like it's getting stretched, just distorted, i don't understand this]
        
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    int src_i = (out_i*n_samples)/out_buffer.Length;
                    out_buffer[out_i] = GetAudioBufferAt(src_i)/32767f;
                }
                ClearAudioBuffer(); // all samples consumed
            } else if (audioStretchMethod == AudioStretchMethod.Truncate) {
                // very naive, just truncate incoming samples if necessary
                // [sounds ok but very distorted]
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                    out_buffer[out_i] = GetAudioBufferAt(out_i)/32767f;
                }
                ClearAudioBuffer(); // throw away any remaining samples
            } else {
                Debug.LogWarning("Unhandled AudioStretchMode");
            }
        }
    }

    // Based on MainForm:ProcessInput, but with a lot of stuff missing
    void ProcessInput(
        ControllerInputCoalescer finalHostController,
        IInputProvider inputProvider // [not in BizHawk, this is our abstraction of BizHawk's Input class]
    ) {
        // loop through all available events
        InputEvent ie;
        while ((ie = inputProvider.DequeueEvent()) != null)
        {
            // Debug.Log(ie);
            finalHostController.Receive(ie);
        }
    }

    //
    // The rest of the methods are copied / closely adapted from ones in MainForm.cs
    //
    CoreComm CreateCoreComm() {
        var cfp = new CoreFileProvider(
            dialogParent,
            firmwareManager,
            config.PathEntries,
            config.FirmwareUserSpecifications);

        var prefs = CoreComm.CorePreferencesFlags.None;

        if (config.SkipWaterboxIntegrityChecks)
            prefs = CoreComm.CorePreferencesFlags.WaterboxMemoryConsistencyCheck;

        // can't pass self as IDialogParent :(
        return new CoreComm(
            s => Debug.Log($"message: {s}"),
            s => Debug.Log($"notification: {s}"),
            cfp,
            prefs);
    }

    private void CoreSettings(object sender, RomLoader.SettingsLoadArgs e)
    {
        e.Settings = config.GetCoreSettings(e.Core, e.SettingsType);
    }

    private void CoreSyncSettings(object sender, RomLoader.SettingsLoadArgs e)
    {
        e.Settings = config.GetCoreSyncSettings(e.Core, e.SettingsType);
    }

    private void ShowLoadError(object sender, RomLoader.RomErrorArgs e) {
        Debug.LogError(e.Message);
    }

    // helper methods for circular audio buffer
    // [could also just make an external CircularBuffer class]
    private int AudioBufferLength() {
        return (audioBufferEnd-audioBufferStart+audioBuffer.Length)%audioBuffer.Length;
    }
    private void ClearAudioBuffer() {
        audioBufferStart = 0;
        audioBufferEnd = 0;
    }
    private short GetAudioBufferAt(int i) {
        return audioBuffer[(audioBufferStart + i)%audioBuffer.Length];
    }
}