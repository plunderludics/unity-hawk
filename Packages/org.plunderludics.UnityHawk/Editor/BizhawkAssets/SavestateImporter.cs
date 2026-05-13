using System.IO;
using System.IO.Compression;
using BizHawk.Emulation.Common;
using UnityEditor.AssetImporters;
using UnityEngine;
using ZstdSharp;
using B83.Image.BMP;

namespace UnityHawk.Editor {

[ScriptedImporter(1, "savestate")]
internal class SavestateImporter : BizHawkAssetImporter<Savestate> {
    const string k_GameInfoFile = "GameInfo.json";
    const string k_FramebufferFile = "Framebuffer.bmp";

    public override void OnImportAsset(AssetImportContext ctx) {
        base.OnImportAsset(ctx);
        try {
            var savestate = (Savestate)ctx.mainObject;
            using var stateFile = ZipFile.OpenRead(ctx.assetPath);

            var gameInfo = GameInfo.NullInstance;
            Texture2D screenshot = null;

            foreach (var entry in stateFile.Entries) {
                if (entry.Name == k_GameInfoFile) {
                    gameInfo = TryImportGameInfo(entry, ctx.assetPath);
                } else if (entry.Name == k_FramebufferFile) {
                    screenshot = TryImportScreenshot(entry, ctx.assetPath);
                }
            }

            savestate.RomInfo.Name = gameInfo.Name;
            savestate.RomInfo.Hash = gameInfo.Hash;
            savestate.RomInfo.Region = gameInfo.Region;
            savestate.RomInfo.System = gameInfo.System;
            savestate.RomInfo.NotInDatabase = gameInfo.NotInDatabase;
            savestate.RomInfo.Core = gameInfo.ForcedCore;

            if (screenshot != null) {
                screenshot.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
                ctx.AddObjectToAsset("screenshot", screenshot);
                savestate.Screenshot = screenshot;
            }
        } catch (System.Exception e) {
            Debug.LogWarning($"Failed to import savestate content from '{ctx.assetPath}': {e.Message}");
        }
    }

    static GameInfo TryImportGameInfo(ZipArchiveEntry entry, string assetPath) {
        try {
            using var s = entry.Open();
            return GameInfo.Deserialize(s) ?? GameInfo.NullInstance;
        } catch (System.Exception e) {
            Debug.LogWarning($"Failed to import GameInfo from '{assetPath}': {e.Message}");
            return GameInfo.NullInstance;
        }
    }

    static Texture2D TryImportScreenshot(ZipArchiveEntry entry, string assetPath) {
        try {
            using var s = entry.Open();

            byte[] rawBytes;
            using (var ms = new MemoryStream()) {
                s.CopyTo(ms);
                rawBytes = ms.ToArray();
            }

            // Seems like sometimes (maybe in bizhawk 2.11 only?) the framebuffer file is *not* compressed - handle both cases
            byte[] bmpBytes;
            try {
                using var compressed = new MemoryStream(rawBytes);
                using var zstd = new DecompressionStream(compressed);
                using var decompressed = new MemoryStream();
                zstd.CopyTo(decompressed);
                bmpBytes = decompressed.ToArray();
            } catch (ZstdException) {
                bmpBytes = rawBytes;
            }

            var bmpImg = new BMPLoader().LoadBMP(bmpBytes);
            return bmpImg.ToTexture2D(TextureFormat.RGB24);
        } catch (System.Exception e) {
            Debug.LogWarning($"Failed to import screenshot from '{assetPath}': {e.Message}");
            return null;
        }
    }
}

}