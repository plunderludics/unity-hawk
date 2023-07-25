# UnityHawk

UnityHawk lets you run emulated games within a Unity project, using [BizHawk](https://tasvideos.org/BizHawk).

## Installation
Add this line under `dependencies` in your `manifest.json`:
```
"org.plunderludics.unity-hawk": "https://github.com/plunderludics/unity-hawk.git?path=/Packages/org.plunderludics.UnityHawk"
```

## Usage
- Add a UnityHawk.Emulator component to an object with a Renderer attached.
- Put the rom, savestate, config & lua files you want to use into `Assets/StreamingAssets/`
- Set the filenames on the Emulator component (relative to the StreamingAssets dir)
  - (You can also use an absolute path if you want to reference files outside of the Unity project - UnityHawk will attempt to copy the necessary files into the build at build time but relying on this is not recommended)

## Development
UnityHawk uses a custom fork of BizHawk which is here: https://github.com/plunderludics/bizhawk/tree/unity-hawk

After building that project copy EmuHawk.exe and the dll/ directory into the BizHawk/ directory within this package.