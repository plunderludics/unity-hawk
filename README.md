# UnityHawk

UnityHawk lets you run emulated games within a Unity project, using [BizHawk](https://tasvideos.org/BizHawk).

Made as a tool for the development of [plunderludics](https://plunderludics.github.io/wiki/).

Under development, may have bugs; issues, feature requests or contributions are welcome. Only works on Windows currently.

## Installation
Add these two lines under `dependencies` in your `manifest.json`:
```
"org.plunderludics.unity-hawk": "https://github.com/plunderludics/unity-hawk.git?path=/Packages/org.plunderludics.UnityHawk#upm"
"com.dbrizov.naughtyattributes": "https://github.com/dbrizov/NaughtyAttributes.git#upm"
```

Or, install using [openupm-cli](https://github.com/openupm/openupm-cli):
```
openupm add org.plunderludics.unityhawk
```

## Usage
- Add an `Emulator` component to an object with an attached `Renderer`.
- Put the rom, savestate, config & lua files you want to use into `Assets/StreamingAssets/`
- If using a platform that requires firmware, put the files inside `StreamingAssets/Firmware/`
- Set the filenames on the Emulator component (relative to the `StreamingAssets/` directory)
    - (You can also use an absolute path if you want to reference files outside of the Unity project - UnityHawk will attempt to copy the necessary files into the build at build time but relying on this is not really recommended)
- The live emulator graphics can be grabbed in code via the `Emulator.Texture` property.

## Features
- Enable 'Send Input To Bizhawk' to send keyboard input from Unity to Bizhawk (gamepad input not supported yet). If this isn't enabled Bizhawk will get input directly from the operating system.
- Enable 'Capture Emulator Audio' to route emulator audio to an AudioSource, allowing you to use unity's positional audio system or add audio effects
    - (unfortunately this creates some latency and sometimes distorted audio, especially when multiple emulators are running concurrently, this should probably be considered experimental for now)
- Enable 'Show Bizhawk Gui' to show the native Bizhawk window; useful for doing plunderludics development (finding save states, tweaking config & scripts, etc) without having to leave Unity

## Building and releasing
- Any files within StreamingAssets (e.g. roms, firmware) will get copied into the build so be careful about distribution legality :)

## Development
UnityHawk uses a custom fork of BizHawk which is here: https://github.com/plunderludics/bizhawk/tree/unity-hawk

After building that project copy `EmuHawk.exe` and the `dll/` directory into the `BizHawk/` directory within this package.

## Contact
If you want help setting up the tool, or you are interested in contributing, feel free to join our discord: https://discord.gg/ATJSh8W8dp. Github issues also welcome.
