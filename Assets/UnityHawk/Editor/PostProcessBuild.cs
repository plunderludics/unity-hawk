using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

using System.IO;


namespace UnityHawk {
public static class PostProcessBuild
{
    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        // Copy the BizHawk dir and Roms dir directly into the unity-hawk_Data folder
        // [kinda sucks but don't know a better way]
        var dataDir = Path.Join(Path.GetDirectoryName(pathToBuiltProject), Path.GetFileNameWithoutExtension(pathToBuiltProject)+"_Data");
        var buildBizhawkDir = Path.Join(dataDir, UnityHawk.bizhawkDirName);
        FileUtil.ReplaceDirectory(UnityHawk.bizhawkDir, buildBizhawkDir);

        // TODO: we should separate the dlls needed by Unity (i.e. BizHawk.Client.Common, etc)
        // and the ones loaded at runtime within BizHawk (like waterboxhost.dll, etc)
        // currently we end up with two copies of every dll in the build which is stupid
        // [one way to do this would be leave the file structure as is, disable the runtime dlls from being exported to the standalone by Unity,
        //  and then just manually copy them into dataDir in this script]
    }
}
}