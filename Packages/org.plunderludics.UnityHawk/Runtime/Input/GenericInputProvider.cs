using System;
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

// Map from InputSystem input actions to bizhawk keys according to user-specified mapping
public class GenericInputProvider : InputProvider {
#if ENABLE_INPUT_SYSTEM
    const string KEYBOARD_NAME = "Keyboard";

    enum Source {
        Keyboard,
        Gamepad
    }

    // [Do it this way rather than having to add a SerializableDictionary implementation]
    [Header("refs")]
    [Tooltip("the unity input to bizhawk keyboard mapping")]
    [UnityEngine.Serialization.FormerlySerializedAs("mappings")]
    [SerializeField] GenericInputMapping mappings;

    [Header("sources")]
    [Tooltip("can the input come from any source?")]
    [SerializeField] bool anySource = true;

    [HideIf("anySource")]
    [Tooltip("if not, what kind of source can the input come from?")]
    [SerializeField] Source source = Source.Keyboard;

    bool showGamePadIndex => !anySource && source == Source.Gamepad;
    [ShowIf("showGamePadIndex")]
    [Tooltip("if gamepad, whats the index?")]
    [SerializeField] int gamepadIndex = 0;


#endif

    List<InputEvent> pressed = new();
    public void Start() {
#if ENABLE_INPUT_SYSTEM
        // [I don't get why you have to manually enable all the actions, but ok]
        var mappingsDict = mappings.All.ToDictionary(
            m => m.action.action.id,
            m => m.keyName
        );
        foreach (var mapping in mappings.All) {
            mapping.action.action.Enable();

            // NOTE from mut: i think this is a MAJOR hack to avoid using the player feature
            // unity has by default, where it manages creating new players for devices
            // unfortunately, i couldn't find a way to get the device when polling,
            // so had to change this to use events and flush on read
            // down
            mapping.action.action.started += ctx => {
                // Debug.Log($"[unity-hawk] pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id],
                    isPressed = true
                });
            };
            // release
            mapping.action.action.canceled += ctx => {
                Debug.Log($"pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id],
                    isPressed = false
                });
            };
        }
#else
        Debug.LogError("GenericInputProvider will not work unless the new input system is enabled.");
#endif
    }

    public override List<InputEvent> InputForFrame() {
        // flush input event list
        // maybe this is bad?
        var flush = new List<InputEvent>(pressed);
        pressed.Clear();

#if ENABLE_INPUT_SYSTEM
#endif
        return flush;
    }

    // queries
    public int GamepadIndex{
        get {
            return gamepadIndex;
        }
        set {
            gamepadIndex = value;
        }
    }

    private InputDevice targetDevice {
        // TODO: maybe cache this and update only when detecting new input
        get {
            InputDevice targetGamepad = null;
            try {
                return source switch {
                    // TODO: multiple keyboards?
                    Source.Keyboard => InputSystem.GetDevice<Keyboard>(),
                    Source.Gamepad => Gamepad.all[gamepadIndex],
                    _ => null
                };
            } catch (ArgumentOutOfRangeException) {
                Debug.LogWarning($"[unity-hawk] gamepad #{gamepadIndex} out of range");
                return null;
            }
        }
    }
}

}