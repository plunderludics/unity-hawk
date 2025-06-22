using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk {

public enum Controller {
    None = 0,
    P1 = 1,
    P2 = 2,
    P3 = 3,
    P4 = 4,
    P5 = 5,
    P6 = 6,
    P7 = 7,
    P8 = 8
} // (Could just be an int but this is convenient for inspector dropdown)


// input in Unity format
// (basically same as InputEvent in BizHawk, but keep decoupled in case)
public struct InputEvent {
    public string name; // Must correspond to emulator button/axis name E.g. "P1 A"
    public int value;  // 0 or 1 for buttons, -INT_MAX - INT_MAX for analog (?)
    public Controller controller;
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