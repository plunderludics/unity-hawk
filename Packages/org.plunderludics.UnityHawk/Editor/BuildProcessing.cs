using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace UnityHawk {
// This does two main things:
//  - copy the BizHawk directory (which contains gamedb, etc) into the build
//  - ensure any file dependencies (roms, savestates, etc) from Emulator components are copied into StreamingAssets in the build

// OnProcessScene (called only when scene changes) collects file dependencies per scene for later copying in OnPostprocessBuild.

public class BuildProcessing : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport {
    /// List of assets for each scene that need to be copied into build directory
    /// (keyed by scene path)
    Dictionary<string, HashSet<BizhawkAsset>> _filesForScene = new ();
    
    /// Track the last processed scene path for fallback when no scenes are in build settings
    string _lastProcessedScenePath = null;

    ///// IOrderedCallback
    public int callbackOrder => 0;

    ///// IPreprocessBuildWithReport
    public void OnPreprocessBuild(BuildReport report) {
        // Don't actually need to do anything here
    }

    ///// IProcessSceneWithReport
    /// This gets called for each scene in the build, but only when changes are made to the scene.
    /// When changes are made, clear the list of files to copy and regenerate
    public void OnProcessScene(Scene scene, BuildReport report) {
        Debug.Log($"[unity-hawk] OnProcessScene({scene.path})");
        if (BuildPipeline.isBuildingPlayer) { // Only process when actually building
            CollectSceneFiles(scene);
            _lastProcessedScenePath = scene.path;
        }
    }

    ///// IPostprocessBuildWithReport
    public void OnPostprocessBuild(BuildReport report) {
        Debug.Log("[unity-hawk] OnPostprocessBuild");

        var exePath = report.summary.outputPath;

        // Copy all the collected files to the build directory
        var nFilesCopied = CopyFilesToBuild(exePath);
        Debug.Log($"[unity-hawk] copied {nFilesCopied} files to build directory");

        if (nFilesCopied == 0) {
            // There's no file dependencies so I think we can assume bizhawk is not actually being used, don't copy it into build
            // (Are there any weird edge cases where this would not be true? I guess if there's an Emulator component that's only used to load assets from external paths)
            Debug.LogWarning($"[unity-hawk] no UnityHawk assets are used in this build so not copying BizHawk into build");
        } else {
            // Gotta make sure all the bizhawk stuff gets into the build
            // Copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into xxx_Data/org.plunderludics.UnityHawk/BizHawk/
            // [kinda sucks but don't know a better way]
            var targetDir = Path.Combine(GetBuildDataDir(exePath), Paths.BizhawkDirRelative);
            Debug.Log($"[unity-hawk] copying bizhawk directory from: {Path.GetFullPath(Paths.BizHawkDir)} to {Path.GetFullPath(targetDir)}");
            Directory.CreateDirectory(targetDir);
            FileUtil.ReplaceDirectory(Path.GetFullPath(Paths.BizHawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]
        }
    }

    ///// commands
    /// Collect and store all the bizhawk assets that need to be copied for a scene
    void CollectSceneFiles(Scene scene) {
        Debug.Log($"[unity-hawk] CollectSceneFiles({scene.path})");
        // Scene has been modified, clear list of files and regenerate
        _filesForScene[scene.path] = new();

        // TODO: Look for BuildSettings component in the scene

        // look through all Emulator components in the scene and
        // locate (external) file dependencies and collect them for later copying
        var root = scene.GetRootGameObjects();
        var dependencies = EditorUtility.CollectDependencies(root);
        var bizhawkDependencies = dependencies.OfType<BizhawkAsset>().ToList();

        Debug.Log($"[unity-hawk] collected {bizhawkDependencies.Count} dependencies for scene {scene.path}: \n {string.Join("\n", bizhawkDependencies)}");

        // Add assets that need to be copied
        foreach (var dependency in bizhawkDependencies) {
            if (!_filesForScene[scene.path].Contains(dependency)) {
                _filesForScene[scene.path].Add(dependency);
            }
        }
    }

    /// Copy all collected files for each scene to the build directory
    /// (This will do multiple copies for assets that are used in multiple scenes,
    ///  but doesn't really matter - they go to the same path in the build directory)
    /// returns the number of files copied
    int CopyFilesToBuild(string exePath) {
        var bizhawkAssetsPath = Path.Combine(GetBuildDataDir(exePath), Paths.BizHawkAssetsDirName);
        Directory.CreateDirectory(bizhawkAssetsPath);

        int nFilesCopied = 0;

        var scenePaths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
        if (scenePaths.Count == 0) {
            // Kinda hacky, but it seems like when there are no scenes in build settings,
            // Unity falls back to the currently active scene,
            // Which i /think/ should be the last processed scene
            // TODO: Is there a more robust way to handle this?
            if (_lastProcessedScenePath == null) {
                throw new Exception($"[unity-hawk] no scenes in build settings and no last processed scene");
            }
            scenePaths.Add(_lastProcessedScenePath);
            Debug.Log($"[unity-hawk] no scenes in build settings, fallback to last processed scene ({_lastProcessedScenePath})");
        }
        foreach (var scenePath in scenePaths) {
            if (!_filesForScene.ContainsKey(scenePath)) {
                throw new Exception($"[unity-hawk] scene {scenePath} is in build but has not been processed");
            }

            var sceneFiles = _filesForScene[scenePath];
            Debug.Log($"[unity-hawk] for scene {scenePath}: copying {sceneFiles.Count} files to {bizhawkAssetsPath}");
            foreach (var file in sceneFiles) {
                if (!file) {
                    Debug.LogWarning($"[unity-hawk] for scene {scenePath}: null BizHawkAsset encountered");
                    continue;
                }
                MoveToDirectory(false, file, bizhawkAssetsPath);
                nFilesCopied++;
            }
        }

        return nFilesCopied;
    }

    /// moves an asset to a new directory
    static void MoveToDirectory(bool isDir, BizhawkAsset asset, string baseTargetDir) {
        if (!asset) {
            throw new ArgumentNullException($"[unity-hawk] MoveToDirectory: asset is null");
        }

        // Get the path that Emulator will actually look for the file
        var inPath = Paths.GetAssetPath(asset);

        // the relative path of the file
        var inRelativePath = asset.Location;

        // And copy into the right location in the build (xxx_Data/BizhawkAssets/relative/path)
        var outPath = Path.Combine(baseTargetDir, inRelativePath);
        var outDir = Path.GetDirectoryName(outPath);

        Debug.Log($"[unity-hawk] Copy from: {inPath} to {outPath}");

        Directory.CreateDirectory(outDir!);
        if (!File.Exists(outPath)) {
            if (isDir) {
                FileUtil.ReplaceDirectory(inPath, outPath);
            } else {
                FileUtil.ReplaceFile(inPath, outPath);
            }
        }

        var inExt = Path.GetExtension(inPath).ToLowerInvariant();
        // This is annoying, but some roms (e.g. for PSX) are .cue files, and those point to other file dependencies,
        // so we need to copy those files over as well (without renaming)
        // [if there are other special cases we have to handle like this we should probably rethink this whole approach tbh - too much work]
        // [definitely at least need an alternative in case this has issues (one way would be to just use StreamingAssets)]
        // this cuefile resolver seems to basically find files with similar names and correct extensions in the same directory
        if (inExt is ".cue" or ".ccd") {
            var inNoExt = Path.GetFileNameWithoutExtension(inPath);

            var fileInfos = new FileInfo(inPath).Directory?.GetFiles();
            foreach (var other in fileInfos!) {
                var otherExt = Path.GetExtension(other.FullName).ToLowerInvariant();

                // ignore self, archives and unity meta files
                if (otherExt is ".cue" or ".ccd" or ".meta" or ".7z" or ".rar" or ".zip" or ".bz2" or ".gz") {
                    continue;
                }

                var otherNoExt = Path.GetFileNameWithoutExtension(other.FullName);
                if (string.Equals(otherNoExt, inNoExt, StringComparison.InvariantCultureIgnoreCase)) {
                    var otherOutPath = Path.Combine(outDir, other.Name);
                    Debug.Log($"[unity-hawk] Copy from: {other.FullName} to {otherOutPath}");
                    if (!File.Exists(otherOutPath)) {
                        File.Copy(other.FullName, otherOutPath, overwrite: true);
                    }
                }
            }
        }
    }

    ///// queries
    static string GetBuildDataDir(string exePath) {
        return Path.Combine(Path.GetDirectoryName(exePath)!, $"{Path.GetFileNameWithoutExtension(exePath)}_Data");
    }
}

}