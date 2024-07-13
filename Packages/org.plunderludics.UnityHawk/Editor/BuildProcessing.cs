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
    public bool _didProcessScene = false;
    public void OnPreprocessBuild(BuildReport report) {
        // Debug.Log("OnPreprocessBuild");
        // This is so dumb but it seems like OnProcessScene only gets called for the first build after a scene is saved
        // (because incremental build means a scene won't get re-built if it hasn't changed)
        // So we make some trivial change to the scene so Unity thinks it's changed and calls the OnProcessScene callback
        for (int i = -1; i < SceneManager.sceneCountInBuildSettings; i++) {
            Scene scene = (i == -1) ? SceneManager.GetActiveScene() : SceneManager.GetSceneByBuildIndex(i); 
            string n = "unityhawk-fake-object-to-force-rebuild-scene";
            GameObject g = GameObject.Find(n);
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

    public static void ProcessScene(Scene scene, string exePath) {
        // Debug.Log("ProcessScene");
        // Need to create the build dir in advance so we can copy files in there before the build actually happens
        string streamingAssetsBuildDir = Path.Combine(GetBuildDataDir(exePath), "StreamingAssets");
        Directory.CreateDirectory (streamingAssetsBuildDir);

        // look through all Emulator components in the scene and
        // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
        var root = scene.GetRootGameObjects();
        var dependencies = EditorUtility.CollectDependencies(root);
        var bizhawkDependencies = dependencies.OfType<BizhawkAsset>();

        Debug.Log($"[unity-hawk] build processing, moving dependencies to StreamingAssets: \n {string.Join("\n", bizhawkDependencies)}");

        foreach (var dependency in bizhawkDependencies) {
            MoveToDirectory(false, BizhawkAssetDatabase.GetFullPath(dependency), streamingAssetsBuildDir);
        }

        foreach (var gameObject in root) {
            // RecurseGameObjects(gameObject);
            // comb through the entire scene
            var emulators = gameObject.GetComponentsInChildren<Emulator>(includeInactive: true);
            // TODO: with the BizhawkAssetDatabase, all the emulator pathnames can go away
            foreach (Emulator emulator in emulators) {
                if ((!emulator.enabled || !emulator.gameObject.activeInHierarchy) && !emulator.forceCopyFilesToBuild) {
                    // If emulator component is not active, ignore it unless the forceCopyFilesToBuild flag is set
                    continue;
                }
                if (!emulator.useManualPathnames) {
                    // The default way for an Emulator to be set up is using DefaultAssets to refer file dependencies
                    // which is convenient in the editor. But we don't want the files to be packed into the binary resource file,
                    // they need to remain as separate files on disk. So at build time (ie here) we convert the emulator to 'use manual pathnames'
                    // hardcode the paths to the file locations (/Assets/x/y/z) and remove the DefaultAsset references so the files don't get packed.
                    // The rest of the code after this block is already set up to handle the case of hardcoded pathnames, copying them into the StreamingAssets directory in the build
                    emulator.SetFilenamesFromAssetReferences();

                    emulator.romFile = null;
                    emulator.saveStateFile = null;
                    emulator.configFile = null;
                    emulator.luaScriptFile = null;
                    // emulator.firmwareDirectory = null;
                    emulator.savestatesOutputDirectory = null;

                    emulator.useManualPathnames = true;
                }

                // define abstract getters and setters just to avoid duplicating code for each field
                var accessors = new List<(bool, Func<string>, Action<string>)> {
                    (false, () => emulator.romFileName, fn => emulator.romFileName = fn),
                    (false, () => emulator.configFileName, fn => emulator.configFileName = fn),
                    (false, () => emulator.saveStateFileName, fn => emulator.saveStateFileName = fn),
                    (false, () => emulator.luaScriptFileName, fn => emulator.luaScriptFileName = fn),
                    (true,  () => emulator.firmwareDirName, fn => emulator.firmwareDirName = fn)
                    // ignore savestatesdirectory because it gets ignored in the build anyway
                };

                foreach ((bool isDir, var getter, var setter) in accessors) {
                    // Set the emulator to point to the new file [don't worry, this change doesn't persist in the scene, only affects the build]
                    // basically have to redo this so that the emulator points to the correct files
                    // maybe this can be completely removed (except for the firmware folder)
                    setter(MoveToDirectory(isDir, getter(), streamingAssetsBuildDir));
                }
            }
        }
    }

    /// moves an asset to a new directory (so that we can place things in the streaming assets folder)
    static string MoveToDirectory(bool isDir, string path, string targetDir) {
        if (!string.IsNullOrEmpty(path)) {
            // Get the path that Emulator will actually look for the file
            string origFilePath = Paths.GetAssetPath(path);

            // ignore anything within StreamingAssets/ since those get copied over already by Unity
            // Debug.Log($"Check isSubPath {origFilePath}, {Application.streamingAssetsPath}");
            if (Paths.IsSubPath(Application.streamingAssetsPath, origFilePath)) {
                // Debug.Log($"Skipping copy for: {path}");
                // Ensure filepath is relative (to StreamingAssets/)
                string newFile = Paths.GetRelativePath(origFilePath, Application.streamingAssetsPath);
                return newFile;
            }

            string newFileName = Path.GetRelativePath(Application.dataPath, origFilePath);

            // And copy into the right location in the build (xxx_Data/StreamingAssets/relative/path)
            string newFilePath = Path.Combine(targetDir, newFileName);
            Debug.Log($"Copy from: {origFilePath} to {newFilePath}");
            Directory.CreateDirectory(Path.GetDirectoryName(newFilePath));
            if (!File.Exists(newFilePath)) {
                if (isDir) {
                    FileUtil.ReplaceDirectory(origFilePath, newFilePath);
                } else {
                    FileUtil.ReplaceFile(origFilePath, newFilePath);
                }
            }

            if (Path.GetExtension(path) == ".cue") {
                // This is annoying, but some roms (e.g. for PSX) are .cue files, and those point to other file dependencies
                // so we need to copy those files over as well (without renaming)
                // [if there are other special cases we have to handle like this we should probably rethink this whole approach tbh - too much work]
                // [definitely at least need an alternative in case this has issues (one way would be to just use StreamingAssets)]

                // this also has a minor bug that if separate .bin files have the same name they'll get clobbered
                // probably has a ton of edge cases that will break too
                var cueSheet = new CueSheet(origFilePath);
                // Copy all the .bin files that are referenced
                string cueFileParentDir = Path.GetDirectoryName(origFilePath);
                foreach (Track t in cueSheet.Tracks) {
                    string binFileName = t.DataFile.Filename;
                    if (binFileName != Path.GetFileName(binFileName)) {
                        throw new ArgumentException("UnityHawk build script doesn't support .cue files that reference files in a different directory");
                    }
                    // bin file pathname is relative to the directory of the cue file
                    string binPath = Path.Combine(cueFileParentDir, binFileName);
                    var cueFilePath = Path.Combine(targetDir, binFileName);
                    Debug.Log($"Copy from: {binPath} to {cueFilePath}");
                    if (!File.Exists(cueFilePath)) {
                        File.Copy(binPath, cueFilePath, overwrite: true);
                    }
                }
            }

            return newFileName;
        }

        return "";
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        Debug.Log("OnPostprocessBuild");
        if (!_didProcessScene) {
            throw new BuildFailedException("OnProcessScene was not called");
        }

        string exePath = report.summary.outputPath;
        // Gotta make sure all the bizhawk stuff gets into the build
        // Copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into xxx_Data/org.plunderludics.UnityHawk/BizHawk/

        // [kinda sucks but don't know a better way]
        var targetDir = Path.Combine(GetBuildDataDir(exePath), Paths.bizhawkDirRelative);
        Debug.Log($"from: {Path.GetFullPath(Paths.bizhawkDir)} to {Path.GetFullPath(targetDir)}");
        Directory.CreateDirectory(targetDir);
        FileUtil.ReplaceDirectory(Path.GetFullPath(Paths.bizhawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]
    }

    static string GetBuildDataDir(string exePath) {
        return Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath)+"_Data");
    }
}
}