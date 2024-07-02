using UnityEditor.AssetImporters;

namespace UnityHawk.Editor {
    
[ScriptedImporter(1, new [] {
    "z64", "n64", // Nintendo 64
    "cue", // PSX
    "nes", // Famicom / NES
    "smc", // Super Famicom / SNES
    "ccd", // ?
    "dsk" // Apple II
    // TODO: add all
})]
public class RomImporter : BizHawkAssetImporter<Rom>
{
    public override void OnImportAsset(AssetImportContext ctx) {
        base.OnImportAsset(ctx);
    }
}

}
