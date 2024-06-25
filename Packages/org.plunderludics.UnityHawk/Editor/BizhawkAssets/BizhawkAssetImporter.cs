using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityHawk.Editor {
    
public class BizHawkAssetImporter<T> : ScriptedImporter where T : BizhawkAsset
{
    public override void OnImportAsset(AssetImportContext ctx) {
        var a = ScriptableObject.CreateInstance<T>();
        a.Path = ctx.assetPath;
        ctx.AddObjectToAsset("main obj", a);
        ctx.SetMainObject(a); 
    }
}

}
