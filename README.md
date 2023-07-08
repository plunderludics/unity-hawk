# unity-hawk

First build this fork of BizHawk: https://github.com/plunderludics/bizhawk/tree/unity-hawk

then copy these directories from `BizHawk/output` into `Packages/org.plunderludics.UnityHawk/BizHawk/` in this Unity project:
 - `dll`
 - `gamedb`
 - `Firmware`

## Usage:
Add a UnityHawk.Emulator component to an object with a Renderer attached.
Put the rom, savestate, config & lua files you want to use into `Assets/StreamingAssets/`
Set the filenames on the Emulator component (relative to the StreamingAssets dir)
(You can also use an absolute path if you want to reference files outside of the Unity project - UnityHawk will attempt to copy the necessary files into the build at build time)