using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using Plunderludics.UnityHawk;

namespace UnityHawk {

// Convert from Unity input format to BizHawk input format
public static class ConvertInput {
    public static Plunderludics.UnityHawk.InputEvent ToBizHawk(UnityHawk.InputEvent ie) {
        // Right now formats are the same, conversion is straightforward (should we just use the bizhawk class directly?)
        return new Plunderludics.UnityHawk.InputEvent {
            name = ie.name,
            value = ie.value,
            controller = ie.controller,
            isAnalog = ie.isAnalog
        };
    }
    // private static string UnityKeyNameToBizHawkKeyName(string key) {
    //     // Unity and BizHawk naming conventions are slightly different so have to convert some
    //     // TODO figure out a more robust way of doing this
    //     if (key == "Return") {
    //         return "Enter";
    //     } else if (key == "UpArrow") {
    //         return "Up";
    //     } else if (key == "DownArrow") {
    //         return "Down";
    //     } else if (key == "RightArrow") {
    //         return "Right";
    //     } else if (key == "LeftArrow") {
    //         return "Left";
    //     } else {
    //         return key;
    //     }
    // }
}

}