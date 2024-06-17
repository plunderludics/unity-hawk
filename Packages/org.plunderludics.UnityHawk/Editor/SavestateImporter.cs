using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BizHawk.Client.Common;
using BizHawk.Common;
using BizHawk.Common.IOExtensions;
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
        GameInfo gi = GameInfo.NullInstance;
        // find the game info file and deserialize it
        foreach (var entry in stateFile.Entries) {
            if (entry.Name != k_GameInfoFile) continue;
            
            using var s = entry.Open();
            gi = GameInfo.Deserialize(s) ?? GameInfo.NullInstance;
            break;
        }

        var savestate = ScriptableObject.CreateInstance<Savestate>();
        savestate.Path = ctx.assetPath;
        
        savestate.Name = gi.Name;
        savestate.Hash = gi.Hash;
        savestate.Region = gi.Region;
        savestate.System = gi.System;
        savestate.NotInDatabase = gi.NotInDatabase;
        savestate.Core = gi.ForcedCore;
        
        ctx.AddObjectToAsset("main obj", savestate);
        ctx.SetMainObject(savestate); 
    }
}

}
