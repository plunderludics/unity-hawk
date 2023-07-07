// Represents a single instance of BizHawk
// (This is basically a very stripped-down implementation of MainForm.cs in BizHawk)

using UnityEngine; // (just for Debug.Log - otherwise this class should probably be independent of Unity)

using System;
using System.Collections.Generic;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;

namespace UnityHawk {
public class BizHawkInstance {
    public double DefaultFrameRate => emulator.VsyncRate();
    public bool IsLoaded => (emulator != null);
    public string CurrentCoreName => emulator.Attributes().CoreName;
    public int VideoBufferWidth => videoProvider.BufferWidth;
    public int VideoBufferHeight => videoProvider.BufferHeight;

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
    SoundOutputProvider bufferedSoundProvider; // BizHawk's internal resampling engine
    LuaEngine luaEngine;

    public bool InitEmulator(
        string configPath,
        string romPath,
        List<string> luaScriptPaths,
        string saveStateFileName = null
        // TODO params
    ) {
        UnityHawk.InitIfNeeded();

        inputManager = new InputManager();
        dialogParent = new DialogParent();

        // Load config
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

        luaEngine = new LuaEngine();

        loader = new RomLoader(config);

        loader.OnLoadError += ShowLoadError;
        loader.OnLoadSettings += CoreSettings;
        loader.OnLoadSyncSettings += CoreSyncSettings;

        var nextComm = CreateCoreComm();

        bool loaded = loader.LoadRom(romPath, nextComm, null);
        
        if (loaded) {
            emulator = loader.LoadedEmulator;
            game = loader.Game;

            videoProvider = emulator.AsVideoProviderOrDefault();
            soundProvider = emulator.AsSoundProviderOrDefault();

            // Debug.Log($"virtual: {videoProvider.VirtualWidth} x {videoProvider.VirtualHeight} = {videoProvider.VirtualWidth * videoProvider.VirtualHeight}");
            // Debug.Log($"buffer: {videoProvider.BufferWidth} x {videoProvider.BufferHeight} = {videoProvider.BufferWidth * videoProvider.BufferHeight}");

            // Turns out BizHawk provides this wonderful SyncToAsyncProvider to asynchronously provide audio, which is what we need for the way Unity handles sound
            // It also does some resampling so the sound works ok when the emulator is running at speeds other than 1x
            soundProvider.SetSyncMode(SyncSoundMode.Sync); // [we could also use Async directly if the emulator provides it]
            bufferedSoundProvider = new SoundOutputProvider(() => emulator.VsyncRate(), standaloneMode: true); // [idk why but standalone mode seems to be less distorted]
            bufferedSoundProvider.BaseSoundProvider = soundProvider;
            // bufferedSoundProvider.LogDebug = true;

            // [not sure what this does but seems important]
            inputManager.SyncControls(emulator, movieSession, config);

            if (!String.IsNullOrEmpty(saveStateFileName)) {
                bool loadedState = LoadState(saveStateFileName);
                if (!loadedState) {
                    Debug.LogWarning($"Failed to load state: {saveStateFileName}");
                }
            }

            luaEngine.Restart(config, inputManager, emulator, game, luaScriptPaths);
        } else {
            Debug.LogWarning($"Failed to load {romPath}.");
        }
        return loaded;
    }

    public void FrameAdvance(IInputProvider inputProvider)
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

    public bool LoadState(string path) {
        if (!new SavestateFile(emulator, movieSession, null/*QuickBmpFile*/, movieSession.UserBag).Load(path, dialogParent))
        {
            Debug.LogWarning($"Could not load state: {path}");
            return false;
        }

        // [MainForm:LoadState also has a bunch of other stuff that might be important, but this seems to work for now]

        return true;
    }

    public int[] GetVideoBuffer() {
        return videoProvider.GetVideoBuffer();
    }

    public void GetSamplesSync(out short[] buf, out int nSamples) {
        soundProvider.GetSamplesSync(out buf, out nSamples);
    }

    public void GetSamples(short[] buf) {
        bufferedSoundProvider.GetSamples(buf);
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
}