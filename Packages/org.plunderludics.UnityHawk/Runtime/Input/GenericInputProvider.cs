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
// [See https://learn.microsoft.com/en-us/dotnet/api/system.linq.lookup-2]

// TODO currently doesn't work for any extra inputs added after Start() gets called, but it should
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
    public class Action2Key {
        [FormerlySerializedAs("Name")]
        [FormerlySerializedAs("name")]
        [FormerlySerializedAs("keyName")]
        [Tooltip("Name of key/axis on the Bizhawk side")]
        public string inputName;

        public InputActionReference action;
        public bool enabled = true;
        public Action2Key(Action2Key other) {
            action = other.action;
            inputName = other.inputName;
            enabled = other.enabled;
        }
    }
    [Serializable]
    public class Action2Axis : Action2Key {
        public float scale = 1f;
        public Action2Axis(Action2Axis other) : base(other) {
            scale = other.scale;
        }
    }

    [Header("Mappings")]
    [Tooltip("whether to use a separate scriptable object or define the mapping directly in this component")]
    [SerializeField] bool useMappingObject;

    [HideIf("useMappingObject")]
    [Tooltip("the unity input to bizhawk keyboard mapping")]
    [SerializeField] public List<Action2Key> keyMappings;
    [HideIf("useMappingObject")]
    [Tooltip("the unity input to bizhawk analog input mapping")]
    [SerializeField] public List<Action2Axis> axisMappings;

    [ShowIf("useMappingObject")]
    [SerializeField] GenericInputMappingObject mappingObject;

    [Tooltip("Max value of axis input")]
    [SerializeField] float axisScale = 10000; // [Idk about this but 10000 seems to be ~right for N64 at least]

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

        var mappingsDict = keyMappings.ToDictionary(
            m => m.action.action.id,
            m => m
        );
        foreach (var a2k in keyMappings) {
            // Add press/release callbacks for key mappings
            if (a2k.action.action.type != InputActionType.Button) {
                Debug.LogWarning($"Mapping from {a2k.action.action.name} to {a2k.inputName} is type {a2k.action.action.type}, should probably be Button for key events");
            }

            // [I don't get why you have to manually enable all the actions, but ok]
            a2k.action.action.Enable();

            // NOTE from mut: i think this is a MAJOR hack to avoid using the player feature
            // unity has by default, where it manages creating new players for devices
            // unfortunately, i couldn't find a way to get the device when polling,
            // so had to change this to use events and flush on read
            // press
            a2k.action.action.started += ctx => {
                // Debug.Log($"[unity-hawk] pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                if (!mappingsDict[ctx.action.id].enabled) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id].inputName,
                    isPressed = true
                });
            };
            // release
            a2k.action.action.canceled += ctx => {
                // Debug.Log($"pressed {ctx.action.name} {ctx.control.device.name}");
                if(ctx.control.device != targetDevice) {
                    return;
                }
                if (!mappingsDict[ctx.action.id].enabled) {
                    return;
                }
                pressed.Add(new InputEvent {
                    keyName = mappingsDict[ctx.action.id].inputName,
                    isPressed = false
                });
            };
        }

        // Also have to enable all the axis mappings
        foreach (var a2a in axisMappings) {
            a2a.action.action.Enable();
        }
    }

    public override List<InputEvent> InputForFrame() {
        // flush input event list
        // maybe this is bad?
        var flush = new List<InputEvent>(pressed);
        pressed.Clear(); // Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        // Debug.Log($"GenericInputProvider returning: {flush.Count} events");
        return flush.Concat(base.InputForFrame()).ToList(); ;
    }

    public override Dictionary<string, int> AxisValuesForFrame()
    {
        var axisValues = new Dictionary<string, int>();

        // Send latest values for actions of type Value or PassThrough (not Button)
        foreach (var a2k in axisMappings) {
            if (!a2k.enabled) continue;

            if (a2k.action.action.type == InputActionType.Button) {
                Debug.LogWarning($"Mapping from {a2k.action.action.name} to {a2k.inputName} is type {a2k.action.action.type}, should probably be PassThrough or Value for analog inputs");
            }

            axisValues[a2k.inputName] = (int)(axisScale*a2k.scale*a2k.action.action.ReadValue<float>());
        }

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
        // So that we can copy from the mappingObject into the mapping
        // and then make changes without affecting the original
        keyMappings = mappingObject.keyMappings.Select((Action2Key a2k) => {
            return new Action2Key(a2k);
        }).ToList();

        axisMappings = mappingObject.axisMappings.Select((Action2Axis a2a) => {
            return new Action2Axis(a2a);
        }).ToList();
    }
#else
    // Input system not enabled, just log an error on start
    void Start() {
        Debug.LogError("GenericInputProvider will not work because the new input system is not enabled.");
    }

    // Still need a dummy implementation of the interface:
    public override List<InputEvent> InputForFrame() {return new();}
    public override Dictionary<string, int> AxisValuesForFrame() {return new();}
#endif
    }

}