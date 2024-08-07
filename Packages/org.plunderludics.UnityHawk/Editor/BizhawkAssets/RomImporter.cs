using UnityEditor.AssetImporters;

namespace UnityHawk.Editor {

[ScriptedImporter(1, new [] {
	/* Amstrad CPC: */ "cdt", "dsk",
	/* Atari 2600: */ "a26", "bin",
	/* Atari 7800: */ "a78", "bin",
	/* Atari Jaguar: */ "j64", "jag",
	/* Atari Lynx: */ "lnx",
	/* Apple II: */ "dsk", "do", // TODO: support ".po"
	/* Arcade: */ "zip", "7z", "chd",
	/* ColecoVision: */ "col",
	/* Commodore 64: */ "prg", "d64", "g64", "crt", "tap",
	/* Commodore 64 Music File: */ "sid",
	/* MSX: */ "cas", "dsk", "mx1", "rom",
	/* IntelliVision: */ "int", "bin", "rom",
	/* Neo Geo Pocket: */ "ngp", "ngc",
	/* Odyssey 2: */ "o2",
	/* PC Engine: */ "pce", "sgx", "cue", "ccd", "mds",
	/* Nintendo Gameboy: */ "gb", "gbc", "sgb", "gbs",
	/* Nintendo Gameboy Advance: */ "gba",
	/* NES: */ "nes", "fds", "unf", "nsf",
	/* Super NES: */ "smc", "sfc", "bs", // TODO: support ".xml"
	/* Nintendo Virtual Boy: */ "vb",
	/* Nintendo 64: */ "z64", "v64", "n64",
	/* Nintendo 64 Disk Drive: */ "ndd",
	/* Nintendo DS: */ "nds",
	/* Sega Master System: */ "sms", "gg", "sg",
	/* Sega Genesis: */ "gen", "smd", "32x", "bin", "cue", "ccd", // TODO: support ".md"
	/* Sony PlayStation: */ "bin", "cue", "ccd", "mds", "m3u",
	/* Sony PSX Executables (experimental): */ "exe",
	/* Sony PSF Playstation Sound File: */ "psf", "minipsf",
	/* Sinclair ZX Spectrum: */ "tzx", "tap", "dsk", "pzx", "csw", // TODO: support ".wav",
	/* TI-83: */ "83g", "83l", "83p",
	/* TIC-80: */ "tic",
	/* Uzebox: */ "uze",
	/* Vectrex: */ "vec",
	/* WonderSwan: */ "ws", "wsc", "pc2",

	/* Music Files: */ "psf", "minipsf", "sid", "nsf", "gbs",
	/* Disc Images: */ "cue", "ccd", "cdi", "mds", "m3u"
})]
public class RomImporter : BizHawkAssetImporter<Rom>
{
    public override void OnImportAsset(AssetImportContext ctx) {
        base.OnImportAsset(ctx);
    }
}

}