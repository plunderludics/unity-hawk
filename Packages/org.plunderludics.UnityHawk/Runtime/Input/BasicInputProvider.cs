using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

// Just gets key events directly from Unity
[DefaultExecutionOrder(-1000)] // Kinda hacky but this has to run before Emulator or there will be a 1-frame input delay.
public class BasicInputProvider : InputProvider {
    System.Array _allKeyCodes;
    List<InputEvent> pressedThisFrame;

    void OnEnable() {
        pressedThisFrame = new();
#if ENABLE_INPUT_SYSTEM
        _allKeyCodes = System.Enum.GetValues(typeof(Key)); // for new inputsystem
#else
        _allKeyCodes = System.Enum.GetValues(typeof(KeyCode)); // for old inputsystem
#endif
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

    void Poll() {
        // Grab Unity input and add to the queue.
        // [assuming/hoping that the key codes are ~same between old and new inputsystem]
        foreach(var k in _allKeyCodes)
        {
            bool interaction = false;
            bool isPressed = false;
            string keyName = null;

#if ENABLE_INPUT_SYSTEM
            if ((Key)k == Key.None || (Key)k == Key.IMESelected) continue;
            if (Keyboard.current[(Key)k].wasPressedThisFrame) {
                interaction = true;
                isPressed = true;
            }
            if (Keyboard.current[(Key)k].wasReleasedThisFrame) {
                interaction = true;
                isPressed = false;
            }
            keyName = System.Enum.GetName(typeof(Key), k);
#else
            if (Input.GetKeyDown((KeyCode)k)) {
                // Debug.Log("key down: " + k);
                interaction = true;
                isPressed = true;
            }
            if (Input.GetKeyUp((KeyCode)k)) {
                // Debug.Log("key up: " + k);
                interaction = true;
                isPressed = false;
            }
            keyName = System.Enum.GetName(typeof(KeyCode), k);
#endif
            if (interaction) {
                // Debug.Log($"key event: {k} {isPressed}");
                pressedThisFrame.Add(new InputEvent {
                    keyName = keyName,
                    isPressed = isPressed
                });
            }
        }
    }

    public override List<InputEvent> InputForFrame() {
        var toReturn = new List<InputEvent>(pressedThisFrame);
        pressedThisFrame = new ();
        return toReturn;
    }

    public override Dictionary<string, int> AxisValuesForFrame() {
        // No support for analog input
        return new();
    }
}

}