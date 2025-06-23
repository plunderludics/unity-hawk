using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using Plunderludics.UnityHawk.Shared;

namespace UnityHawk {

// Convert from Unity input format to BizHawk input format
// (The classes are almost identical but seems best to keep decoupled just in case)
public static class ConvertInput {
    public static Plunderludics.UnityHawk.Shared.InputEvent ToBizHawk(UnityHawk.InputEvent ie) {
        return new Plunderludics.UnityHawk.Shared.InputEvent {
            name = ie.name,
            value = ie.value,
            controller = (int)ie.controller,
            isAnalog = ie.isAnalog
        };
    }
}
}