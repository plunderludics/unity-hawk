using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;

namespace UnityHawk {
// This does two main things:
//  - copy the BizHawk directory (which contains gamedb, etc) into the build
//  - ensure any file dependencies (roms, savestates, etc) from Emulator components are copied into StreamingAssets in the build


// TODO: the way this is set up is not really correct; OnProcessScene should alter the scene (set params on the Emulator components)
//       but not actually copy the files. It should just store a list of files that need to be copied and then all the copying should
//       happen in OnPostprocessBuild. This would function properly with unity's scene caching behaviour and remove the need for the weird hack
//       below in OnPreprocessBuild

public class BuildProcessing : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport {
    /// if the scene was processed or not
    bool _didProcessScene = false;

    ///// IOrderedCallback
    public int callbackOrder => 0;

    ///// IPreprocessBuildWithReport
    public void OnPreprocessBuild(BuildReport report) {
        // This is so dumb, but it seems like OnProcessScene only gets called for the first build after a scene is saved
        // (because incremental build means a scene won't get re-built if it hasn't changed)
        // So we make some trivial change to the scene so Unity thinks it's changed and calls the OnProcessScene callback
        for (var i = -1; i < SceneManager.sceneCountInBuildSettings; i++) {
            var scene = (i == -1) ? SceneManager.GetActiveScene() : SceneManager.GetSceneByBuildIndex(i);
            const string n = "unityhawk-fake-object-to-force-rebuild-scene";
            var g = GameObject.Find(n);
            if (!g) {
                g = new GameObject(n);
                g.hideFlags = HideFlags.HideInHierarchy;
            }
            g.transform.position += Vector3.down;

            // Seems to be necessary
            EditorSceneManager.SaveScene(scene);
        }
    }

    ///// IProcessSceneWithReport
    public void OnProcessScene(Scene scene, BuildReport report) {
        if (BuildPipeline.isBuildingPlayer) {
            ProcessScene(scene, report.summary.outputPath);
            _didProcessScene = true;
        }
    }

    ///// IPostprocessBuildWithReport
    public void OnPostprocessBuild(BuildReport report) {
        Debug.Log("OnPostprocessBuild");
        if (!_didProcessScene) {
            throw new BuildFailedException("OnProcessScene was not called");
        }

        // Gotta make sure all the bizhawk stuff gets into the build
        // Copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into xxx_Data/org.plunderludics.UnityHawk/BizHawk/
        // [kinda sucks but don't know a better way]
        var exePath = report.summary.outputPath;
        var targetDir = Path.Combine(GetBuildDataDir(exePath), Paths.BizhawkDirRelative);
        Debug.Log($"from: {Path.GetFullPath(Paths.BizHawkDir)} to {Path.GetFullPath(targetDir)}");
        Directory.CreateDirectory(targetDir);
        FileUtil.ReplaceDirectory(Path.GetFullPath(Paths.BizHawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]
    }

    ///// commands
    static void ProcessScene(Scene scene, string exePath) {
        // Need to create the build dir in advance so we can copy files in there before the build actually happens
        var bizhawkAssetsPath = Path.Combine(GetBuildDataDir(exePath), Paths.BizHawkAssetsDirName);
        Directory.CreateDirectory (bizhawkAssetsPath);

        // look through all Emulator components in the scene and
        // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
        var root = scene.GetRootGameObjects();
        var dependencies = EditorUtility.CollectDependencies(root);
        var bizhawkDependencies = dependencies.OfType<BizhawkAsset>().ToList();

        Debug.Log($"[unity-hawk] build processing, moving dependencies to {bizhawkAssetsPath}: \n {string.Join("\n", bizhawkDependencies)}");

        foreach (var dependency in bizhawkDependencies) {
            MoveToDirectory(false, dependency, bizhawkAssetsPath);
        }
    }

    /// moves an asset to a new directory
    static void MoveToDirectory(bool isDir, BizhawkAsset asset, string baseTargetDir) {
        if (!asset) {
            return;
        }

        // Get the path that Emulator will actually look for the file
        var inPath = Paths.GetAssetPath(asset);

        // the relative path of the file
        var inRelativePath = asset.Location;

        // And copy into the right location in the build (xxx_Data/BizhawkAssets/relative/path)
        var outPath = Path.Combine(baseTargetDir, inRelativePath);
        var outDir = Path.GetDirectoryName(outPath);

        Debug.Log($"Copy from: {inPath} to {outPath}");

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
                    Debug.Log($"Copy from: {other.FullName} to {otherOutPath}");
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