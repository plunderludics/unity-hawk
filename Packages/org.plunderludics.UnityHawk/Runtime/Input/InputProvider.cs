using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk {

// input in Unity format
// [currently only keyboard, no gamepad/mouse support yet]
public struct InputEvent {
    public string keyName; // should match the enum name here [https://docs.unity3d.com/ScriptReference/KeyCode.html]
                           // hopefully equivalent to new inputsystem as well
    public bool isPressed; // either pressed or released this frame
}

// Generic base class for providing input
// (Could make custom implementations of this for doing input remapping on the unity side,
// or programmatically generating input for plunderludic purposes)
public abstract class InputProvider : MonoBehaviour {
    // (derive from monobehaviour so params can be easily tweaked in inspector)

    List<InputEvent> _addedInputs = new();

    // Return a list of input events for the frame, in chronological order
    public virtual List<InputEvent> InputForFrame() {
        var toReturn = new List<InputEvent>(_addedInputs);
        _addedInputs.Clear(); // Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        return toReturn;
    }
    public abstract Dictionary<string, int> AxisValuesForFrame();

    public void AddInputEvent(InputEvent ie) {
        _addedInputs.Add(ie);
    }
}

}