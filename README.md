# UnityHawk

![demo](https://github.com/plunderludics/unity-hawk/assets/8207025/24774607-7bb0-4ba1-9130-4073f39bb883)

UnityHawk lets you run emulated games within a Unity project, using [BizHawk](https://tasvideos.org/BizHawk).

Made as a tool for the development of [plunderludics](https://plunderludics.github.io/wiki/).

Under development, may have bugs; issues, feature requests or contributions are welcome. Only works on Windows currently.

## Installation
Add these two lines under `dependencies` in your `manifest.json`:
```
"org.plunderludics.unityhawk": "https://github.com/plunderludics/unity-hawk.git?path=/Packages/org.plunderludics.UnityHawk#upm"
"com.dbrizov.naughtyattributes": "https://github.com/dbrizov/NaughtyAttributes.git#upm"
```

Or, install using [openupm-cli](https://github.com/openupm/openupm-cli):
```
openupm add org.plunderludics.unityhawk
```

UnityHawk should then appear in the Package Manager window. From the Package Manager window you can import the sample called `Demo` for a simple example of how to use this package.

## Usage
- Add an `Emulator` component to an object with an attached `Renderer`.
- Put the rom, savestate, config & lua files you want to use into `Assets/StreamingAssets/`
- If using a platform that requires firmware, put the files inside `Assets/StreamingAssets/Firmware/`
- Set the filenames on the Emulator component (relative to the StreamingAssets directory)
- The live emulator graphics can be grabbed in code via the `Emulator.Texture` property.
- Within a BizHawk Lua script, you can use the `unityhawk.methodcall(methodName, argString)` method to send and receive information from Unity. The method must be registered on the Unity side using `Emulator.RegisterMethod`. See `test.lua` and `RegisterMethodExample` in the `Demo` sample for a brief example.

## Features
- Enable 'Send Input To Bizhawk' to send keyboard input from Unity to Bizhawk (gamepad input not supported yet). If this isn't enabled Bizhawk will get input directly from the operating system.
- Enable 'Show Bizhawk Gui' to show the native Bizhawk window; useful for doing plunderludics development (finding save states, tweaking config & scripts, etc) without having to leave Unity
- \[experimental\] Enable 'Capture Emulator Audio' to route emulator audio to an AudioSource, allowing you to use unity's positional audio system or add audio effects
    - (unfortunately the current implementation creates some latency and sometimes distorted audio, especially with multiple emulators running concurrently)

## Building and releasing
- Building (for Windows) should just work; anything you put in the StreamingAssets directory (roms, firmware) will be copied into the build, as well as the necessary Bizhawk dependencies.
- (You can use absolute filepaths if you want to reference files outside of the StreamingAssets directory - in that case UnityHawk will attempt to copy the files into the build at build time but it's a bit flaky and relying on this is not really recommended)

## Implementation
The `Emulator` component spawns `EmuHawk.exe` as a child process which shares graphics and audio with Unity via [shared memory](https://github.com/justinstenning/SharedMemory).

## Attribution
The included demo scene contains [this shader](https://github.com/yunoda-3DCG/Simple-CRT-Shader), [this model of a TV](https://sketchfab.com/3d-models/crt-tv-9ba4baa106e64319a0b540cf0af5aa9e), and [a rom of Elite for the NES](http://www.iancgbell.clara.net/elite/nes/index.htm).

## Development
UnityHawk uses a modified fork of BizHawk which is here: https://github.com/plunderludics/bizhawk/tree/unity-hawk

After building that project copy `EmuHawk.exe` and the `dll/` directory into the `BizHawk~/` directory within this package.

## Contact
If you want help setting up the tool, or you are interested in contributing, feel free to join our discord: https://discord.gg/ATJSh8W8dp. Github issues also welcome.
