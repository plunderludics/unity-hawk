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
    IController controller;
    IVideoProvider videoProvider;

    RomLoader loader;
    Config config;
    FirmwareManager firmwareManager = new FirmwareManager();

    string bizhawkDir = Path.Combine(Application.dataPath, "BizHawk");

    public Renderer targetRenderer;
    Texture2D targetTexture;

    void Awake()
    {
        // [copied from MainForm.cs]
        // Note: the gamedb directory MUST already have the gamedb.txt file, otherwise seems to hang :(
        string gamedbPath = Path.Combine(bizhawkDir, "gamedb");
        Database.InitializeDatabase(
            bundledRoot:gamedbPath,
            userRoot: gamedbPath,
            silent: true);
        BootGodDb.Initialize(gamedbPath);
        MAMEMachineDB.Initialize(gamedbPath);
    }
    void Start()
    {
        var configPath = Path.Combine(bizhawkDir, "config.ini");
        Debug.Log(configPath);
        config = ConfigService.Load<Config>(configPath);
        loader = new RomLoader(config) {
            //ChoosePlatform = ChoosePlatformForRom,
        };

        loader.OnLoadError += ShowLoadError;
        loader.OnLoadSettings += CoreSettings;
        loader.OnLoadSyncSettings += CoreSyncSettings;

        var romPath = Path.Combine(Application.dataPath, "mario.nes");
        var nextComm = CreateCoreComm();

        bool loaded = loader.LoadRom(romPath, nextComm, null);

        if (loaded) {
            Debug.Log($"Loaded {romPath}!");

            controller = new NullController();
            emulator = loader.LoadedEmulator;

            videoProvider = emulator.AsVideoProvider();

            Debug.Log($"{videoProvider.BufferWidth} x {videoProvider.BufferHeight}");
            targetTexture = new Texture2D(videoProvider.BufferWidth, videoProvider.BufferHeight);
            targetRenderer.material.mainTexture = targetTexture;

        } else {
            Debug.LogWarning($"Failed to load {romPath}.");
        }
    }

    void Update()
    {
        if (emulator != null) {
            emulator.FrameAdvance(controller, true, true);
            // Debug.Log(emulator.Frame);

            int[] buffer = videoProvider.GetVideoBuffer();
            // i guess this just happens to be the right pixel format already
            targetTexture.SetPixelData(buffer, 0);
            targetTexture.Apply();
        }
    }

    //
    // The rest of the methods are copied / closely adapted from ones in MainForm.cs
    //

    CoreComm CreateCoreComm() {
        var dialogParent = new UnityDialogParent();
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