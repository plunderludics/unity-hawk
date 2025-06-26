using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityHawk {

// TODO: make a bunch of these paths configurable in some config file
public static class Paths {
    ///// exe
    private static readonly string packageName = "org.plunderludics.UnityHawk";
    public static readonly string BizhawkDirRelative = Path.Combine(packageName, "BizHawk~");
    private static readonly string _bizhawkDirForEditor = Path.Combine("Packages", BizhawkDirRelative);
    // [^ seems insane but somehow Unity makes this work whether the package is in the Library/PackageCache/ dir or the Packages/ dir
    //  maybe not the best idea to rely on this dark behaviour though..]

    // In the build put the bizhawk stuff inside "xx_Data/com.plunderludics.UnityHawk/BizHawk"
    public static string BizHawkDir =>
#if UNITY_EDITOR
        _bizhawkDirForEditor;
#else
        Path.Combine(Application.dataPath, BizhawkDirRelative);
#endif

    private static readonly string _emuhawkExeName =
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        "EmuHawk-Headless.exe";
#else
        "EmuHawk.exe";
#endif

    public static readonly string emuhawkExePath = Path.Combine(BizHawkDir, _emuhawkExeName);

    public static readonly string defaultBizhawkConfigPath = Path.Combine(BizHawkDir, "config.ini");
    public static readonly string defaultUnityHawkConfigPath = Path.Combine("Packages", packageName, "Runtime/UnityHawkConfigDefault.asset");

    public static readonly string dllDir = Path.Combine(BizHawkDir, "dll");

    ///// assets
    public const string BizHawkAssetsDirName = "BizhawkAssets";
    private static readonly string _bizhawkAssetsDirForEditor = Application.dataPath;
    public static readonly string BizHawkAssetsDirForBuild = Path.Combine(Application.dataPath, BizHawkAssetsDirName);

    public static string BizHawkAssetsDir =>
#if UNITY_EDITOR
        _bizhawkAssetsDirForEditor;
    #else
        BizHawkAssetsDirForBuild;
#endif

    public static readonly string RamWatchPath = BizHawkAssetsDir;
    public static readonly object SavestatesOutputPath = BizHawkAssetsDir;

    // Returns the path that will be loaded for a filename param (rom, lua, config, savestate)
    public static string GetFullPath(string path) {
        if (!string.IsNullOrEmpty(path) && path == Path.GetFullPath(path)) {
            // Already an absolute path, don't change it [Path.Combine below will do this anyway but just to be explicit]
            return path;
        }

        return Path.Combine(BizHawkAssetsDir, path);
    }

    // this uses the fact that Paths.cs treats absolute paths differently than relative
    public static string GetAssetPath(BizhawkAsset asset) {
        #if UNITY_EDITOR
        // if we return an absolute path, in editor, we don't need any preprocessing

        // ignore asset path in editor, just get its location
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetDatabase.GetAssetPath(asset)));
        #endif

        return Path.Combine(BizHawkAssetsDirForBuild, asset.Location);
    }
}

}