// Set of mappings that maps from keynames to BizHawk buttons
// [Do we want another version of this that supports inputsystem actions? Idk]
// TODO would be nice to have a slightly nicer editor too

using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BizHawk.Common.CollectionExtensions;

namespace UnityHawk {

[System.Serializable]
public class Controls {
    [System.Serializable]
    public struct KeyCode2Control {
        public bool Enabled;
        public KeyCode Key; // Keyboard key name e.g. "Z"
        public string Control; // Bizhawk control name e.g. "D-Pad Up" (Don't include "P1 " prefix!))
        public Controller Controller; // Which controller this key is for (can be None)
    }

    // Mappings from keyname to bizhawk console button name (can be many-to-many)
    [SerializeField] List<KeyCode2Control> mappings;

    // Clone constructor
    public Controls(Controls controls) {
        mappings = new (controls.mappings);
    }

    // Map from a keyname to a list of (button name, controller) tuples
    public List<(string, Controller)> this[KeyCode key] {
        get {
            // Super inefficient TODO should probably store a Dictionary<string, List<(string, Controller)>> that gets updated when mappings changes
            List<(string, Controller)> result = new();
            foreach (var mapping in mappings) {
                if (mapping.Key == key && mapping.Enabled) {
                    result.Add((mapping.Control, mapping.Controller));
                }
            }
            return result;
        }
    }
    
    public static ControlsObject GetDefaultControlsObject(string systemId) {
        return Resources.Load<ControlsObject>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}
