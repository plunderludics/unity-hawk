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
        var enabledProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.Enabled));
        var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.sourceType));
        var keyProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.Key));
        var axisNameProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.AxisName));
#if ENABLE_INPUT_SYSTEM
        var actionRefProp = property.FindPropertyRelative("ActionRef");
#else
        SerializedProperty actionRefProp = null;
#endif
        var controlProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.Control));
        var controllerProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.Controller));
        var isAnalogProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.IsAnalog));
        var minValueProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.MinValue));
        var maxValueProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.MaxValue));

        // Create a custom label based on the source type
        string customLabel = CreateCustomLabel(sourceTypeProp, keyProp, axisNameProp, actionRefProp, controlProp, controllerProp);
        
        // Dim the color if not enabled
        Color c = GUI.color;
        GUI.color = enabledProp.boolValue ? Color.white : Color.gray;
        
        // Draw individual fields instead of the whole property
        DrawCustomFields(position, property, enabledProp, sourceTypeProp, keyProp, axisNameProp, actionRefProp, 
                        controlProp, controllerProp, isAnalogProp, minValueProp, maxValueProp, customLabel);
        
        GUI.color = c; // Reset color
    }

    private string CreateCustomLabel(SerializedProperty sourceTypeProp, SerializedProperty keyProp, 
        SerializedProperty axisNameProp, SerializedProperty actionRefProp, 
        SerializedProperty controlProp, SerializedProperty controllerProp)
    {
        var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
        string controllerPrefix = controllerProp.intValue > 0 ? $"P{controllerProp.intValue} " : "";
        string target = $"{controllerPrefix}{controlProp.stringValue}";
        
        string source = sourceType switch
        {
            Controls.InputSourceType.KeyCode => keyProp.enumDisplayNames[keyProp.enumValueIndex],
            Controls.InputSourceType.LegacyAxis => $"Axis: {axisNameProp.stringValue}",
#if ENABLE_INPUT_SYSTEM
            Controls.InputSourceType.InputActionReference => $"Action: {(actionRefProp?.objectReferenceValue?.name ?? "None")}",
#endif
            _ => "Unknown"
        };
        
        return $"{source} → {target}";
    }

    private void DrawCustomFields(Rect position, SerializedProperty property, SerializedProperty enabledProp, SerializedProperty sourceTypeProp,
        SerializedProperty keyProp, SerializedProperty axisNameProp, SerializedProperty actionRefProp,
        SerializedProperty controlProp, SerializedProperty controllerProp, SerializedProperty isAnalogProp,
        SerializedProperty minValueProp, SerializedProperty maxValueProp, string customLabel)
    {
        var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        
        // Calculate how many lines we need
        int lineCount = 2; // Enabled + SourceType
        if (sourceType == Controls.InputSourceType.KeyCode) lineCount++;
        if (sourceType == Controls.InputSourceType.LegacyAxis) lineCount++;
#if ENABLE_INPUT_SYSTEM
        if (sourceType == Controls.InputSourceType.InputActionReference) lineCount++;
#endif
        lineCount += 2; // Control + Controller
        if (isAnalogProp.boolValue) lineCount += 3; // IsAnalog + MinValue + MaxValue
        
        // Draw foldout header
        var foldoutRect = new Rect(position.x, position.y, position.width, lineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, customLabel, true);
        
        if (!property.isExpanded) return;
        
        // Draw fields
        float y = position.y + lineHeight + spacing;
        
        // Enabled
        var enabledRect = new Rect(position.x, y, position.width, lineHeight);
        EditorGUI.PropertyField(enabledRect, enabledProp, new GUIContent("Enabled"));
        y += lineHeight + spacing;
        
        // Source Type
        var sourceTypeRect = new Rect(position.x, y, position.width, lineHeight);
        EditorGUI.PropertyField(sourceTypeRect, sourceTypeProp, new GUIContent("Source Type"));
        y += lineHeight + spacing;
        
        // Source-specific fields
        switch (sourceType)
        {
            case Controls.InputSourceType.KeyCode:
                var keyRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(keyRect, keyProp, new GUIContent("Key"));
                y += lineHeight + spacing;
                break;
                
            case Controls.InputSourceType.LegacyAxis:
                var axisRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(axisRect, axisNameProp, new GUIContent("Axis Name"));
                y += lineHeight + spacing;
                break;
                
            case Controls.InputSourceType.InputActionReference:
#if ENABLE_INPUT_SYSTEM
                var actionRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(actionRect, actionRefProp, new GUIContent("Action Reference"));
                y += lineHeight + spacing;
#else 
                EditorGUILayout.HelpBox(
                    "InputActionReference is only available when the new Input System package is enabled.",
                    MessageType.Warning
                );
#endif
                break;
        }
        
        // Control
        var controlRect = new Rect(position.x, y, position.width, lineHeight);
        EditorGUI.PropertyField(controlRect, controlProp, new GUIContent("Control"));
        y += lineHeight + spacing;
        
        // Controller
        var controllerRect = new Rect(position.x, y, position.width, lineHeight);
        EditorGUI.PropertyField(controllerRect, controllerProp, new GUIContent("Controller"));
        y += lineHeight + spacing;
        
        var isAnalogRect = new Rect(position.x, y, position.width, lineHeight);
        EditorGUI.PropertyField(isAnalogRect, isAnalogProp, new GUIContent("Is Analog"));
        y += lineHeight + spacing;
        
        // Analog fields (only show if IsAnalog is true)
        if (isAnalogProp.boolValue)
        {
            var minValueRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(minValueRect, minValueProp, new GUIContent("Min Value"));
            y += lineHeight + spacing;
            
            var maxValueRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(maxValueRect, maxValueProp, new GUIContent("Max Value"));
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
        
        var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.sourceType));
        var isAnalogProp = property.FindPropertyRelative(nameof(Controls.KeyCode2Control.IsAnalog));
        
        var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        
        int lineCount = 1; // Header
        lineCount += 2; // Enabled + SourceType
        if (sourceType == Controls.InputSourceType.KeyCode) lineCount++;
        if (sourceType == Controls.InputSourceType.LegacyAxis) lineCount++;
        if (sourceType == Controls.InputSourceType.InputActionReference) lineCount++;

        lineCount += 3; // Control + Controller + IsAnalog
        if (isAnalogProp.boolValue) lineCount += 2; // MinValue + MaxValue
        
        return lineCount * lineHeight + lineCount * spacing;
    }
}

}