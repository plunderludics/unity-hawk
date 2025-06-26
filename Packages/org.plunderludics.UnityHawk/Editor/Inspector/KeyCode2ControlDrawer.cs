// Improve UI for the 'Controls' object slightly

using BizHawk.Client.Common;
using UnityEditor;
using UnityEngine;
using UnityHawk;

namespace UnityHawk.Editor {
[CustomPropertyDrawer(typeof(Controls.KeyCode2Control))]
public class KeyCode2ControlDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var keyProp = property.FindPropertyRelative("Key");
        var controlProp = property.FindPropertyRelative("Control");
        var controllerProp = property.FindPropertyRelative("Controller");
        var enabledProp = property.FindPropertyRelative("Enabled");

        // Show a tag like e.g. "Up Arrow → P1 D-Pad Up"
        string controllerPrefix = controllerProp.intValue > 0 ? $"P{controllerProp.intValue} " : "";
        string customLabel = $"{keyProp.enumDisplayNames[keyProp.enumValueIndex]} → {controllerPrefix}{controlProp.stringValue}";
        Color c = GUI.color;
        GUI.color = enabledProp.boolValue ? Color.white : Color.gray; // Dim the color if not enabled
        EditorGUI.PropertyField(position, property, new GUIContent(customLabel), true);
        GUI.color = c; // Reset color
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
}