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
    
    KeyCode[] _allKeyCodes;
    List<InputEvent> pressedThisFrame;

    void OnEnable() {
        pressedThisFrame = new();
        _allKeyCodes = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
    
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
    }

    void Poll() {
        if (useControlsObject || useDefaultControls) controls = controlsObject?.Controls;
        if (controls == null) return;
        // Grab Unity input and add to the queue.
        // [assuming/hoping that the key codes are ~same between old and new inputsystem]
        foreach(var kc in _allKeyCodes)
        {
            bool interaction = false;
            bool isPressed = false;

#if ENABLE_INPUT_SYSTEM
            Key key = KeyCodeToKey(kc);
            if (key == Key.None || key == Key.IMESelected) continue;
            if (Keyboard.current[key].wasPressedThisFrame) {
                interaction = true;
                isPressed = true;
            }
            if (Keyboard.current[key].wasReleasedThisFrame) {
                interaction = true;
                isPressed = false;
            }
#else
            if (Input.GetKeyDown(kc)) {
                // Debug.Log("key down: " + k);
                interaction = true;
                isPressed = true;
            }
            if (Input.GetKeyUp(kc)) {
                // Debug.Log("key up: " + k);
                interaction = true;
                isPressed = false;
            }
#endif
            // keyName = System.Enum.GetName(typeof(KeyCode), kc);
            if (interaction) {
                List<(string, Controller)> controlNames = controls[kc];
                if (controlNames == null) return;
                foreach ((string name, Controller controller) in controlNames) {
                    pressedThisFrame.Add(new InputEvent {
                        name = name,
                        value = isPressed ? 1 : 0,
                        controller = controller,
                        isAnalog = false
                    });
                }
            }
        }
    }

    public override List<InputEvent> InputForFrame() {
        var baseInputs = base.InputForFrame();
        var myInputs = new List<InputEvent>(pressedThisFrame);
        pressedThisFrame.Clear(); // TODO: Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        return baseInputs.Concat(myInputs).ToList();
    }

#if ENABLE_INPUT_SYSTEM
    Key KeyCodeToKey(KeyCode kc) {
        // Pretty ugly but works for most keys... TODO better solution
        string name = System.Enum.GetName(typeof(KeyCode), kc);        
        try {
            if (name == "Return") name = "Enter"; // Urgh
            return (Key)System.Enum.Parse(typeof(Key), name);
        } catch (System.ArgumentException e) {
            // Debug.LogWarning($"KeyCode {kc} not found in Key enum: {e}");
            return Key.None;
        }
    }
#endif
}

}