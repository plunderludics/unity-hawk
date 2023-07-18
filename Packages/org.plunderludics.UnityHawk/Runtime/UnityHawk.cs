// global manager thing that does one-time initialization of all the stuff BizHawk expects (db, etc)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    private static readonly string _bizhawkDirForEditor = Path.Combine("Packages", bizhawkDirRelative);

    // [^ seems insane but somehow Unity makes this work whether the package is in the Library/PackageCache/ dir or the Packages/ dir
    //  maybe not the best idea to rely on this dark behaviour though..]

    // In the build put the bizhawk stuff inside "xx_Data/com.plunderludics.UnityHawk/BizHawk"
    private static readonly string _bizhawkDirForBuild = Path.Combine(Application.dataPath, bizhawkDirRelative);
    
    public static readonly string bizhawkDir =
#if UNITY_EDITOR
        _bizhawkDirForEditor;
#else
        _bizhawkDirForBuild;
#endif

    public static readonly string emuhawkExePath = Path.Combine(bizhawkDir, "EmuHawk.exe");

    public static readonly string defaultConfigPath = Path.Combine(bizhawkDir, "config.ini");
    
    public static readonly string dllDir = Path.Combine(bizhawkDir, "dll");
}
}