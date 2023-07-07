using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using System.IO;

namespace UnityHawk {

// NOTE if this seems to not be working -
// Make sure every scene is actually added to the list of 'Scenes in Build'
// otherwise this won't work.

// This does two main things:
//  - copy the BizHawk directory (which contains gamedb, etc) into the build
//  - locate any custom file dependencies from UnityHawk.Emulator components (ie roms, config, lua, savestates)
//     and copy those into the build too (as well as temporarily updating the reference in the Emulator component)
class BuildProcessing : UnityEditor.Build.IPostprocessBuildWithReport, IPreprocessBuildWithReport, IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) {
        
    }
    public void OnProcessScene(Scene scene, BuildReport report) {
        if (report == null) {
            // This means the callback is being called during script compilation within the editor,
            // not a build, so no need to do anything
            return;
        }
        Debug.Log("OnProcessScene");
        string outputPath = report.summary.outputPath;

        // look through all Emulator components in the scene and
        // locate (external) file dependencies, copy them into the build, and (temporarily) update the path references
        GameObject[] gameObjects = scene.GetRootGameObjects();
        foreach (var gameObject in gameObjects) {
            var emulators = gameObject.GetComponentsInChildren<Emulator>(includeInactive: true);
            foreach (Emulator emulator in emulators) {
                Debug.Log(emulator.romFileName);
                // emulator.romFileName = "wa"; // [cool, this change seems to not persist after the build is done, so no need to clean up afterwards]
                // TODO: if the filename is an absolute path,
                // then copy it to somewhere within the build directory, and change romFileName to point to that

                // maybe just use a hash of the filename (abs path) so that files won't conflict
                // but if e.g. multiple Emulators point to the same rom
                // then we only need one rom file in the build
            }
        }

    }
    

    public void OnPostprocessBuild(BuildReport report)
    {
        string exePath = report.summary.outputPath;
        // Gotta make sure all the bizhawk stuff gets into the build
        // Just copy over the whole Packages/org.plunderludics.UnityHawk/BizHawk/ directory into the build (with the same path relative to the exe)

        // [kinda sucks but don't know a better way]
        var targetDir = Path.Combine(Path.GetDirectoryName(exePath), UnityHawk.bizhawkDirName);
        FileUtil.ReplaceDirectory(UnityHawk.bizhawkDir, targetDir);

        // TODO: we should separate the dlls needed by Unity (i.e. BizHawk.Client.Common, etc)
        // and the ones loaded at runtime within BizHawk (like waterboxhost.dll, etc)
        // currently we end up with two copies of every dll in the build which is stupid
        // [one way to do this would be leave the file structure as is, disable the runtime dlls from being exported to the standalone by Unity,
        //  and then just manually copy them into dataDir in this script]
    }
}
}