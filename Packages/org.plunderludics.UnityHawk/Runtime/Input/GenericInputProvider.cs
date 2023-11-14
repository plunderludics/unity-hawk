using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityHawk {

// Map from InputSystem input actions to bizhawk keys according to user-specified mapping
public class GenericInputProvider : InputProvider {
#if ENABLE_INPUT_SYSTEM
    // [Do it this way rather than having to add a SerializableDictionary implementation]
    [System.Serializable]
    public struct Action2Key {
        public InputActionReference action;
        public string keyName;
    }
    public List<Action2Key> mappings;

#endif
    public void Start() {
#if ENABLE_INPUT_SYSTEM
        // [I don't get why you have to manually enable all the actions, but ok]
        foreach (var mapping in mappings) {  
            mapping.action.action.Enable();
        }
#else
        Debug.LogError("GenericInputProvider will not work unless the new input system is enabled.");
#endif
    }

    public override List<InputEvent> InputForFrame() {
        List<InputEvent> pressed = new();

#if ENABLE_INPUT_SYSTEM
        foreach (var mapping in mappings) {  
            bool interaction = false;
            bool isPressed = false;
            string keyName = null;

            InputActionReference action = mapping.action;
            keyName = mapping.keyName;

            if (action.action.WasPressedThisFrame()) {
                // Debug.Log($"was pressed: {action}");
                interaction = true;
                isPressed = true;
            }
            if (action.action.WasReleasedThisFrame()) {
                interaction = true;
                isPressed = false;
            }

            if (interaction) {
                // Debug.Log($"key event: {keyName} {isPressed}");
                pressed.Add(new InputEvent {
                    keyName = keyName,
                    isPressed = isPressed
                });
            }
        }
#endif
        return pressed;
    }
}

}