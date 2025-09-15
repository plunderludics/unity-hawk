// TODO some analog input support would be nice

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NaughtyAttributes;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using UnityEditor.EditorTools;
#endif

namespace UnityHawk {

// Just gets key events directly from Unity
[DefaultExecutionOrder(-1000)] // Kinda hacky but this has to run before Emulator or there will be a 1-frame input delay.
public class BasicInputProvider : InputProvider {
    [Tooltip("Emulator to use. If null, will look for attached Emulator")]
    public Emulator emulator;

    [Tooltip("Whether to use built-in default controls for the current system (will load once emulator is running)")]
    public bool useDefaultControls = true;
    
    [HideIf("useDefaultControls")]
    public bool useControlsObject = true;

    [DisableIf("useDefaultControls")]
    [ShowIf("useControlsObject")]
    public ControlsObject controlsObject;

    [HideIf("useControlsObject")]
    public Controls controls;
    
    List<InputEvent> eventsThisFrame;

    void OnEnable() {
        eventsThisFrame = new();
    
        if (!emulator) {
            emulator = GetComponent<Emulator>();
            if (!emulator) {
                Debug.LogWarning("BasicInputProvider has no specified or attached Emulator, will not be able to set controls correctly");
                return;
            }
        }
        emulator.OnRunning += OnNewRom;
        if (emulator.IsRunning) {
            // Already running, set controls now
            OnNewRom();
        }
    }

    // Runs when emulator starts or changes rom
    void OnNewRom() {
        // Debug.Log("BasicInputProvider: New rom started, setting controls");
        if (!useDefaultControls) return;

        string systemId = emulator.SystemId;
        controlsObject = Controls.GetDefaultControlsObject(systemId);
        if (controlsObject == null) {
            Debug.LogError($"No default controls found for platform '{systemId}', controls will not work");
        }

        EnableInputActions();
        // Debug.Log($"Setting controls to {controls} for system {systemId}");
    }

    // Poll for events in Update / FixedUpdate rather than in InputForFrame directly,
    // to avoid issues in the new inputsystem when UpdateMode is set to FixedUpdate
    void Update() {
#if ENABLE_INPUT_SYSTEM
        if (InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInFixedUpdate) {
            Poll();
        }
#else
        Poll();
#endif
    }

    void FixedUpdate() {
#if ENABLE_INPUT_SYSTEM
        if (InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate) {
            Poll();
        }
#endif
    }

    void OnValidate() {
        if (useDefaultControls) useControlsObject = true;
        if (useControlsObject && controlsObject != null) {
            controls = new(controlsObject.Controls); // Keep mapping synced so that we get pre-populated controls if we untick 'use controls object'
        }

        EnableInputActions();
    }

    void EnableInputActions() {
        if (controls == null) return;
        // We need to enable any InputActions that are referenced in the controls
#if ENABLE_INPUT_SYSTEM
        foreach(var mapping in controls.ButtonMappings) {
            if (mapping.sourceType == Controls.InputSourceType.InputActionReference
             && mapping.ActionRef != null
             && mapping.ActionRef.action != null) {
                mapping.ActionRef.action.Enable();
            }
        }
        foreach(var mapping in controls.AxisMappings) {
            if (mapping.sourceType == Controls.InputSourceType.InputActionReference
             && mapping.ActionRef != null
             && mapping.ActionRef.action != null) {
                mapping.ActionRef.action.Enable();
            }
        }
#endif
    }

