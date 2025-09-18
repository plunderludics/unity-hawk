using UnityEngine;
using UnityEditor;

namespace UnityHawk.Editor {
internal abstract class BaseMappingDrawer : PropertyDrawer {
    protected const float LINE_HEIGHT = 18f;
    protected const float SPACING = 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        var enabledProp = property.FindPropertyRelative("Enabled");
        string customLabel = FoldoutLabel(property);
        
        // Dim the color if not enabled
        Color c = GUI.color;
        GUI.color = enabledProp.boolValue ? Color.white : new Color(0.7f, 0.7f, 0.7f);

        // Draw foldout header
        DrawFoldoutHeader(position, enabledProp, property, customLabel);
        
        if (property.isExpanded) {
            float y = position.y + LINE_HEIGHT + SPACING;
            DrawFields(position, property, ref y);
        }
        
        GUI.color = c; // Reset color
    }

    protected abstract void DrawFields(Rect position, SerializedProperty property, ref float y);
    protected abstract string FoldoutLabel(SerializedProperty property);

    protected void DrawFoldoutHeader(Rect position, SerializedProperty enabledProp, SerializedProperty property, string customLabel) {            
        float checkboxWidth = 16f;
        float checkboxSpacing = 16f;
        float foldoutWidth = position.width - checkboxWidth - checkboxSpacing;
        
        // Draw the enabled checkbox
        var checkboxRect = new Rect(position.x, position.y, checkboxWidth, LINE_HEIGHT);
        enabledProp.boolValue = EditorGUI.Toggle(checkboxRect, enabledProp.boolValue);
        
        // Draw the foldout
        var foldoutRect = new Rect(position.x + checkboxWidth + checkboxSpacing, position.y, foldoutWidth, LINE_HEIGHT);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, customLabel, true);
    }

    protected void DrawEnabledField(Rect position, SerializedProperty enabledProp, ref float y) {
        var enabledRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(enabledRect, enabledProp, new GUIContent("Enabled"));
        y += LINE_HEIGHT + SPACING;
    }

    protected void DrawSourceTypeField(Rect position, SerializedProperty sourceTypeProp, ref float y) {
        var sourceTypeRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(sourceTypeRect, sourceTypeProp, new GUIContent("Source Type"));
        y += LINE_HEIGHT + SPACING;
    }



    protected void DrawKeyCodeField(Rect position, SerializedProperty property, ref float y) {
        var keyProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.Key));
        var keyRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(keyRect, keyProp, new GUIContent("Key"));
        y += LINE_HEIGHT + SPACING;
    }

    protected void DrawLegacyAxisNameField(Rect position, SerializedProperty property, ref float y) {
        var axisNameProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.AxisName));
        var axisRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(axisRect, axisNameProp, new GUIContent("Axis Name"));
        y += LINE_HEIGHT + SPACING;
        
#if !ENABLE_LEGACY_INPUT_MANAGER
        var warningRect = new Rect(position.x, y, position.width, LINE_HEIGHT * 2);
        EditorGUI.HelpBox(warningRect, "Legacy Axis requires the legacy Input Manager is not enabled.", MessageType.Warning);
        y += LINE_HEIGHT * 2 + SPACING;
#endif
    }

    protected void DrawInputActionReferenceField(Rect position, SerializedProperty property, ref float y) {
#if ENABLE_INPUT_SYSTEM
        var actionRefProp = property.FindPropertyRelative("ActionRef");
        var actionRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(actionRect, actionRefProp, new GUIContent("Action Reference"));
        y += LINE_HEIGHT + SPACING;
#else
        var warningRect = new Rect(position.x, y, position.width, LINE_HEIGHT * 2);
        EditorGUI.HelpBox(warningRect, "Input Action Reference requires the new Input System to be enabled.", MessageType.Warning);
        y += LINE_HEIGHT * 2 + SPACING;
#endif
    }

    protected void DrawControlField(Rect position, SerializedProperty property, string fieldName, string label, ref float y) {
        var controlProp = property.FindPropertyRelative(fieldName);
        var controlRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(controlRect, controlProp, new GUIContent(label));
        y += LINE_HEIGHT + SPACING;
    }

    protected void DrawControllerField(Rect position, SerializedProperty property, ref float y) {
        var controllerProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.Controller));
        var controllerRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
        EditorGUI.PropertyField(controllerRect, controllerProp, new GUIContent("Controller"));
        y += LINE_HEIGHT + SPACING;
    }

    protected string FormatMappingLabel(string source, SerializedProperty controlProp, SerializedProperty controllerProp) {
        string target = BizhawkTargetLabel(controlProp, controllerProp);
        return $"{source} → {target}";
    }

    protected string BizhawkTargetLabel(SerializedProperty controlProp, SerializedProperty controllerProp) {
        string controllerPrefix = controllerProp.enumValueIndex > 0 ? $"P{controllerProp.enumValueIndex} " : "";
        return $"{controllerPrefix}{controlProp.stringValue}";
    }
}
}
