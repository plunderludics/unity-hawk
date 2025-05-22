// Scriptable object that maps from keynames to BizHawk button names (e.g. A)
// [Do we want another version of this that supports inputsystem actions? Idk]
// TODO would be nice to have a slightly nicer editor too

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BizHawk.Common.CollectionExtensions;

namespace UnityHawk {
[CreateAssetMenu(fileName = "Controls", menuName = "Plunderludics/UnityHawk/Controls", order = 0)]
public class Controls: ScriptableObject {
    [System.Serializable]
    public class KeyCode2Control {
        public KeyCode Key; // Keyboard key name e.g. "Z"
        public string Control; // Bizhawk control name e.g. "D-Pad Up"
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

    static readonly List<(string System, string Filename)> _DefaultControlsForSystem = new () {
        // These files are in Resources/Controls/
        // ".asset" extension gets added automatically so this looks kind of stupid but just to be explicit about the filenames:
        ( "N64", "N64" ),
        ( "PSX", "PSX" ),
        ( "NES", "NES" )
        // TODO other platforms
    };
    public static Controls LoadDefaultControlsForSystem(string systemId) {
        string assetName = _DefaultControlsForSystem.FirstOrNull(x => x.System == systemId)?.Filename;
        if (string.IsNullOrEmpty(assetName)) {
            Debug.LogError($"No controls configured for system {systemId}, controls will not work");
            return null;
        }

        string path = Path.Combine(Paths.defaultControlsResourceDir, assetName);
        Controls controls = Resources.Load<Controls>(path);
        
        // (Using Resources.Load isn't really ideal cause it means all the Controls assets get included in build
        //  - TODO maybe figure out a more elegant way to auto-include whatever is needed)

        if (controls == null) {
            Debug.LogError($"Could not load controls for system {systemId} from {assetName}");
            return null;
        }

        return controls;
    }
}
}