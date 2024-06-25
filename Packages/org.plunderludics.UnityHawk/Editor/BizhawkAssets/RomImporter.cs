using UnityEditor.AssetImporters;

namespace UnityHawk.Editor {
    
[ScriptedImporter(1, new [] {
    "z64", 
    "cue", 
    "nes", 
    "ccd",
    // TODO: add all
})]
public class RomImporter : BizHawkAssetImporter<Rom>
{
    public override void OnImportAsset(AssetImportContext ctx) {
        base.OnImportAsset(ctx);
    }
}

}
