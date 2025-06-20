// Scriptable object that maps from keynames to BizHawk button names (e.g. A)
// [Do we want another version of this that supports inputsystem actions? Idk]
// TODO would be nice to have a slightly nicer editor too

using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BizHawk.Common.CollectionExtensions;

namespace UnityHawk {
[CreateAssetMenu(fileName = "Controls", menuName = "Plunderludics/UnityHawk/Controls", order = 0)]
public class Controls: ScriptableObject {

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

    [System.Serializable]
    public class KeyCode2Control {
        public bool Enabled = true;
        public KeyCode Key; // Keyboard key name e.g. "Z"
        public string Control; // Bizhawk control name e.g. "D-Pad Up" (Don't include "P1 " prefix!))
        public Controller Controller = Controller.P1; // Which controller this key is for
    }

    // Mappings from keyname to bizhawk console button name (can be many-to-many)
    [SerializeField] List<KeyCode2Control> mappings;

    // Map from a keyname to a list of bizhawk button names
    public List<string> this[KeyCode key] {
        get {
            // Super inefficient TODO should probably store a Dictionary<string, List<string>> that gets updated when mappings changes
            List<string> result = new();
            foreach (var mapping in mappings) {
                if (mapping.Key == key && mapping.Enabled) {
                    result.Add(MakeBizhawkButtonName(mapping.Control, mapping.Controller));
                }
            }
            return result;
        }
    }

    // Add the "P1 "/"P2 "/etc prefix if controller is not none
    public static string MakeBizhawkButtonName(string control, Controller controller) {
        if (controller == Controller.None) return control;
        return $"P{(int)controller} {control}";
    }
    
    public static Controls GetDefaultControls(string systemId) {
        return Resources.Load<Controls>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}