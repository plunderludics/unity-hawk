using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using System;
using System.IO;
using System.Collections.Generic;

namespace UnityHawk {

// NOTE if this seems to not be working -
// Make sure every scene is actually added to the list of 'Scenes in Build'
// otherwise this won't work.

// This does two main things:
//  - copy the BizHawk directory (which contains gamedb, etc) into the build
//  - locate any custom file dependencies from UnityHawk.Emulator components (ie roms, config, lua, savestates)
//     and copy those into the build too (as well as temporarily updating the reference in the Emulator component)

public class BuildProcessing : IPostprocessBuildWithReport, IPreprocessBuildWithReport, IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) {
        
    }

    // This seems to only get run about 40% of the time, annoying :/
    public void OnProcessScene(Scene scene, BuildReport report) {
        Debug.Log("OnProcessScene");
        if (report == null) {
            // This means the callback is being called during script compilation within the editor,
            // not a build, so no need to do anything
            return;
        }
        ProcessScene(scene, report.summary.outputPath);
    }

    public static void ProcessScene(Scene scene, string exePath) {
        // Need to create the build dir in advance so we can copy files in there before the build actually happens
        string newDataDir = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath)+"_Data");
        Directory.CreateDirectory (newDataDir);

        // look through all Emulator components in the scene and
        // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
        GameObject[] gameObjects = scene.GetRootGameObjects();
        foreach (var gameObject in gameObjects) {
            var emulators = gameObject.GetComponentsInChildren<Emulator>(includeInactive: true);
            foreach (Emulator emulator in emulators) {
                // define abstract getters and setters just to avoid duplicating code for each field
                var accessors = new List<(Func<string>, Action<string>)> {
                    (() => emulator.romFileName, fn => emulator.romFileName = fn),
                    (() => emulator.configFileName, fn => emulator.configFileName = fn),
                    (() => emulator.saveStateFileName, fn => emulator.saveStateFileName = fn),
                };
                for (int i = 0; i < emulator.luaScripts.Count; i++) {
                    int j = i;
                    Debug.Log(emulator.luaScripts[j]);
                    accessors.Add((() => emulator.luaScripts[j], fn => emulator.luaScripts[j] = fn));
                }
                foreach ((var getter, var setter) in accessors) {
                    string path = getter();
                    if (!string.IsNullOrEmpty(path)) {
                        // Get the path that Emulator will actually look for the file
                        string origFilePath = Emulator.GetAbsolutePath(path);
                        // Use a hash of the abs path for the new file, so that files won't conflict
                        // but also if a file is used multiple times it won't be duplicated in the build
                        // also need to preserve the file extension since bizhawk uses that to determine the platform
                        string newFileName = origFilePath.GetHashCode().ToString("x") + Path.GetExtension(path);
                        // Set the emulator to point to the new file [don't worry, this change doesn't persist in the scene, only affects the build]
                        setter(newFileName);
                        // And copy into the right location in the build (xxx_Data/<hash>)
                        string newFilePath = Path.Combine(newDataDir, newFileName);
                        Debug.Log($"copy from: {origFilePath} to {newFilePath}");
                        File.Copy(origFilePath, newFilePath, overwrite: true);
                    }
                }
                // emulator.romFileName = "wa"; // [cool, this change seems to not persist after the build is done, so no need to clean up afterwards]
                // TODO: if the filename is an absolute path,
                // then copy it to somewhere within the build directory, and change romFileName to point to that

            }
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        string exePath = report.summary.outputPath;
        // Gotta make sure all the bizhawk stuff gets into the build
        // Just copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into the build (with the same path relative to the exe)

        // [kinda sucks but don't know a better way]
        var targetDir = Path.Combine(Path.GetDirectoryName(exePath), UnityHawk.bizhawkDir);
        Debug.Log($"from: {Path.GetFullPath(UnityHawk.bizhawkDir)} to {Path.GetFullPath(targetDir)}");
        Directory.CreateDirectory(targetDir);
        FileUtil.ReplaceDirectory(Path.GetFullPath(UnityHawk.bizhawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]

        // TODO: we should separate the dlls needed by Unity (i.e. BizHawk.Client.Common, etc)
        // and the ones loaded at runtime within BizHawk (like waterboxhost.dll, etc)
        // currently we end up with two copies of every dll in the build which is stupid
        // [one way to do this would be leave the file structure as is, disable the runtime dlls from being exported to the standalone by Unity,
        //  and then just manually copy them into dataDir in this script]
    }
}
}