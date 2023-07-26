using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk {

// input in Unity format
// [currently only keyboard, no gamepad/mouse support yet]
public struct InputEvent {
    public KeyCode key;
    public bool isPressed; // either pressed or released this frame
}

// Generic interface for providing input
// (Could make custom implementations of this for doing input remapping on the unity side,
// or programmatically generating input for plunderludic purposes)
public interface IInputProvider {
    public void Update();
    public InputEvent? DequeueEvent();
}

// Provide input from Unity
public class InputProvider : IInputProvider {
    Queue<InputEvent> _eventQueue;
    KeyCode[] _allKeyCodes;

    public InputProvider() {
        _eventQueue = new();
        _allKeyCodes = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
    }

    public void Update() {
        // Grab Unity input and add to the queue.
        foreach(KeyCode k in _allKeyCodes)
        {
            bool interaction = false;
            bool isPressed = false;
    
            if (Input.GetKeyDown(k)) {
                // Debug.Log("key down: " + k);
                interaction = true;
                isPressed = true;
            }
            if (Input.GetKeyUp(k)) {
                // Debug.Log("key up: " + k);
                interaction = true;
                isPressed = false;
            }
            if (interaction) {
                _eventQueue.Enqueue(new InputEvent {
                    key = k,
                    isPressed = isPressed
                });
            }
        }
    }

    public InputEvent? DequeueEvent() {
        return _eventQueue.Count == 0 ? null : _eventQueue.Dequeue();
    }
}

}