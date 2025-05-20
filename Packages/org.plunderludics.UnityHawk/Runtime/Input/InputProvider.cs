using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk {

// input in Unity format
// (basically same as InputEvent in BizHawk, but keep decoupled in case)
public struct InputEvent {
    public string name; // Must correspond to emulator button/axis name E.g. "P1 A"
    public int value;  // 0 or 1 for buttons, -INT_MAX - INT_MAX for analog (?)
    public int controller; // Starts from 1
    public bool isAnalog;
    public override string ToString() => $"{name}:{value}";
}

// Generic base class for providing input
// (Could make custom implementations of this for doing input remapping on the unity side,
// or programmatically generating input for plunderludic purposes)
public abstract class InputProvider : MonoBehaviour {
    // (derive from monobehaviour so params can be easily tweaked in inspector)

    List<InputEvent> _addedInputs = new();
    Dictionary<string, int> _addedAxisInputs = new();

    // Return a list of input events for the frame, in chronological order
    public virtual List<InputEvent> InputForFrame() {
        var toReturn = new List<InputEvent>(_addedInputs);
        _addedInputs.Clear(); // Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        return toReturn;
    }

    // public virtual Dictionary<string, int> AxisValuesForFrame() {
    //     var toReturn = new Dictionary<string, int>(_addedAxisInputs);
    //     _addedAxisInputs.Clear();
    //     return toReturn;
    // }

    public void AddInputEvent(InputEvent ie) {
        _addedInputs.Add(ie);
    }

    // public void AddAxisInputEvent(string axis, int value) {
    //     _addedAxisInputs.Add(axis, value);
    // }
}

}