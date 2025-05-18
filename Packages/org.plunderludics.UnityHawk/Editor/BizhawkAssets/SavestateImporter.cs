using System.IO;
using System.IO.Compression;
using BizHawk.Emulation.Common;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityHawk.Editor {

// [ScriptedImporter(1, "z64,cue,nes")]
[ScriptedImporter(1, "savestate")]
public class SavestateImporter : BizHawkAssetImporter<Savestate> {
    const string k_GameInfoFile = "GameInfo.json";

    public override void OnImportAsset(AssetImportContext ctx) {

        using var stateFile = ZipFile.OpenRead(ctx.assetPath);
        var gameInfo = GameInfo.NullInstance;
        // find the game info file and deserialize it
        foreach (var entry in stateFile.Entries) {
            if (entry.Name != k_GameInfoFile) continue;

            using var s = entry.Open();
            gameInfo = GameInfo.Deserialize(s) ?? GameInfo.NullInstance;
            break;
        }

        var savestate = ScriptableObject.CreateInstance<Savestate>();
        savestate.Location = Path.GetRelativePath(Application.dataPath, ctx.assetPath);
        savestate.RomInfo = new RomInfo(gameInfo);

        ctx.AddObjectToAsset("main obj", savestate);
        ctx.SetMainObject(savestate);
    }
}

}