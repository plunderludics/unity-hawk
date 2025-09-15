using System.IO;
using System.IO.Compression;
using BizHawk.Emulation.Common;
using UnityEditor.AssetImporters;
using UnityEngine;
using ZstdSharp;
using B83.Image.BMP;

namespace UnityHawk.Editor {

[ScriptedImporter(1, "savestate")]
public class SavestateImporter : BizHawkAssetImporter<Savestate> {
    const string k_GameInfoFile = "GameInfo.json";
    const string k_FramebufferFile = "Framebuffer.bmp";

    public override void OnImportAsset(AssetImportContext ctx) {
        base.OnImportAsset(ctx);
        var savestate = (Savestate)ctx.mainObject;

        using var stateFile = ZipFile.OpenRead(ctx.assetPath);
        var gameInfo = GameInfo.NullInstance;
        Texture2D screenshot = null;
        // find the game info file and deserialize it
        foreach (var entry in stateFile.Entries) {
            if (entry.Name == k_GameInfoFile) {
                using var s = entry.Open();
                gameInfo = GameInfo.Deserialize(s) ?? GameInfo.NullInstance;
            } else if (entry.Name == k_FramebufferFile) {
                using var s = entry.Open();
                using var bmpStream = new DecompressionStream(s);

                // First read all bytes from stream
                byte[] bmpBytes;
                using (var ms = new MemoryStream())
                {
                    bmpStream.CopyTo(ms);
                    bmpBytes = ms.ToArray();
                }

                BMPLoader bmpLoader = new BMPLoader();
                BMPImage bmpImg = bmpLoader.LoadBMP(bmpBytes);

                // Convert the Color32 array into a Texture2D
                screenshot = bmpImg.ToTexture2D(TextureFormat.RGB24); // Ensure no alpha channel
            }
        }

        savestate.RomInfo.Name = gameInfo.Name;
        savestate.RomInfo.Hash = gameInfo.Hash;
        savestate.RomInfo.Region = gameInfo.Region;
        savestate.RomInfo.System = gameInfo.System;
        savestate.RomInfo.NotInDatabase = gameInfo.NotInDatabase;
        savestate.RomInfo.Core = gameInfo.ForcedCore;

        screenshot.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
        ctx.AddObjectToAsset("screenshot", screenshot);
        savestate.Screenshot = screenshot;
    }
}

}