    void Poll() {
        if (useControlsObject || useDefaultControls) controls = controlsObject?.Controls;
        if (controls == null) return;
        
        // Handle button mappings
        foreach(var mapping in controls.ButtonMappings) {
            switch (mapping.sourceType) {
                case Controls.InputSourceType.KeyCode:
                    HandleButtonKeyCodeMapping(mapping);
                    break;
                case Controls.InputSourceType.LegacyAxis:
                    HandleButtonLegacyAxisMapping(mapping);
                    break;
                case Controls.InputSourceType.InputActionReference:
                    HandleButtonInputActionReferenceMapping(mapping);
                    break;
            }
        }
        
        // Handle axis mappings
        foreach(var mapping in controls.AxisMappings) {
            switch (mapping.sourceType) {
                case Controls.InputSourceType.KeyCode:
                    HandleAxisKeyCodeMapping(mapping);
                    break;
                case Controls.InputSourceType.LegacyAxis:
                    HandleAxisLegacyAxisMapping(mapping);
                    break;
                case Controls.InputSourceType.InputActionReference:
                    HandleAxisInputActionReferenceMapping(mapping);
                    break;
            }
        }
    }
    
    private void HandleButtonKeyCodeMapping(Controls.ButtonMapping mapping) {
        bool interaction = false;
        bool isPressed = false;

#if ENABLE_INPUT_SYSTEM
        Key key = KeyCodeToKey(mapping.Key);
        if (Keyboard.current[key].wasPressedThisFrame) {
            interaction = true;
            isPressed = true;
        }
        if (Keyboard.current[key].wasReleasedThisFrame) {
            interaction = true;
            isPressed = false;
        }
#else
        if (Input.GetKeyDown(mapping.Key)) {
            interaction = true;
            isPressed = true;
        }
        if (Input.GetKeyUp(mapping.Key)) {
            interaction = true;
            isPressed = false;
        }
#endif

        if (interaction) {
            // Button mappings are always digital: send 0 on release, 1 on press
            int value = isPressed ? 1 : 0;
            eventsThisFrame.Add(new InputEvent {
                name = mapping.EmulatorButtonName,
                value = value,
                controller = mapping.Controller,
                isAnalog = false
            });
        }
    }

    private void HandleButtonLegacyAxisMapping(Controls.ButtonMapping mapping) {
#if ENABLE_LEGACY_INPUT_MANAGER
        // Legacy axis as button - use GetButton for button-like axes
        bool interaction = false;
        bool isPressed = false;
        if (Input.GetButtonDown(mapping.AxisName)) {
            interaction = true;
            isPressed = true;
        }
        if (Input.GetButtonUp(mapping.AxisName)) {
            interaction = true;
            isPressed = false;
        }

        if (interaction) {
            int value = isPressed ? 1 : 0;
            eventsThisFrame.Add(new InputEvent {
                name = mapping.EmulatorButtonName,
                value = value,
                controller = mapping.Controller,
                isAnalog = false
            });
        }
#else
        Debug.LogWarning("LegacyAxis mappings are not supported because legacy input manager is not enabled");
#endif
    }

    private void HandleButtonInputActionReferenceMapping(Controls.ButtonMapping mapping) {
        // Debug.Log($"Handling button input action reference mapping: {mapping.EmulatorButtonName}");
#if ENABLE_INPUT_SYSTEM
        if (mapping.ActionRef != null && mapping.ActionRef.action != null) {
            bool isPressed = mapping.ActionRef.action.WasPressedThisFrame();
            bool isReleased = mapping.ActionRef.action.WasReleasedThisFrame();

            // Debug.Log($"Button {mapping.ActionRef.action.name} was pressed: {isPressed}, released: {isReleased}");
            
            if (isPressed || isReleased) {
                eventsThisFrame.Add(new InputEvent {
                    name = mapping.EmulatorButtonName,
                    value = isReleased ? 0 : 1,
                    controller = mapping.Controller,
                    isAnalog = false
                });
            }
        }
#else
        Debug.LogWarning("InputActionReference mappings are not supported because new InputSystem is not enabled");
#endif
    }

