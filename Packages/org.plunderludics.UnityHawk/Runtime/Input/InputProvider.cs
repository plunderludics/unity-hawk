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

    // Return a list of input events for the frame, in chronological order
    public abstract List<InputEvent> InputForFrame();
    public abstract Dictionary<string, int> AxisValuesForFrame();
}

}