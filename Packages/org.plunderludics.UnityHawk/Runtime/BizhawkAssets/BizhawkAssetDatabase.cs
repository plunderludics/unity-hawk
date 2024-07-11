using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityHawk {
public static class BizhawkAssetDatabase {
    // this uses the fact that Paths.cs treats absolute paths differently than relative
    public static string GetFullPath(BizhawkAsset asset) {
        #if UNITY_EDITOR
        // if we return an absolute path, in editor, we don't need any preprocessing
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetDatabase.GetAssetPath(asset)));
        #endif

        // this depends on BuildProcessing.cs actually adding every asset to streaming assets
        return asset.Path;
    }
}
}