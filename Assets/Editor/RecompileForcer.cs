// Very hacky script to allow generating xml docs from bin/build_docs.sh
// For some reason just opening project and compiling doesn't seem to work, so we force a second compilation by calling this function

using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;

public static class RecompileForcer
{
    public static void ForceRecompile()
    {
        Debug.Log("Forcing recompile...");
        // Force compilation by reimporting a script asset
        AssetDatabase.ImportAsset("Assets/Editor/RecompileForcer.cs", ImportAssetOptions.ForceUpdate);
    }
}
