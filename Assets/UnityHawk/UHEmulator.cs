using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Profiling;

using System;
using System.Linq;
using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

public class UHEmulator : MonoBehaviour
{
    [Header("Params")]
    // All pathnames are loaded relative to ./Assets/BizHawk/, unless the pathname is absolute [sort of abusing Path.Combine behavior here]
    public string romFileName = "mario.nes";
    public string configFileName = "config.ini";
    public string saveStateFileName = ""; // Leave empty to boot clean
    public List<string> luaScripts;
    public Renderer targetRenderer;
    public float frameRateMultiplier = 1f; // Speed up or slow down emulation

    // [Make these public for debugging texture stuff]
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;
    private bool linearTexture; // [seems so make no difference visually]
    private bool forceReinitTexture;
    public bool blitTexture = true;

    // [Make these public for debugging audio stuff]
    private bool useManualAudioHandling = false;
    public enum AudioStretchMethod {Truncate, PreserveSampleRate, Stretch, Overlap}
    private AudioStretchMethod audioStretchMethod = AudioStretchMethod.Truncate; // if using non-manual audio, this should probably be Truncate

    [Header("Debug")]
    // [These should really be readonly in the inspector]
    public int frame = 0;
    public float uncappedFps;
    public float emulatorDefaultFps;
    public string currentCore = "nul";

    // If other scripts want to grab the texture
    public RenderTexture Texture => _renderTexture;

    IEmulator emulator;
    IGameInfo game;
    IVideoProvider videoProvider;
    ISoundProvider soundProvider;
    InputManager inputManager;
    IDialogParent dialogParent;
    MovieSession movieSession; // [annoying that we need this at all]
    RomLoader loader;
    Config config;
    FirmwareManager firmwareManager = new FirmwareManager();

    UHInputProvider inputProvider;

    Texture2D _bufferTexture;
    RenderTexture _renderTexture; // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    static int AudioChunkSize = 734; // [Hard to explain right now but for 'Accumulate' AudioStretchMethod, preserve audio chunks of this many samples (734 ~= 1 frame at 60fps at SR=44100)]
    static int AudioBufferSize = 44100*2;

    short[] audioBuffer; // circular buffer (queue) to store audio samples accumulated from the emulator
    int audioBufferStart, audioBufferEnd;
    static int ChannelCount = 2; // Seems to be always 2, for all BizHawk sound and for Unity audiosource
    private int audioSamplesNeeded; // track how many samples unity wants to consume
    SoundOutputProvider bufferedSoundProvider; // BizHawk's internal resampling engine

    ProfilerMarker s_FrameAdvanceMarker;

    UHLuaEngine luaEngine;

    bool _stopRunningEmulatorTask = false;

    void OnEnable()
    {
        s_FrameAdvanceMarker = new ProfilerMarker($"FrameAdvance {GetInstanceID()}");

        if (!targetRenderer) {
            // Default to the attached Renderer component, if there is one
            targetRenderer = GetComponent<Renderer>();
            if (!targetRenderer) {
                Debug.LogWarning("No Renderer present, will not display emulator graphics");
            }
        }

        // Check if there is an AudioSource attached
        // [would be nice if we could specify a targetAudioSource on a different object but Unity makes that inconvenient]
        if (!GetComponent<AudioSource>()) {
            Debug.LogWarning("No AudioSource component, will not play emulator audio");
        }

        _stopRunningEmulatorTask = false;
        bool loaded = InitEmulator();
        if (loaded) {
            // start emulator looping in a new thread unrelated to the unity framerate
            // [maybe should have a param to set if it should run in a separate thread or not? kind of annoying to support both though]
            Task.Run(EmulatorLoop);
        }
    }

    void Update() {
        if (emulator != null) {
            // replenish the input queue with new input from unity
            inputProvider.Update();
            // get the texture from bizhawk and blit to the unity texture
            // [for efficiency we could have a flag to check if the texture has changed since last Update (it won't have if the emulator is running slower than unity)]
            UpdateTexture();
        }
    }
    
    void OnDisable() {
        // In the editor, gotta kill the task or it will keep running in edit mode
        _stopRunningEmulatorTask = true;
    }

    void OnApplicationPause() {
        // TODO would be good to pause the emulator here
    }

