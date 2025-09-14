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
[System.Serializable]
public struct InputEvent {
    public string name; // Must correspond to emulator button/axis name (without controller prefix) E.g. "R2"
    public int value;  // 0 or 1 for buttons; for analog inputs, range depends on the emulator (0-255 for Nymashock PSX core)
    public Controller controller;
    public bool isAnalog;
    public override string ToString() => $"{name}:{value}";

    public InputEvent(string name, int value, Controller controller = Controller.P1, bool isAnalog = false) {
        this.name = name;
        this.value = value;
        this.controller = controller;
        this.isAnalog = isAnalog;
    }
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
    public void AddInputEvent(InputEvent ie) {
        _addedInputs.Add(ie);
    }
}

}