using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;

public class TestBizHawk : MonoBehaviour
{
    IEmulator emulator;
    IVideoProvider videoProvider;
    ISoundProvider soundProvider;

    InputManager inputManager;
    UnityInputProvider inputProvider;

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

    static int AudioChunkSize = 734; // [Hard to explain right now but for timestretching, preserve audio chunks of this many samples (734 = 1 frame at 60fps at SR=44100)]
    static int RunningAudioBufferSize = 8096;
    short[] runningAudioBuffer;
    int runningAudioBufferLength;

    public int frame = 0;

    void Start()
    {
        // Check if there is an AudioSource attached
        if (!GetComponent<AudioSource>()) {
            Debug.LogWarning("No AudioSource component, will not play emulator audio");
        }

        // Initialize stuff
        runningAudioBuffer = new short[RunningAudioBufferSize];
        runningAudioBufferLength = 0;

        inputManager = new InputManager();
        inputProvider = new UnityInputProvider();

        dialogParent = new UnityDialogParent();

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

            // [not sure what this does but seems important]
            inputManager.SyncControls(emulator, movieSession, config);
        } else {
            Debug.LogWarning($"Failed to load {romPath}.");
        }
    }

    // [Not really sure what the framerate of this should be tbh - should check what BizHawk does]
    void Update()
    {
        if (emulator != null) {
            // Input handling
            inputProvider.Update(); // (this should read in all the Unity input and store it in a queue)
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

            // Re-init the target texture if needed (if dimensions have changed, as happens on PSX)
            if (forceReinitTexture || (targetTexture.width != videoProvider.BufferWidth || targetTexture.height != videoProvider.BufferHeight)) {
                InitTargetTexture();
                forceReinitTexture = false;
            }

            // copy the texture from the emulator to the target renderer
            // [any faster way to do this?]
            int[] videoBuffer = videoProvider.GetVideoBuffer();
            // Debug.Log($"Actual video buffer size: {videoBuffer.Length}");
            // [note: for e.g. PSX, the videoBuffer array is much larger than the actual current pixel data (BufferWidth x BufferHeight)
            //  can possibly optimize a lot by truncating the buffer before this call:]
            targetTexture.SetPixelData(videoBuffer, 0);
            targetTexture.Apply();

            // get audio samples for the emulated frame
            short[] lastFrameAudioBuffer;
            int nSamples;
            soundProvider.GetSamplesSync(out lastFrameAudioBuffer, out nSamples);
            // // Debug.Log($"Got {nSamples} samples this frame.");
            // // [Seems to be ~734 samples each frame for mario.nes]
            // // append them to running buffer
            for (int i = 0; i < nSamples; i++) {
                runningAudioBuffer[runningAudioBufferLength] = lastFrameAudioBuffer[i];
                runningAudioBufferLength++;
            }

            frame++;
        }
    }

    // Init/re-init the texture for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTargetTexture() {
        targetTexture = new Texture2D(videoProvider.BufferWidth, videoProvider.BufferHeight, textureFormat, linearTexture);
        targetRenderer.material.mainTexture = targetTexture;
    }

    // Send audio from the emulator to the AudioSource
    // (will only run if there is an AudioSource component attached)
    // [this method is a mess atm, needs to be cleaned up]
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        // Debug.Log($"n channels: {channels}");
        // Debug.Log($"Unity buffer size: {out_buffer.Length}; Emulated audio buffer size: {runningAudioBufferLength}");

        // Unity needs 2048 samples right now, and depending on the speed the emulator is running,
        // we might have anywhere from 0 to like 10000 accumulated.
        
        // If the emulator isn't running at 'native' speed (e.g running at 0.5x or 2x), we need to do some kind of rudimentary timestretching
        // to play the audio faster/slower without distorting too much

        int method = 2;

        for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (method == 0) {
                // Attempt to do pitch-neutral timestretching by preserving the sample rate of audio chunks of a certain length (AudioChunkSize)
                // but playing those chunks either overlapping (if emulator is faster than native speed) or with gaps (if slower)
                // [there may be better ways to do this]
                // (it seems like EmuHawk does something similar to this maybe?)
                // [currently sounds really bad i think there must be something wrong with the code below]
                    
                int n_chunks = runningAudioBuffer.Length/AudioChunkSize;
                int chunk_sep = (out_buffer.Length - AudioChunkSize)/n_chunks;
                
                out_buffer[out_i] = 0f;

                // Add contribution from each chunk
                // [might be better to take the mean of all chunks here, idk.]
                for (int chunk_i = 0; chunk_i < n_chunks; chunk_i++) {
                    int chunk_start = chunk_i*chunk_sep; // in output space
                    if (chunk_start <= out_i && out_i < chunk_start + AudioChunkSize) {
                        // This chunk is contributing
                        int src_i = (out_i - chunk_start) + (chunk_i*AudioChunkSize);
                        short sample = runningAudioBuffer[src_i];
                        out_buffer[out_i] += sample/32767f; // convert short (-32768 to 32767) to float (-1f to 1f)
                    }
                }
            } else if (method == 1) {
                // very naive, just truncate if necessary
                // [sounds ok but distorted]
                out_buffer[out_i] = runningAudioBuffer[out_i]/32767f;
            } else {
                // No pitch adjustment, just stretch the accumulated audio to fit unity's audio buffer
                // [sounds ok but a little weird, and means the pitch changes if the sample rate changes]
                out_buffer[out_i] = runningAudioBuffer[(out_i*runningAudioBufferLength)/out_buffer.Length]/32767f;
            }
        }

        // consume all accumulated samples (play them) and reset the buffer
        // [instead should probably have some samples left over for the next unity chunk, todo think about this more]
        runningAudioBufferLength = 0;
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
}