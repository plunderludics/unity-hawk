// global manager thing does one-time initialization of all the stuff BizHawk expects (db, etc)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;
using System.Runtime.InteropServices;
using System;

public class UnityHawk : MonoBehaviour
{   
    public static readonly string bizhawkDir = Path.Combine(Application.dataPath, "BizHawk");
    public static readonly string romsDir = Path.Combine(Application.dataPath, "Roms");

    
    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetDllDirectory(string lpPathName);

    void Awake()
    {
        // [huge hack - preload all the dlls for cores that have to load them at runtime
        //  i don't know why, but just calling SetDllDirectory before bizhawk loads doesn't work.
        //  but there must be a better way than this]

        var dllDir = Path.Combine(bizhawkDir, "dll");
        _ = SetDllDirectory(dllDir);

        var libsToLoad = new List<string> {
            //QuickNes (NES)
            "libquicknes.dll",
            //Nymashock (PSX)
            "waterboxhost.dll",
            "libzstd.dll",
            "libbizabiadapter_msabi_sysv.dll",
            //Mupen64 (N64)
            "mupen64plus.dll",
            "mupen64plus-audio-bkm.dll",
            "mupen64plus-input-bkm.dll",
            // "mupen64plus-video-rice.dll",
            "mupen64plus-video-GLideN64.dll",
            "libspeexdsp.dll"

            // TODO load any others we need [or find a way to avoid doing this]
        };
        foreach (string lib in libsToLoad) {
            int e = (int)LoadLibrary(lib);
            if (e == 0) {
                Debug.LogError($"Could not load: {lib}");
            }
        }

        // [copied from MainForm.cs]
        // Note: the gamedb directory MUST already have the gamedb.txt file, otherwise seems to hang :(
        string gamedbPath = Path.Combine(bizhawkDir, "gamedb");
        Database.InitializeDatabase(
            bundledRoot: gamedbPath,
            userRoot: gamedbPath,
            silent: true);
        BootGodDb.Initialize(gamedbPath);

        // this is only necessary for certain platforms so maybe should be in a 
        // 'InitializeMAMEMachineIfNeeded' method that clients can call, something like that
        MAMEMachineDB.Initialize(gamedbPath);
    }
}
