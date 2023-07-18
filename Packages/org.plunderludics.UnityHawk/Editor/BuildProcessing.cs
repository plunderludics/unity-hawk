// using UnityEditor;
// using UnityEngine;
// using UnityEngine.SceneManagement;

// using UnityEditor.Build;
// using UnityEditor.Build.Reporting;

// using System;
// using System.IO;
// using System.Collections.Generic;

// using CueSharp;

// namespace UnityHawk {

// // This does two main things:
// //  - copy the BizHawk directory (which contains gamedb, etc) into the build
// //  - locate any custom file dependencies from UnityHawk.Emulator components (ie roms, config, lua, savestates)
// //     and copy those into the build too (as well as temporarily updating the reference in the Emulator component)
// //     (this second part is only necessary if you don't put your dependencies in StreamingAssets/
// //      and it's kind of flaky so using StreamingAssets should probably be the recommended method)

// // NOTE if this seems to not be working -
// // Make sure every scene is actually added to the list of 'Scenes in Build'
// // otherwise this won't work.

// public class BuildProcessing : IPostprocessBuildWithReport, IPreprocessBuildWithReport, IProcessSceneWithReport
// {
//     public int callbackOrder => 0;
//     public void OnPreprocessBuild(BuildReport report) {
        
//     }

//     // This seems to only get run about 40% of the time, annoying :/
//     public void OnProcessScene(Scene scene, BuildReport report) {
//         Debug.Log("OnProcessScene");
//         if (report == null) {
//             // This means the callback is being called during script compilation within the editor,
//             // not a build, so no need to do anything
//             return;
//         }
//         ProcessScene(scene, report.summary.outputPath);
//     }

//     public static void ProcessScene(Scene scene, string exePath) {
//         // Need to create the build dir in advance so we can copy files in there before the build actually happens
//         string streamingAssetsBuildDir = Path.Combine(GetBuildDataDir(exePath), "StreamingAssets");
//         Directory.CreateDirectory (streamingAssetsBuildDir);

//         // look through all Emulator components in the scene and
//         // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
//         GameObject[] gameObjects = scene.GetRootGameObjects();
//         foreach (var gameObject in gameObjects) {
//             var emulators = gameObject.GetComponentsInChildren<Emulator>(includeInactive: true);
//             foreach (Emulator emulator in emulators) {
//                 // define abstract getters and setters just to avoid duplicating code for each field
//                 var accessors = new List<(Func<string>, Action<string>)> {
//                     (() => emulator.romFileName, fn => emulator.romFileName = fn),
//                     (() => emulator.configFileName, fn => emulator.configFileName = fn),
//                     (() => emulator.saveStateFileName, fn => emulator.saveStateFileName = fn),
//                 };
//                 for (int i = 0; i < emulator.luaScripts.Count; i++) {
//                     int j = i;
//                     Debug.Log(emulator.luaScripts[j]);
//                     accessors.Add((() => emulator.luaScripts[j], fn => emulator.luaScripts[j] = fn));
//                 }
//                 foreach ((var getter, var setter) in accessors) {
//                     string path = getter();

//                     if (!string.IsNullOrEmpty(path)) {
//                         // Get the path that Emulator will actually look for the file
//                         string origFilePath = Emulator.GetAbsolutePath(path);

//                         // ignore anything within StreamingAssets/ since those get copied over already by Unity
//                         Debug.Log($"Check isSubPath {origFilePath}, {Application.streamingAssetsPath}");
//                         if (IsSubPath(Application.streamingAssetsPath, origFilePath)) {
//                             Debug.Log($"Skipping copy for: {origFilePath}");
//                             continue;
//                         }

