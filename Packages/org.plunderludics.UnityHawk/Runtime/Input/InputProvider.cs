using System.Collections.Generic;
using UnityEngine;

namespace UnityHawk {

/// <summary>
/// Controller number for an input event.
/// For consoles with no controllers, use None
/// </summary>
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

/// <summary>
/// Represents an input event (button press or axis value) to send to the BizHawk emulator.
/// </summary>
[System.Serializable]
public struct InputEvent {
    /// <summary>
    /// The name of the input event. Must correspond to emulator button/axis name E.g. "D-Pad Up", etc
    /// </summary>
    public string name;

    /// <summary>
    /// The value of the input event. 0 or 1 for buttons. Analog value depends on emulator - e.g. 0-255 for Nymashock (PSX)
    /// </summary>
    public int value;
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

/// <summary>
/// Base class for providing input to a UnityHawk Emulator. Override this to create a custom input provider.
/// </summary>
public abstract class InputProvider : MonoBehaviour {
    // (derive from monobehaviour so params can be easily tweaked in inspector)
    List<InputEvent> _addedInputs = new();

    /// <summary>
    /// Return a list of input events for the frame, in chronological order.
    /// InputProvider implementations should override this.
    /// </summary>
    /// <remarks>
    /// Should only be called by a single Emulator once per frame - will break if multiple clients use the same InputProvider.
    /// </remarks>
    public virtual List<InputEvent> InputForFrame() {
        var toReturn = new List<InputEvent>(_addedInputs);
        _addedInputs.Clear(); // Not ideal because will break if multiple clients use the same InputProvider, should clear at the end of the frame
        return toReturn;
    }

    /// <summary>
    /// Add an input event to the input provider.
    /// (This can be used as an alternative way of providing custom input without creating an InputProvider implementation.)
    /// </summary>
    /// <param name="ie">The input event to add.</param>
    public void AddInputEvent(InputEvent ie) {
        _addedInputs.Add(ie);
    }
}

}