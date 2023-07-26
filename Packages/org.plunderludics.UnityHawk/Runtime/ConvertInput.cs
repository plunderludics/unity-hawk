using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using BizHawk.UnityHawk;

// Convert from Unity input format to BizHawk input format
public static class ConvertInput {
    public static BizHawk.UnityHawk.InputEvent ToBizHawk(UnityHawk.InputEvent ke) {
        string unityButtonName = System.Enum.GetName(typeof(KeyCode), ke.key);
        string bizhawkButtonName = UnityKeyNameToBizHawkKeyName(unityButtonName);

        return new BizHawk.UnityHawk.InputEvent {
            EventType = ke.isPressed ? InputEventType.Press : InputEventType.Release,
            ButtonName = bizhawkButtonName,
            Source = ClientInputFocus.Keyboard
        };
    }
    private static string UnityKeyNameToBizHawkKeyName(string key) {
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