//                         // Use a hash of the abs path for the new file, so that files won't conflict
//                         // but also if a file is used multiple times it won't be duplicated in the build
//                         // also need to preserve the file extension since bizhawk uses that to determine the platform
//                         string ext = Path.GetExtension(path);
//                         string newFileName = origFilePath.GetHashCode().ToString("x") + ext;
//                         // Set the emulator to point to the new file [don't worry, this change doesn't persist in the scene, only affects the build]
//                         setter(newFileName);
//                         // And copy into the right location in the build (xxx_Data/StreamingAssets/<hash>)
//                         string newFilePath = Path.Combine(streamingAssetsBuildDir, newFileName);
//                         Debug.Log($"copy from: {origFilePath} to {newFilePath}");
//                         File.Copy(origFilePath, newFilePath, overwrite: true);

//                         if (ext == ".cue") {
//                             // This is annoying, but some roms (e.g. for PSX) are .cue files, and those point to other file dependencies
//                             // so we need to copy those files over as well (without renaming)
//                             // [if there are other special cases we have to handle like this we should probably rethink this whole approach tbh - too much work]
//                             // [definitely at least need an alternative in case this has issues (one way would be to just use StreamingAssets)]
                            
//                             // this also has a minor bug that if separate .bin files hae the same name they'll get clobbered
//                             // probably has a ton of edge cases that will break too
//                             var cueSheet = new CueSheet(origFilePath);
//                             // Copy all the .bin files that are referenced
//                             string cueFileParentDir = Path.GetDirectoryName(origFilePath);
//                             foreach (Track t in cueSheet.Tracks) {
//                                 string binFileName = t.DataFile.Filename;
//                                 if (binFileName != Path.GetFileName(binFileName)) {
//                                     throw new ArgumentException("UnityHawk build script doesn't support .cue files that reference files in a different directory. Consider putting your rom files in StreamingAssets instead");
//                                 }
//                                 // bin file pathname is relative to the directory of the cue file
//                                 string binPath = Path.Combine(cueFileParentDir, binFileName);
//                                 newFilePath = Path.Combine(streamingAssetsBuildDir, binFileName); 
//                                 Debug.Log($"copy from: {binPath} to {newFilePath}");
//                                 File.Copy(binPath, newFilePath, overwrite: true);
//                             }
//                         }
//                     }
//                 }
//             }
//         }
//     }

//     public void OnPostprocessBuild(BuildReport report)
//     {
//         string exePath = report.summary.outputPath;
//         // Gotta make sure all the bizhawk stuff gets into the build
//         // Copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into xxx_Data/org.plunderludics.UnityHawk/BizHawk/

//         // [kinda sucks but don't know a better way]
//         var targetDir = Path.Combine(GetBuildDataDir(exePath), UnityHawk.bizhawkDirRelative);
//         Debug.Log($"from: {Path.GetFullPath(UnityHawk.bizhawkDir)} to {Path.GetFullPath(targetDir)}");
//         Directory.CreateDirectory(targetDir);
//         FileUtil.ReplaceDirectory(Path.GetFullPath(UnityHawk.bizhawkDir), Path.GetFullPath(targetDir)); // [only works with full paths for some reason]

//         // TODO: we should separate the dlls needed by Unity (i.e. BizHawk.Client.Common, etc)
//         // and the ones loaded at runtime within BizHawk (like waterboxhost.dll, etc)
//         // currently we end up with two copies of every dll in the build which is stupid
//         // [one way to do this would be leave the file structure as is, disable the runtime dlls from being exported to the standalone by Unity,
//         //  and then just manually copy them into dataDir in this script]
//     }

//     static string GetBuildDataDir(string exePath) {
//         return Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath)+"_Data");
//     }

//     // [https://stackoverflow.com/a/74401631]
//     public static bool IsSubPath(string parent, string child)
//     {
//         try
//         {
//             parent = Path.GetFullPath(parent);
//             if (!parent.EndsWith(Path.DirectorySeparatorChar.ToString()))
//                 parent = parent + Path.DirectorySeparatorChar;
//             child = Path.GetFullPath(child);
//             if (child.Length <= parent.Length)
//                 return false;
//             return child.StartsWith(parent);
//         }
//         catch
//         {
//             return false;
//         }
//     }
// }
// }