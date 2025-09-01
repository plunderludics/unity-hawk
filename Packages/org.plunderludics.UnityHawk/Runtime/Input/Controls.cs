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
    public struct ButtonMapping {
        [Tooltip("Whether this mapping is enabled")]
        public bool Enabled;
        [Tooltip("What type of input source this is")]
        public InputSourceType sourceType;
        [Tooltip("Key to press (only used if sourceType is KeyCode)")]
        public KeyCode Key;
        [Tooltip("Axis name (only used if sourceType is LegacyAxis)")]
        public string AxisName;
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action reference (only used if sourceType is InputActionReference)")]
        public UnityEngine.InputSystem.InputActionReference ActionRef;
#endif
        [Tooltip("Bizhawk control name e.g. \"D-Pad Up\" (Don't include \"P1 \" prefix!)")]
        public string EmulatorButtonName;
        [Tooltip("Which controller this key is for (can be None)")]
        public Controller Controller;
    }

    [System.Serializable]
    public struct AxisMapping {
        [Tooltip("Whether this mapping is enabled")]
        public bool Enabled;
        [Tooltip("What type of input source this is")]
        public InputSourceType sourceType;
        [Tooltip("Negative key")]
        public KeyCode NegativeKey;
        [Tooltip("Positive key")]
        public KeyCode PositiveKey;
        [Tooltip("Axis name")]
        public string AxisName;
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input action reference")]
        public UnityEngine.InputSystem.InputActionReference ActionRef;
#endif
        [Tooltip("Bizhawk axis name name e.g. \"Left Stick Left / Right\" (Don't include \"P1 \" prefix!)")]
        public string EmulatorAxisName;
        [Tooltip("Which controller this axis is for (can be None)")]
        public Controller Controller;
        [Tooltip("Minimum value for this axis (probably 0)  ")]
        public int MinValue;
        [Tooltip("Maximum value for this axis (probably 255)")]
        public int MaxValue;
    }

    // Mappings from input source to bizhawk control (can be many-to-many)
    [SerializeField] List<ButtonMapping> buttonMappings;
    [SerializeField] List<AxisMapping> axisMappings;

    // Clone constructor
    public Controls(Controls controls) {
        buttonMappings = new (controls.buttonMappings);
        axisMappings = new (controls.axisMappings);
    }

    // Get all enabled mappings
    public List<ButtonMapping> ButtonMappings => buttonMappings.Where(m => m.Enabled).ToList();
    public List<AxisMapping> AxisMappings => axisMappings.Where(m => m.Enabled).ToList();
    
    // Note: Slightly problematic because control labels are specific to the core rather than the system,
    // so e.g. Nymashock (PSX) uses "P1 △" where Octoshock (PSX) uses "P1 Triangle".
    // Bizhawk Emulation api doesn't seem to have a nice way to get the current core, so we can either
    // just hope the choice of core is consistent, or just include mappings for both labels in the default Controls object
    public static ControlsObject GetDefaultControlsObject(string systemId) {
        return Resources.Load<ControlsObject>(Path.Join(Paths.defaultControlsResourceDir, systemId));
    }
}
}
