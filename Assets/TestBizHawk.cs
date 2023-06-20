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

    static int RunningAudioBufferSize = 4096;
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
            // // [Seems to be 734 samples each frame for mario.nes]
            // // append them to running buffer
            // for (int i = 0; i < nSamples; i++) {
            //     runningAudioBuffer[runningAudioBufferLength] = lastFrameAudioBuffer[i];
            //     runningAudioBufferLength++;
            // }

            frame++;
        }
    }

    // Init/re-init the texture for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTargetTexture() {
        targetTexture = new Texture2D(videoProvider.BufferWidth, videoProvider.BufferHeight, textureFormat, linearTexture);
        targetRenderer.material.mainTexture = targetTexture;
    }

    // [Heads up, will only get called if there is an AudioSource component attached]
    // [also doesn't work at all right now, idk why]
    void OnAudioFilterRead(float[] data, int channels) {
        // Ignore input (contents of `data`), just overwrite with emulator audio

        // Debug.Log($"n channels: {channels}");
        // Debug.Log($"Unity buffer size: {data.Length}; Emulated audio buffer size: {runningAudioBufferLength}");

        // i have no idea how to deal with framerate mismatch between unity and the emulator right now
        // for now, just stretch out the audio
        for (int i = 0; i < data.Length; i++) {
            short sample = runningAudioBuffer[i*(runningAudioBufferLength/data.Length)];
            data[i] = sample/32767; // is this even right? probably a better way to do this
        }

        // consume all accumulated samples (play them) and restart the buffer
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