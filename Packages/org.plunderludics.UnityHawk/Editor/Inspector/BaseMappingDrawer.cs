using UnityEngine;
using UnityEditor;

namespace UnityHawk.Editor {
    /// <summary>
    /// Base class for mapping property drawers with common functionality
    /// </summary>
    public abstract class BaseMappingDrawer : PropertyDrawer {
        protected const float LINE_HEIGHT = 18f;
        protected const float SPACING = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var enabledProp = property.FindPropertyRelative("Enabled");
            string customLabel = FoldoutLabel(property);
            
            // Dim the color if not enabled
            Color c = GUI.color;
            GUI.color = enabledProp.boolValue ? Color.white : Color.gray;
            
            // Draw foldout header
            DrawFoldoutHeader(position, property, customLabel);
            
            if (property.isExpanded) {
                float y = position.y + LINE_HEIGHT + SPACING;
                DrawFields(position, property, ref y);
            }
            
            GUI.color = c; // Reset color
        }

        protected abstract void DrawFields(Rect position, SerializedProperty property, ref float y);
        protected abstract string FoldoutLabel(SerializedProperty property);

        protected void DrawFoldoutHeader(Rect position, SerializedProperty property, string customLabel) {
            property.isExpanded = EditorGUI.Foldout(new Rect(position.x, position.y, position.width, LINE_HEIGHT), 
                property.isExpanded, customLabel, true);
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

        protected void DrawInputSourceFields(Rect position, SerializedProperty property, ref float y) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.sourceType));
            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;

            switch (sourceType) {
                case Controls.InputSourceType.KeyCode:
                    DrawKeyCodeField(position, property, ref y);
                    break;
                case Controls.InputSourceType.LegacyAxis:
                    DrawAxisNameField(position, property, ref y);
                    break;
                case Controls.InputSourceType.InputActionReference:
                    DrawInputActionReferenceField(position, property, ref y);
                    break;
            }
        }

        protected void DrawKeyCodeField(Rect position, SerializedProperty property, ref float y) {
            var keyProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.Key));
            var keyRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(keyRect, keyProp, new GUIContent("Key"));
            y += LINE_HEIGHT + SPACING;
        }

        protected void DrawAxisNameField(Rect position, SerializedProperty property, ref float y) {
            var axisNameProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.AxisName));
            var axisRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(axisRect, axisNameProp, new GUIContent("Axis Name"));
            y += LINE_HEIGHT + SPACING;
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
