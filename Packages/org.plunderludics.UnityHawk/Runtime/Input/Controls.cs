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
    public enum InputSourceType {
        KeyCode,                // Keyboard code - should work with either new or legacy input system
        LegacyAxis,             // Legacy input system axis (Unity input manager)
        InputActionReference    // New input system action reference
    }

    [System.Serializable]
    public struct KeyCode2Control {
        public bool Enabled;
        public InputSourceType sourceType;
        
        // Input source (only one used based on sourceType)
        public KeyCode Key;                     // For KeyCode inputs
        public string AxisName;                 // For legacy axis inputs
#if ENABLE_INPUT_SYSTEM
        public UnityEngine.InputSystem.InputActionReference ActionRef; // For new input system actions
#endif
        
        // Target
        [Tooltip("Bizhawk control name e.g. \"D-Pad Up\" (Don't include \"P1 \" prefix!)")]
        public string Control;
        [Tooltip("Which controller this key is for (can be None)")]
        public Controller Controller;
        
        // For sending analog input to bizhawk
        [Tooltip("Whether this is an analog input on the bizhawk side")]
        public bool IsAnalog;
        [Tooltip("Minimum value for analog input (probably 0)")]
        public int MinValue;
        [Tooltip("Maximum value for analog input (probably 255)")]
        public int MaxValue;
    }

    // Mappings from keyname to bizhawk console button name (can be many-to-many)
    [SerializeField] List<KeyCode2Control> mappings;

    // Clone constructor
    public Controls(Controls controls) {
        mappings = new (controls.mappings);
    }

    // Map from a keyname to a list of (button name, controller) tuples
    // TODO update
    public List<(string, Controller)> this[KeyCode key] {
        get {
            // Super inefficient TODO should probably store a Dictionary<string, List<(string, Controller)>> that gets updated when mappings changes
            List<(string, Controller)> result = new();
            foreach (var mapping in mappings) {
                if (!mapping.Enabled) continue;
                if (mapping.sourceType == InputSourceType.KeyCode && mapping.Key == key) {
                    result.Add((mapping.Control, mapping.Controller));
                }
            }
            return result;
        }
    }
    
    // Get all KeyCodes that are mapped in the controls
    public HashSet<KeyCode> AllKeyCodes => mappings.Where(m => m.Enabled && m.sourceType == InputSourceType.KeyCode).Select(m => m.Key).ToHashSet();
    
    // Note: Slightly problematic because control labels are specific to the core rather than the system,
    // so e.g. Nymashock (PSX) uses "P1 △" where Octoshock (PSX) uses "P1 Triangle".
    // Bizhawk Emulation api doesn't seem to have a nice way to get the current core, so we can either
    // just hope the choice of core is consistent, or just include mappings for both labels in the default Controls object
    public static ControlsObject GetDefaultControlsObject(string systemId) {
        return Resources.Load<ControlsObject>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}