    // This will run asynchronously so that it's not bound by unity update framerate
    void EmulatorLoop() {
        while (!_stopRunningEmulatorTask) {
            if (emulator == null) continue;

            emulatorDefaultFps = (float)emulator.VsyncRate(); // Idk if this can change at runtime but checking every frame just in case
            System.Diagnostics.Stopwatch sw = new();
            sw.Start();
            s_FrameAdvanceMarker.Begin();

            FrameAdvance();

            // Store audio from the last emulated frame so it can be played back on the unity audio thread
            StoreLastFrameAudio();

            s_FrameAdvanceMarker.End();
            sw.Stop();
            TimeSpan ts = sw.Elapsed;
            uncappedFps = (float)(1f/ts.TotalSeconds);

            // Very naive throttle but it works fine - just sleep a bit if above target fps on this frame
            // (Throttle.cs in BizHawk seems a lot more sophisticated)
            float targetFps = emulatorDefaultFps*frameRateMultiplier;
            if (uncappedFps > targetFps) {
                Thread.Sleep((int)(1000*(1f/targetFps - 1f/uncappedFps)));
            }
            
            frame++;
        }
    }

    bool InitEmulator() {
        // Initialize stuff
        audioBuffer = new short[AudioBufferSize];
        audioSamplesNeeded = 0;
        ClearAudioBuffer();

        UnityHawk.InitIfNeeded();

        inputProvider = new UHInputProvider();
        inputManager = new InputManager();
        dialogParent = new UHDialogParent();

        // Load config
        var configPath = Path.Combine(UnityHawk.bizhawkDir, configFileName);

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

        luaEngine = new UHLuaEngine();

        loader = new RomLoader(config);

        loader.OnLoadError += ShowLoadError;
        loader.OnLoadSettings += CoreSettings;
        loader.OnLoadSyncSettings += CoreSyncSettings;

        var romPath = Path.Combine(UnityHawk.bizhawkDir, romFileName);

        var nextComm = CreateCoreComm();

        bool loaded = loader.LoadRom(romPath, nextComm, null);
        
        if (loaded) {
            emulator = loader.LoadedEmulator;
            game = loader.Game;
            currentCore = emulator.Attributes().CoreName;

            videoProvider = emulator.AsVideoProviderOrDefault();
            soundProvider = emulator.AsSoundProviderOrDefault();

            Debug.Log($"virtual: {videoProvider.VirtualWidth} x {videoProvider.VirtualHeight} = {videoProvider.VirtualWidth * videoProvider.VirtualHeight}");
            Debug.Log($"buffer: {videoProvider.BufferWidth} x {videoProvider.BufferHeight} = {videoProvider.BufferWidth * videoProvider.BufferHeight}");
            InitTextures();

            // Turns out BizHawk provides this wonderful SyncToAsyncProvider to asynchronously provide audio, which is what we need for the way Unity handles sound
            // It also does some resampling so the sound works ok when the emulator is running at speeds other than 1x
            soundProvider.SetSyncMode(SyncSoundMode.Sync); // [we could also use Async directly if the emulator provides it]
            bufferedSoundProvider = new SoundOutputProvider(() => emulator.VsyncRate(), standaloneMode: true); // [idk why but standalone mode seems to be less distorted]
            bufferedSoundProvider.BaseSoundProvider = soundProvider;
            // bufferedSoundProvider.LogDebug = true;

            // [not sure what this does but seems important]
            inputManager.SyncControls(emulator, movieSession, config);

            if (!String.IsNullOrEmpty(saveStateFileName)) {
                bool loadedState = LoadState(Path.Combine(UnityHawk.bizhawkDir, saveStateFileName));
                if (!loadedState) {
                    Debug.LogWarning($"Failed to load state: {saveStateFileName}");
                }
            }

            var luaScriptPaths = luaScripts.Select(path => Path.Combine(UnityHawk.bizhawkDir, path)).ToList();
            luaEngine.Restart(config, inputManager, emulator, game, luaScriptPaths);
        } else {
            Debug.LogWarning($"Failed to load {romPath}.");
        }
        return loaded;
    }

    void FrameAdvance()
    {
        if (emulator != null) {
            var finalHostController = inputManager.ControllerInputCoalescer;
            // InputManager.ActiveController.PrepareHapticsForHost(finalHostController);
            ProcessInput(finalHostController, inputProvider);
            inputManager.ActiveController.LatchFromPhysical(finalHostController); //[idk what this does]
            // [there's a bunch more input chain processing stuff in MainForm.cs that we are leaving out for now]

            luaEngine.UpdateBefore();
            
            // [gotta call this to make sure the input gets through]
            movieSession.HandleFrameBefore();

            // Emulator step forward
            emulator.FrameAdvance(inputManager.ControllerOutput, true, true);

            // [maybe not needed]
            movieSession.HandleFrameAfter();

            luaEngine.UpdateAfter();
        }
    }

