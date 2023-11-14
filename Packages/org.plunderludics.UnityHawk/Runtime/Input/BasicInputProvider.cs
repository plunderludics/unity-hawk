using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

// Just gets key events directly from Unity (via old input system)
// TODO support new input system as well
public class BasicInputProvider : InputProvider {
    // Queue<InputEvent> _eventQueue;
    System.Array _allKeyCodes;

    public BasicInputProvider() {
#if ENABLE_INPUT_SYSTEM
        _allKeyCodes = System.Enum.GetValues(typeof(Key)); // for new inputsystem
#else
        _allKeyCodes = System.Enum.GetValues(typeof(KeyCode)); // for old inputsystem
#endif
    }

    public override List<InputEvent> InputForFrame() {
        List<InputEvent> pressed = new();
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
                pressed.Add(new InputEvent {
                    keyName = keyName,
                    isPressed = isPressed
                });
            }
        }

        return pressed;
    }
}

}