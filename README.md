# UnityHawk

UnityHawk lets you run emulated games within a Unity project, using [BizHawk](https://tasvideos.org/BizHawk).

## Installation
Add these two lines under `dependencies` in your `manifest.json`:
```
"org.plunderludics.unity-hawk": "https://github.com/plunderludics/unity-hawk.git?path=/Packages/org.plunderludics.UnityHawk#upm"
"com.dbrizov.naughtyattributes": "https://github.com/dbrizov/NaughtyAttributes.git#upm"
```

## Usage
- Add an `Emulator` component to an object with an attached `Renderer`.
- Put the rom, savestate, config & lua files you want to use into `Assets/StreamingAssets/`
- If using a platform that requires firmware, put the files inside `StreamingAssets/Firmware/`
- Set the filenames on the Emulator component (relative to the `StreamingAssets/` directory)
  - (You can also use an absolute path if you want to reference files outside of the Unity project - UnityHawk will attempt to copy the necessary files into the build at build time but relying on this is not recommended)
- The live emulator graphics can be grabbed in code via the `Emulator.Texture` property.

## Building and releasing
 - Any files within StreamingAssets (e.g. roms, firmware) will get copied into the build so be careful about distribution legality :)

## Development
UnityHawk uses a custom fork of BizHawk which is here: https://github.com/plunderludics/bizhawk/tree/unity-hawk

After building that project copy `EmuHawk.exe` and the `dll/` directory into the `BizHawk/` directory within this package.
