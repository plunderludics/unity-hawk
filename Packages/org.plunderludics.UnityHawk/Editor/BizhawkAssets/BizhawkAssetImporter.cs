using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityHawk.Editor {

public class BizHawkAssetImporter<T> : ScriptedImporter where T : BizhawkAsset
{
    public override void OnImportAsset(AssetImportContext ctx) {
        var a = ScriptableObject.CreateInstance<T>();
        a.Path = GetPath(ctx);
        ctx.AddObjectToAsset("main obj", a);
        ctx.SetMainObject(a);
    }

    protected string GetPath(AssetImportContext ctx) {
        return Path.GetRelativePath(Application.dataPath, ctx.assetPath);
    }
}

}