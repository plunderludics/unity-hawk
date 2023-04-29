using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using System.IO;

public class TestBizHawk : MonoBehaviour
{
    IEmulator emulator;
    IController controller;
    RomLoader romLoader;
    // TODO: create config
    Config config;
    FirmwareManager firmwareManager = new FirmwareManager();
    //RomLoader loader;

    void Start()
    {
        var configPath = Path.Combine(Application.dataPath, "config.ini");
        Debug.Log(configPath);
        config = ConfigService.Load<Config>(configPath);
        romLoader = new RomLoader(config) {
            // ChoosePlatform = ChoosePlatformForRom,
        };

        var romPath = Path.Combine(Application.dataPath, "mario.nes");
        var nextComm = CreateCoreComm();

        // maybe not necessary?
        IOpenAdvancedLibretro ioaRetro = null;
        var result = romLoader.LoadRom(romPath, nextComm, ioaRetro?.CorePath);

        controller = new NullController();
        emulator = result
            ? romLoader.LoadedEmulator
            : new NullEmulator();
    }

    void Update()
    {
        emulator.FrameAdvance(controller, true, true);
        Debug.Log(emulator.Frame);
    }

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
}