    void UpdateTexture() {
        // Re-init the target texture if needed (if dimensions have changed, as happens on PSX)
        if (forceReinitTexture || !_bufferTexture || !_renderTexture || (_bufferTexture.width != videoProvider.BufferWidth || _bufferTexture.height != videoProvider.BufferHeight)) {
            InitTextures();
            forceReinitTexture = false;
        }

        if (blitTexture) {
            // Copy the texture from the emulator into a Unity texture
            int[] videoBuffer = videoProvider.GetVideoBuffer();
            int nPixels = _bufferTexture.width * _bufferTexture.height;
            // Debug.Log($"Actual video buffer size: {videoBuffer.Length}");
            // Debug.Log($"Number of pixels: {nPixels}");

            // Write the pixel data from the emulator directly into the bufferTexture
            // (seems to work as long as the texture format is BGRA32 - only problem is it's flipped horizontally)
            _bufferTexture.SetPixelData(videoBuffer, 0);
            _bufferTexture.Apply(/*updateMipmaps: false*/);
            // This bit is annoying but in order to just flip the image we have to Blit into a separate RenderTexture
            Graphics.Blit(_bufferTexture, _renderTexture, scale: new Vector2(1f,-1f), offset: Vector2.zero);
        }
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures() {
        _bufferTexture = new     Texture2D(videoProvider.BufferWidth, videoProvider.BufferHeight, textureFormat, linearTexture);
        _renderTexture = new RenderTexture(videoProvider.BufferWidth, videoProvider.BufferHeight, depth:0, format:renderTextureFormat);
        if (targetRenderer) targetRenderer.material.mainTexture = _renderTexture;
    }

    void StoreLastFrameAudio() {
        // get audio samples for the emulated frame
        short[] lastFrameAudioBuffer;
        int nSamples;
        if (useManualAudioHandling) {
            nSamples = 0;
            soundProvider.GetSamplesSync(out lastFrameAudioBuffer, out nSamples);
            // NOTE! there are actually 2*nSamples values in the buffer because it's stereo sound
            // Debug.Log($"Adding {nSamples} samples to the buffer.");
            // [Seems to be ~734 samples each frame for mario.nes]
            // append them to running buffer
        } else {
            nSamples = audioSamplesNeeded;
            lastFrameAudioBuffer = new short[audioSamplesNeeded*ChannelCount];
            bufferedSoundProvider.GetSamples(lastFrameAudioBuffer);
            audioSamplesNeeded = 0;
        }

        lock (audioBuffer) {
            for (int i = 0; i < nSamples*ChannelCount; i++) {
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

    // Send audio from the emulator to the AudioSource
    // (will only run if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (channels != ChannelCount) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }
        // for non-manual audio - track how many samples we wanna request from bizhawk
        audioSamplesNeeded += out_buffer.Length/channels;

        // copy from the accumulated emulator audio buffer into unity's buffer
        // this needs to happen in both manual and non-manual mode
        lock (audioBuffer) {
            int n_samples = AudioBufferLength();
            // Debug.Log($"Unity buffer size: {out_buffer.Length}; Emulated audio buffer size: {n_samples}");

            // Unity needs 2048 samples right now, and depending on the speed the emulator is running,
            // we might have anywhere from 0 to like 10000 accumulated.
            
            // If the emulator isn't running at 'native' speed (e.g running at 0.5x or 2x), we need to do some kind of rudimentary timestretching
            // to play the audio faster/slower without distorting too much

            if (audioStretchMethod == AudioStretchMethod.PreserveSampleRate) {
                // Play back the samples at 1:1 sample rate, which means audio will lag behind if the emulator runs faster than 1x
                for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
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

    bool LoadState(string path) {
        if (!new SavestateFile(emulator, movieSession, null/*QuickBmpFile*/, movieSession.UserBag).Load(path, dialogParent))
        {
            Debug.LogWarning($"Could not load state: {path}");
            return false;
        }

        // [MainForm:LoadState also has a bunch of other stuff that might be important, but this seems to work for now]

        return true;
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