    private void HandleAxisKeyCodeMapping(Controls.AxisMapping mapping) {
        bool key1Pressed = false;
        bool key2Pressed = false;

#if ENABLE_INPUT_SYSTEM
        Key negativeKey = KeyCodeToKey(mapping.NegativeKey);
        Key positiveKey = KeyCodeToKey(mapping.PositiveKey);
            
        key1Pressed = Keyboard.current[negativeKey].isPressed;
        key2Pressed = Keyboard.current[positiveKey].isPressed;
#else
        key1Pressed = Input.GetKey(mapping.NegativeKey);
        key2Pressed = Input.GetKey(mapping.PositiveKey);
#endif

        // Calculate axis value based on key presses
        int axisValue = mapping.MinValue; // Default to min value
        
        if (key1Pressed && !key2Pressed) {
            axisValue = mapping.MinValue;
        } else if (key2Pressed && !key1Pressed) {
            axisValue = mapping.MaxValue;
        } else {
            // Neither or both keys pressed - neutral position
            axisValue = (mapping.MinValue + mapping.MaxValue) / 2;
        }

        eventsThisFrame.Add(new InputEvent {
            name = mapping.EmulatorAxisName,
            value = axisValue,
            controller = mapping.Controller,
            isAnalog = true
        });
    }

    private void HandleAxisLegacyAxisMapping(Controls.AxisMapping mapping) {
#if ENABLE_LEGACY_INPUT_MANAGER
        // Legacy axis as axis - linear mapping from [-1, 1] to [MinValue, MaxValue]
        float axisValue = Input.GetAxis(mapping.AxisName);
        
        // Map from [-1, 1] to [MinValue, MaxValue]
        int mappedValue = Mathf.RoundToInt(Mathf.Lerp(mapping.MinValue, mapping.MaxValue, (axisValue + 1f) / 2f));
        
        eventsThisFrame.Add(new InputEvent {
            name = mapping.EmulatorAxisName,
            value = mappedValue,
            controller = mapping.Controller,
            isAnalog = true
        });
#else
        Debug.LogWarning("LegacyAxis mappings are not supported because legacy input manager is not enabled");
#endif
    }

    private void HandleAxisInputActionReferenceMapping(Controls.AxisMapping mapping) {
#if ENABLE_INPUT_SYSTEM
        if (mapping.ActionRef != null && mapping.ActionRef.action != null) {
            float axisValue = mapping.ActionRef.action.ReadValue<float>();

            // Debug.Log($"Axis {mapping.ActionRef.action.name} value: {axisValue}");
            
            // Map from [-1, 1] to [MinValue, MaxValue]
            int mappedValue = Mathf.RoundToInt(Mathf.Lerp(mapping.MinValue, mapping.MaxValue, (axisValue + 1f) / 2f));
            
            eventsThisFrame.Add(new InputEvent {
                name = mapping.EmulatorAxisName,
                value = mappedValue,
                controller = mapping.Controller,
                isAnalog = true
            });
        }
#else
        Debug.LogWarning("InputActionReference mappings are not supported because new InputSystem is not enabled");
#endif
    }

    public override List<InputEvent> InputForFrame() {
        var baseInputs = base.InputForFrame();
        var myInputs = new List<InputEvent>(eventsThisFrame);
        eventsThisFrame.Clear(); // TODO: Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        return baseInputs.Concat(myInputs).ToList();
    }

#if ENABLE_INPUT_SYSTEM
    static Dictionary<KeyCode, string> _keyCodeNameCache = new();
    static Dictionary<string, Key> _keyNameToKeyCache = new();

    // Convert legacy input manager KeyCode to new InputSystem Key
    Key KeyCodeToKey(KeyCode kc) {
        // Cache the name lookup
        if (!_keyCodeNameCache.TryGetValue(kc, out string name)) {
            name = System.Enum.GetName(typeof(KeyCode), kc);
            _keyCodeNameCache[kc] = name;
        }

        if (name == "Return") name = "Enter"; // Urgh

        // Cache the parse result
        if (!_keyNameToKeyCache.TryGetValue(name, out Key key)) {
            try {
                key = (Key)System.Enum.Parse(typeof(Key), name);
            } catch (System.ArgumentException) {
                key = Key.None;
            }
            _keyNameToKeyCache[name] = key;
        }

        return key;
    }
#endif
}

}