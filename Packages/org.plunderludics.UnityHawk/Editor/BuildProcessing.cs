using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CueSharp;
using UnityEditor.SceneManagement;

namespace UnityHawk {
// This does two main things:
//  - copy the BizHawk directory (which contains gamedb, etc) into the build
//  - ensure any file dependencies (roms, savestates, etc) from Emulator components are copied into StreamingAssets in the build


// TODO: the way this is set up is not really correct; OnProcessScene should alter the scene (set params on the Emulator components)
//       but not actually copy the files. It should just store a list of files that need to be copied and then all the copying should
//       happen in OnPostprocessBuild. This would function properly with unity's scene caching behaviour and remove the need for the weird hack
//       below in OnPreprocessBuild

public class BuildProcessing : IPostprocessBuildWithReport, IPreprocessBuildWithReport, IProcessSceneWithReport
{
    public int callbackOrder => 0;

    private bool _didProcessScene = false;

    public void OnPreprocessBuild(BuildReport report) {
        // Debug.Log("OnPreprocessBuild");
        // This is so dumb but it seems like OnProcessScene only gets called for the first build after a scene is saved
        // (because incremental build means a scene won't get re-built if it hasn't changed)
        // So we make some trivial change to the scene so Unity thinks it's changed and calls the OnProcessScene callback
        for (var i = -1; i < SceneManager.sceneCountInBuildSettings; i++) {
            var scene = (i == -1) ? SceneManager.GetActiveScene() : SceneManager.GetSceneByBuildIndex(i);
            var n = "unityhawk-fake-object-to-force-rebuild-scene";
            var g = GameObject.Find(n);
            if (g == null) {
                g = new GameObject(n);
                g.hideFlags = HideFlags.HideInHierarchy;
            }
            g.transform.position += Vector3.down;

            EditorSceneManager.SaveScene(scene); // Seems to be necessary 
        }
    }

    public void OnProcessScene(Scene scene, BuildReport report) {
        // Debug.Log("OnProcessScene");
        if (BuildPipeline.isBuildingPlayer) {
            ProcessScene(scene, report.summary.outputPath);
            _didProcessScene = true;
        }
    }

    private static void ProcessScene(Scene scene, string exePath) {
        // Debug.Log("ProcessScene");
        // Need to create the build dir in advance so we can copy files in there before the build actually happens
        var bizhawkAssetsPath = Path.Combine(GetBuildDataDir(exePath), Paths.BizHawkAssetsDirName);
        Directory.CreateDirectory (bizhawkAssetsPath);

        // look through all Emulator components in the scene and
        // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
        var root = scene.GetRootGameObjects();
        var dependencies = EditorUtility.CollectDependencies(root);
        var bizhawkDependencies = dependencies.OfType<BizhawkAsset>();

        Debug.Log($"[unity-hawk] build processing, moving dependencies to {bizhawkAssetsPath}: \n {string.Join("\n", bizhawkDependencies)}");

        foreach (var dependency in bizhawkDependencies) {
            MoveToDirectory(false, dependency, bizhawkAssetsPath);
        }
    }

    /// moves an asset to a new directory
    static void MoveToDirectory(bool isDir, BizhawkAsset asset, string targetDir) {
        if (asset == null) {
            return;
        }

        // Get the path that Emulator will actually look for the file
        var origFilePath = Paths.GetAssetPath(asset);

        var newFileName = asset.Location;

        // And copy into the right location in the build (xxx_Data/BizhawkAssets/relative/path)
        var newFilePath = Path.Combine(targetDir, newFileName);
        Debug.Log($"Copy from: {origFilePath} to {newFilePath}");
        Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));
        if (!File.Exists(newFilePath)) {
            if (isDir) {
                FileUtil.ReplaceDirectory(origFilePath, newFilePath);
            } else {
                FileUtil.ReplaceFile(origFilePath, newFilePath);
            }
        }

        if (Path.GetExtension(origFilePath) == ".cue") {
            // This is annoying, but some roms (e.g. for PSX) are .cue files, and those point to other file dependencies
            // so we need to copy those files over as well (without renaming)
            // [if there are other special cases we have to handle like this we should probably rethink this whole approach tbh - too much work]
            // [definitely at least need an alternative in case this has issues (one way would be to just use StreamingAssets)]

            // this also has a minor bug that if separate .bin files have the same name they'll get clobbered
            // probably has a ton of edge cases that will break too
            var cueSheet = new CueSheet(origFilePath);
            // Copy all the .bin files that are referenced
            var cueFileParentDir = Path.GetDirectoryName(origFilePath);
            foreach (var t in cueSheet.Tracks) {
                var binFileName = t.DataFile.Filename;
                if (binFileName != Path.GetFileName(binFileName)) {
                    throw new ArgumentException("UnityHawk build script doesn't support .cue files that reference files in a different directory");
                }
                // bin file pathname is relative to the directory of the cue file
                var binPath = Path.Combine(cueFileParentDir, binFileName);
                var cueFilePath = Path.Combine(targetDir, binFileName);
                Debug.Log($"Copy from: {binPath} to {cueFilePath}");
                if (!File.Exists(cueFilePath)) {
                    File.Copy(binPath, cueFilePath, overwrite: true);
                }
            }
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        Debug.Log("OnPostprocessBuild");
        if (!_didProcessScene) {
            throw new BuildFailedException("OnProcessScene was not called");
        }

        var exePath = report.summary.outputPath;
        // Gotta make sure all the bizhawk stuff gets into the build
        // Copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into xxx_Data/org.plunderludics.UnityHawk/BizHawk/

        // [kinda sucks but don't know a better way]
        var targetDir = Path.Combine(GetBuildDataDir(exePath), Paths.BizhawkDirRelative);
        Debug.Log($"from: {Path.GetFullPath(Paths.BizHawkDir)} to {Path.GetFullPath(targetDir)}");
        Directory.CreateDirectory(targetDir);
        FileUtil.ReplaceDirectory(Path.GetFullPath(Paths.BizHawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]
    }

    static string GetBuildDataDir(string exePath) {
        return Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath)+"_Data");
    }
}
}