using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityHawk.Editor {

public abstract class BizHawkAssetImporter<T> : ScriptedImporter where T : BizhawkAsset
{
    public override void OnImportAsset(AssetImportContext ctx) {
        var a = ScriptableObject.CreateInstance<T>();
        a.Location = Path.GetRelativePath(Application.dataPath, ctx.assetPath);
        ctx.AddObjectToAsset("main obj", a);
        ctx.SetMainObject(a);
    }
}

}