// global manager thing that does one-time initialization of all the stuff BizHawk expects (db, etc)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using BizHawk.Common.PathExtensions;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Linq;

using System.Threading.Tasks;

namespace UnityHawk {
// [if at some point we need some user-configurable global bizhawk settings we could make this back into a MonoBehaviour]
public static class UnityHawk
{
    public static readonly string packageName = "org.plunderludics.UnityHawk";
    public static readonly string bizhawkDirRelative = Path.Combine(packageName, "BizHawk");
    public static readonly string bizhawkDirForEditor = Path.Combine("Packages", bizhawkDirRelative);
    // [^ seems insane but somehow Unity makes this work whether the package is in the Library/PackageCache/ dir or the Packages/ dir
    //  maybe not the best idea to rely on this dark behaviour though..]

    // In the build put the bizhawk stuff inside "xx_Data/com.plunderludics.UnityHawk/BizHawk"
    public static readonly string bizhawkDirForBuild = Path.Combine(Application.dataPath, bizhawkDirRelative);
    
    public static readonly string bizhawkDir =
#if UNITY_EDITOR
        bizhawkDirForEditor;
#else
        bizhawkDirForBuild;
#endif

    public static readonly string defaultConfigPath = Path.Combine(bizhawkDir, "config.ini");
    
    public static readonly string dllDir = Path.Combine(bizhawkDir, "dll");

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();

    private static bool _initialized = false;

    public static void InitIfNeeded()
    {
        if (_initialized) return;
        // Initialize some global stuff that every BizHawk instance will use

        // override all the path variables in bizhawk since the way bizhawk determines them is
        // inconsistent between running in editor and running build
        PathUtils.DllDirectoryPath = dllDir;
        PathUtils.ExeDirectoryPath = bizhawkDir;
        PathUtils.DataDirectoryPath = bizhawkDir;

        //  huge hack - preload all the dlls for cores that have to load them at runtime
        //  i don't know why, but just calling SetDllDirectory before bizhawk loads doesn't work.
        //  but there must be a better way than this

        string dllDirFullPath = Path.GetFullPath(dllDir);
        Debug.Log($"Loading dlls from: {dllDirFullPath}");
        _ = SetDllDirectory(dllDirFullPath); // have a feeling this might not work in build

        var libsToLoad = new List<string> {
            // QuickNes (NES)
            "libquicknes.dll",
            // Nymashock (PSX)
            "waterboxhost.dll",
            "libzstd.dll",
            "libbizabiadapter_msabi_sysv.dll",
            // Mupen64 (N64)
            "mupen64plus.dll",
            "mupen64plus-audio-bkm.dll",
            "mupen64plus-input-bkm.dll",
              // "mupen64plus-video-rice.dll",
            "mupen64plus-video-GLideN64.dll",
            "libspeexdsp.dll",
            // Other
            "lua54.dll"

            // TODO load any others we need [or find a way to avoid doing this]
        };
        foreach (string lib in libsToLoad) {
            int e = (int)LoadLibrary(lib);
            if (e == 0) {
                Debug.LogError($"Could not load: {lib}, last error: {GetLastError()}");
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

        _initialized = true;
    }
}
}