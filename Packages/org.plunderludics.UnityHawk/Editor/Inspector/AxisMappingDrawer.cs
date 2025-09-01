using UnityEngine;
using UnityEditor;

namespace UnityHawk.Editor {
    [CustomPropertyDrawer(typeof(Controls.AxisMapping))]
    public class AxisMappingDrawer : BaseMappingDrawer {
        protected override void DrawFields(Rect position, SerializedProperty property, ref float y) {
            var enabledProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.Enabled));
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.sourceType));
            var minValueProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.MinValue));
            var maxValueProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.MaxValue));
            
            DrawEnabledField(position, enabledProp, ref y);
            DrawSourceTypeField(position, sourceTypeProp, ref y);
            DrawAxisInputSourceFields(position, property, ref y);
            DrawControlField(position, property, nameof(Controls.AxisMapping.EmulatorAxisName), "Emulator Axis Name", ref y);
            DrawControllerField(position, property, ref y);
            DrawAxisValueFields(position, minValueProp, maxValueProp, ref y);
        }

        protected override string FoldoutLabel(SerializedProperty property) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.sourceType));
            var negativeKeyProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.NegativeKey));
            var positiveKeyProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.PositiveKey));
            var axisNameProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.AxisName));
#if ENABLE_INPUT_SYSTEM
            var actionRefProp = property.FindPropertyRelative("ActionRef");
#else
            SerializedProperty actionRefProp = null;
#endif
            var controlProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.EmulatorAxisName));
            var controllerProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.Controller));

            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
            string source = sourceType switch {
                Controls.InputSourceType.KeyCode => $"{negativeKeyProp.enumDisplayNames[negativeKeyProp.enumValueIndex]}/{positiveKeyProp.enumDisplayNames[positiveKeyProp.enumValueIndex]}",
                Controls.InputSourceType.LegacyAxis => axisNameProp.stringValue,
                Controls.InputSourceType.InputActionReference => actionRefProp?.objectReferenceValue?.name ?? "None",
                _ => "Unknown"
            };

            return FormatMappingLabel(source, controlProp, controllerProp);
        }

        private void DrawAxisInputSourceFields(Rect position, SerializedProperty property, ref float y) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.sourceType));
            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;

            switch (sourceType) {
                case Controls.InputSourceType.KeyCode:
                    DrawKeyCodePairFields(position, property, ref y);
                    break;
                case Controls.InputSourceType.LegacyAxis:
                    DrawLegacyAxisNameField(position, property, ref y);
                    break;
                case Controls.InputSourceType.InputActionReference:
                    DrawInputActionReferenceField(position, property, ref y);
                    break;
            }
        }

        private void DrawKeyCodePairFields(Rect position, SerializedProperty property, ref float y) {
            var negativeKeyProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.NegativeKey));
            var positiveKeyProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.PositiveKey));
            
            var negativeRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(negativeRect, negativeKeyProp, new GUIContent("Negative Key"));
            y += LINE_HEIGHT + SPACING;
            
            var positiveRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(positiveRect, positiveKeyProp, new GUIContent("Positive Key"));
            y += LINE_HEIGHT + SPACING;
        }

        private void DrawAxisValueFields(Rect position, SerializedProperty minValueProp, SerializedProperty maxValueProp, ref float y) {
            var minValueRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(minValueRect, minValueProp, new GUIContent("Min Value"));
            y += LINE_HEIGHT + SPACING;
            
            var maxValueRect = new Rect(position.x, y, position.width, LINE_HEIGHT);
            EditorGUI.PropertyField(maxValueRect, maxValueProp, new GUIContent("Max Value"));
            y += LINE_HEIGHT + SPACING;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            if (!property.isExpanded) return LINE_HEIGHT;
            
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.AxisMapping.sourceType));
            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
            
            float height = LINE_HEIGHT; // Foldout header
            height += (LINE_HEIGHT + SPACING) * 6; // Enabled, SourceType, Control, Controller, MinValue, MaxValue, NeutralValue
            
            // Add height for input source fields
            if (sourceType == Controls.InputSourceType.KeyCode) {
                height += (LINE_HEIGHT + SPACING) * 2; // NegativeKey, PositiveKey
            } else {
                height += LINE_HEIGHT + SPACING; // Single field
            }
            
            // Add height for warnings if needed
#if !ENABLE_INPUT_SYSTEM
            if (sourceType == Controls.InputSourceType.InputActionReference) {
                height += LINE_HEIGHT + SPACING;
            }
#endif
#if !ENABLE_LEGACY_INPUT_MANAGER
            if (sourceType == Controls.InputSourceType.LegacyAxis) {
                height += LINE_HEIGHT * 2 + SPACING;
            }
#endif
            
            return height;
        }
    }
}
