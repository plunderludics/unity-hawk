// Set of mappings that maps from Unity keycodes to BizHawk buttons
// Currently only used by BasicInputProvider
// TODO: Support for analog inputs? Support for InputSystem actions or legacy input manager axes?

using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityHawk {

[System.Serializable]
public class Controls {
    [System.Serializable]
    public struct KeyCode2Control {
        public bool Enabled;
        public KeyCode Key; // Keyboard key name e.g. "Z"
        public string Control; // Bizhawk control name e.g. "D-Pad Up" (Don't include "P1 " prefix!)
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
    
    // Get all KeyCodes that are mapped in the controls
    public HashSet<KeyCode> AllKeyCodes => mappings.Where(m => m.Enabled).Select(m => m.Key).ToHashSet();

    // Note: Slightly problematic because control labels are specific to the core rather than the system,
    // so e.g. Nymashock (PSX) uses "P1 △" where Octoshock (PSX) uses "P1 Triangle".
    // Bizhawk Emulation api doesn't seem to have a nice way to get the current core, so we can either
    // just hope the choice of core is consistent, or just include mappings for both labels in the default Controls object
    public static ControlsObject GetDefaultControlsObject(string systemId) {
        return Resources.Load<ControlsObject>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}
