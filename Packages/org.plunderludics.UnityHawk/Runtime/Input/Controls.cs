// Scriptable object that maps from keynames to BizHawk button names (e.g. A)
// [Do we want another version of this that supports inputsystem actions? Idk]
// TODO would be nice to have a slightly nicer editor too

using UnityEngine;
using System.Collections.Generic;
using System.IO;
<<<<<<< HEAD
=======
using System.Linq;
using System.IO;
using BizHawk.Common.CollectionExtensions;
>>>>>>> unity-hawk-ext-tool

namespace UnityHawk {
[CreateAssetMenu(fileName = "Controls", menuName = "Plunderludics/UnityHawk/Controls", order = 0)]
public class Controls: ScriptableObject {
    [System.Serializable]
    public class KeyCode2Control {
        public KeyCode Key; // Keyboard key name e.g. "Z"
        public string Control; // Bizhawk control name e.g. "D-Pad Up"
        // TODO:
        // bool enabled = true;
        // int controller = 1; // 1-based
    }

    // Mappings from keyname to bizhawk console button name (can be many-to-many)
    [SerializeField] List<KeyCode2Control> mappings;

    // Map from a keyname to a list of bizhawk button names
    public List<string> this[KeyCode key] {
        get {
            // Super inefficient TODO should probably store a Dictionary<string, List<string>> that gets updated when mappings changes
            List<string> result = new();
            foreach (var mapping in mappings) {
                if (mapping.Key == key) {
                    result.Add(mapping.Control);
                }
            }
            return result;
        }
    }

    public static Controls GetDefaultControls(string systemId) {
        return Resources.Load<Controls>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}