# Convert default controls from bizhawk config.ini into Controls scriptableobjects
# Currently ignores non-P1 and analog controls, and also skips some platforms
# Usage: python3 ./bin/make_default_controls.py

import os
import json

config_file = "Packages/org.plunderludics.UnityHawk/BizHawk~/config.ini"
controls_dir = "Packages/org.plunderludics.UnityHawk/Resources/DefaultControls"

skip_systems = {
    "PSX" # Default config has some weird duplicate stuff in there (e.g both "Triangle" and "△"), just use the one we already created for now
}

name_to_system_id = {
    "Lynx Controller": "Lynx",
    "SNES Controller": "SNES",
    "Commodore 64 Controller": "C64",
    "GBA Controller": "GBA",
    "Atari 7800 ProLine Joystick Controller": "A78",
    "Dual Gameboy Controller": "DGB",
    "WonderSwan Controller": "WSWAN",
    "Nintendo 64 Controller": "N64",
    "Saturn Controller": "SAT",
    "GPGX Genesis Controller": "GEN",
    "NES Controller": "NES",
    "Gameboy Controller": "GB",
    "Atari 2600 Basic Controller": "A26",
    "TI83 Controller": "TI83",
    "ColecoVision Basic Controller": "Coleco",
    "SMS Controller": "SMS",
    "LibRetro Controls": "Libretro",
    "TIC-80 Controller": "TIC80",
    "VirtualBoy Controller": "VB",
    "Jaguar Controller": "Jaguar",
    "NeoGeo Portable Controller": "NGP",
    "PSX Front Panel": "PSX",
    # There are a bunch missing here still, TODO add
}

# Unity KeyCode enum
key_name_to_code = {
    "None": 0,
    "Backspace": 8,
    "Tab": 9,
    "Clear": 12,
    "Enter": 13,
    "Pause": 19,
    "Escape": 27,
    "Space": 32,
    "Exclaim": 33,
    "DoubleQuote": 34,
    "Hash": 35,
    "Dollar": 36,
    "Ampersand": 38,
    "Quote": 39,
    "LeftParen": 40,
    "RightParen": 41,
    "Asterisk": 42,
    "Plus": 43,
    "Comma": 44,
    "Minus": 45,
    "Period": 46,
    "Slash": 47,
    "0": 48,
    "1": 49,
    "2": 50,
    "3": 51,
    "4": 52,
    "5": 53,
    "6": 54,
    "7": 55,
    "8": 56,
    "9": 57,
    "Colon": 58,
    "Semicolon": 59,
    "Less": 60,
    "Equals": 61,
    "Greater": 62,
    "Question": 63,
    "At": 64,
    "LeftBracket": 91,
    "Backslash": 92,
    "RightBracket": 93,
    "Caret": 94,
    "Underscore": 95,
    "BackQuote": 96,
    "A": 97,
    "B": 98,
    "C": 99,
    "D": 100,
    "E": 101,
    "F": 102,
    "G": 103,
    "H": 104,
    "I": 105,
    "J": 106,
    "K": 107,
    "L": 108,
    "M": 109,
    "N": 110,
    "O": 111,
    "P": 112,
    "Q": 113,
    "R": 114,
    "S": 115,
    "T": 116,
    "U": 117,
    "V": 118,
    "W": 119,
    "X": 120,
    "Y": 121,
    "Z": 122,
    "Delete": 127,
    "Keypad0": 256,
    "Keypad1": 257,
    "Keypad2": 258,
    "Keypad3": 259,
    "Keypad4": 260,
    "Keypad5": 261,
    "Keypad6": 262,
    "Keypad7": 263,
    "Keypad8": 264,
    "Keypad9": 265,
    "KeypadDecimal": 266,
    "KeypadDivide": 267,
    "KeypadMultiply": 268,
    "KeypadSubtract": 269,
    "KeypadAdd": 270,
    "KeypadEnter": 271,
    "KeypadEquals": 272,
    "Up": 273,
    "Down": 274,
    "Right": 275,
    "Left": 276,
    "Insert": 277,
    "Home": 278,
    "End": 279,
    "PageUp": 280,
    "PageDown": 281,
    "F1": 282,
    "F2": 283,
    "F3": 284,
    "F4": 285,
    "F5": 286,
    "F6": 287,
    "F7": 288,
    "F8": 289,
    "F9": 290,
    "F10": 291,
    "F11": 292,
    "F12": 293,
    "F13": 294,
    "F14": 295,
    "F15": 296,
    "Numlock": 300,
    "CapsLock": 301,
    "ScrollLock": 302,
    "RightShift": 303,
    "LeftShift": 304,
    "RightControl": 305,
    "LeftControl": 306,
    "RightAlt": 307,
    "LeftAlt": 308,
    "LeftCommand": 310,
    "LeftApple": 310,
    "LeftWindows": 311,
    "RightCommand": 309,
    "RightApple": 309,
    "RightWindows": 312,
}

def mapping_to_yaml(system_id, mapping):
    yaml_lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &11400000",
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        "  m_Script: {fileID: 11500000, guid: 772afa63a3b742a4f9d18e203df20db3, type: 3}",
        f"  m_Name: {system_id}",
        "  m_EditorClassIdentifier: ",
        "  mappings:"
    ]
    for k, v in mapping.items():
        if k.startswith("P2 ") or k.startswith("P3 ") or k.startswith("P4 "):
            print(f"Warning: skipping P2/P3/P4 control '{k}' in {system_id}")
            continue
        keys = v.split(",")
        for key in keys:
            key = key.strip()
            if key in key_name_to_code:
                keycode = key_name_to_code[key]
            else:
                print(f"Warning: Key '{key}' not found in key_name_to_code, skipping.")
                # print(system_id,k,v)
                continue
            yaml_lines.append(f"  - Key: {keycode}")
            yaml_lines.append(f"    Control: {k.replace("P1 ", "")}")
    return "\n".join(yaml_lines)

def main():
    os.makedirs(controls_dir, exist_ok=True)
    with open(config_file, encoding="utf-8") as f:
        config = json.load(f)
    controllers = config["AllTrollers"]
    for ctrl_name, mapping in controllers.items():
        if ctrl_name in name_to_system_id:
            system_id = name_to_system_id[ctrl_name] 
        else:
            print(f"Warning: Controller '{ctrl_name}' not found in name_to_system_id, skipping.")
            continue

        if system_id in skip_systems:
            print(f"Skipping {system_id}")
            continue
        print(f"Processing {system_id}")
        yaml = mapping_to_yaml(system_id, mapping)
        asset_name = system_id + ".asset"
        out_path = os.path.join(controls_dir, asset_name)

        # if os.path.exists(out_path):
        #     print(f"Skipped {out_path} (already exists)")
        #     continue
    
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(yaml)
        print(f"Wrote {out_path}")

if __name__ == "__main__":
    main()