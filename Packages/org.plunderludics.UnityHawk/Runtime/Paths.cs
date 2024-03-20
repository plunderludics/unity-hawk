using System;
using System.IO;
using UnityEngine;

namespace UnityHawk {

public static class Paths
{
    public static readonly string packageName = "org.plunderludics.UnityHawk";
    public static readonly string bizhawkDirRelative = Path.Combine(packageName, "BizHawk~");
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

    private static readonly string _emuhawkExeName = 
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        "EmuHawk-Headless.exe";
#else
        "EmuHawk.exe";
#endif

    public static readonly string emuhawkExePath = Path.Combine(bizhawkDir, _emuhawkExeName);

    public static readonly string defaultConfigPath = Path.Combine(bizhawkDir, "config.ini");

    public static readonly string dllDir = Path.Combine(bizhawkDir, "dll");

    // Returns the path that will be loaded for a filename param (rom, lua, config, savestate)
    public static string GetAssetPath(string path) {
        if (!string.IsNullOrEmpty(path) && path == Path.GetFullPath(path)) {
            // Already an absolute path, don't change it [Path.Combine below will do this anyway but just to be explicit]
            return path;
        } else {
            return Path.Combine(Application.streamingAssetsPath, path); // Load relative to StreamingAssets/
        }
    }

    public static string GetRelativePath(string filepath, string folder) {
        Uri pathUri = new Uri(filepath);
        // Folders must end in a slash
        if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString())) {
            folder += Path.DirectorySeparatorChar;
        }
        Uri folderUri = new Uri(folder);
        return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    // [https://stackoverflow.com/a/74401631]
    public static bool IsSubPath(string parent, string child)
    {
        try
        {
            parent = Path.GetFullPath(parent);
            if (!parent.EndsWith(Path.DirectorySeparatorChar.ToString()))
                parent = parent + Path.DirectorySeparatorChar;
            child = Path.GetFullPath(child);
            if (child.Length <= parent.Length)
                return false;
            return child.StartsWith(parent);
        }
        catch
        {
            return false;
        }
    }
}
}