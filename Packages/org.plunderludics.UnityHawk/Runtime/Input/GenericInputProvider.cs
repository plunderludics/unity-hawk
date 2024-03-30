using System;
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

// Map from InputSystem input actions to bizhawk keys according to user-specified mapping
// [Currently has to be a single key for each action, but no reason to have that constraint in theory]
[ExecuteInEditMode]
public class GenericInputProvider : InputProvider {
// Most of this class is only compiled when new input system is available:
#if ENABLE_INPUT_SYSTEM
    const string KEYBOARD_NAME = "Keyboard";

    enum Source {
        Keyboard,
        Gamepad
    }

    [Serializable]
    // class rather than struct so that it can be conveniently edited by reference at runtime
    public class Action2Key {
        public Action2Key(Action2Key other) {
            action = other.action;
            keyName = other.keyName;
            enabled = other.enabled;
        }
        public InputActionReference action;
        public string keyName;
        public bool enabled = true;
    }

    [Header("Mappings")]
    [Tooltip("whether to use a separate scriptable object or define the mapping directly in this component")]
    [SerializeField] bool useMappingObject;

    [HideIf("useMappingObject")]
    [Tooltip("the unity input to bizhawk keyboard mapping. (Doesn't support editing at runtime!)")]
    [SerializeField] List<Action2Key> mapping;

    [FormerlySerializedAs("mappings")]
    [ShowIf("useMappingObject")]
    [SerializeField] GenericInputMappingObject mappingObject;

    [Header("Sources")]
    [Tooltip("can the input come from any source?")]
    [SerializeField] bool anySource = true;

    [HideIf("anySource")]
    [Tooltip("if not, what kind of source can the input come from?")]
    [SerializeField] Source source = Source.Keyboard;

    bool showGamePadIndex => !anySource && source == Source.Gamepad;
    [ShowIf("showGamePadIndex")]
    [Tooltip("if gamepad, whats the index?")]
    [SerializeField] int gamepadIndex = 0;

    List<InputEvent> pressed = new();
    public virtual void Start() {
        if (useMappingObject) {
            CopyMappingFromMappingObject();
        }

        var mappingsDict = mapping.ToDictionary(
            m => m.action.action.id,
            m => m
        );
        foreach (var action2Key in mapping) {
            // [I don't get why you have to manually enable all the actions, but ok]
            action2Key.action.action.Enable();

            // NOTE from mut: i think this is a MAJOR hack to avoid using the player feature
            // unity has by default, where it manages creating new players for devices
            // unfortunately, i couldn't find a way to get the device when polling,
            // so had to change this to use events and flush on read
            // down
            action2Key.action.action.started += ctx => {
                // Debug.Log($"[unity-hawk] pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                if (!mappingsDict[ctx.action.id].enabled) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id].keyName,
                    isPressed = true
                });
            };
            // release
            action2Key.action.action.canceled += ctx => {
                // Debug.Log($"pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                if (!mappingsDict[ctx.action.id].enabled) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id].keyName,
                    isPressed = false
                });
            };
        }
    }
    
    public override List<InputEvent> InputForFrame() {
        // flush input event list
        // maybe this is bad?
        var flush = new List<InputEvent>(pressed);
        pressed.Clear();
        return flush;
    }
    
    public override Dictionary<string, int> AxisValuesForFrame()
    {
        // TODO
        var axisValues = new Dictionary<string, int>();
        // axisValues["X1 LeftThumbX Axis"] = 9999; // this seems to be roughly the max value for bizhawk (at least for n64)
        return axisValues;
    }

    void Update() {
        // Minor hack so the mapping list is populated from the mapping object when unticking 'use mapping object' in edit mode
        if (useMappingObject) {
            CopyMappingFromMappingObject();
        }
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

    private void CopyMappingFromMappingObject() {
        // Have to copy every mapping which is annoying
        // So that we can copy from the mappingObject into the mapping
        // and then make changes without affecting the original
        mapping = mappingObject.All.Select((Action2Key a2k) => {
            return new Action2Key(a2k);
        }).ToList();
    }
#else
    // Input system not enabled, just log an error on start
    void Start() {
        Debug.LogError("GenericInputProvider will not work because the new input system is not enabled.");
    }
    
    // Still need a dummy implementation of the interface:
    public override List<InputEvent> InputForFrame() {return new()}
    public override Dictionary<string, int> InputForFrame() {return new()}
#endif
    }

}