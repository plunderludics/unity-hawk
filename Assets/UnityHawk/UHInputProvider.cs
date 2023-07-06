using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;

// This is our abstraction of the main functionality of the Input singleton class in BizHawk
public interface IInputProvider {
    public InputEvent DequeueEvent();
}

// Provide input from Unity
// [currently pretty hacky]
public class UHInputProvider : IInputProvider {

    Queue<InputEvent> _eventQueue;

    public UHInputProvider() {
        _eventQueue = new();
    }

    public void Update() {
        // Grab Unity input, convert to InputEvents, and add to the queue.
        
        // Big hack to check all keys
        // [not efficient - this GetValues call is currently contributing like 50% of the runtime each frame lol]
        foreach(KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
        {
            bool e = false;
            InputEventType t = InputEventType.Press;

            if (Input.GetKeyDown(k)) {
                // Debug.Log("key down: " + k);
                e = true;
            }
            if (Input.GetKeyUp(k)) {
                // Debug.Log("key up: " + k);
                e = true;
                t = InputEventType.Release;
            }

            if (e) {
                // Another big hack to figure out the name of the key
                string unityButtonName = System.Enum.GetName(typeof(KeyCode), k);
                string bizhawkButtonName = UnityKeyNameToBizHawkKeyName(unityButtonName);
                uint mods = 0; // ignore modifier keys for now
                List<string> emptyList = new(); // dunno
                var ie = new InputEvent
                {
                    EventType = t,
                    LogicalButton = new(bizhawkButtonName, mods, () => emptyList),
                    Source = ClientInputFocus.Keyboard // idk what this is
                };
                _eventQueue.Enqueue(ie);
            }
        }
    }

    public InputEvent DequeueEvent() {
        return _eventQueue.Count == 0 ? null : _eventQueue.Dequeue();
    }

    private string UnityKeyNameToBizHawkKeyName(string key) {
        // Unity and BizHawk naming conventions are slightly different so have to convert some
        // TODO figure out a more robust way of doing this
        if (key == "Return") {
            return "Enter";
        } else if (key == "UpArrow") {
            return "Up";
        } else if (key == "DownArrow") {
            return "Down";
        } else if (key == "RightArrow") {
            return "Right";
        } else if (key == "LeftArrow") {
            return "Left";
        } else {
            return key;
        }
    }
}