using UnityEngine;
using UnityEditor;

namespace UnityHawk.Editor {
    [CustomPropertyDrawer(typeof(Controls.ButtonMapping))]
    public class ButtonMappingDrawer : BaseMappingDrawer {
        protected override void DrawFields(Rect position, SerializedProperty property, ref float y) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.sourceType));
            
            DrawSourceTypeField(position, sourceTypeProp, ref y);
            DrawInputSourceFields(position, property, ref y);
            DrawControlField(position, property, nameof(Controls.ButtonMapping.EmulatorButtonName), "Emulator Button Name", ref y);
            DrawControllerField(position, property, ref y);
        }

        private void DrawInputSourceFields(Rect position, SerializedProperty property, ref float y) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.sourceType));
            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;

            switch (sourceType) {
                case Controls.InputSourceType.KeyCode:
                    DrawKeyCodeField(position, property, ref y);
                    break;
                case Controls.InputSourceType.LegacyAxis:
                    DrawLegacyAxisNameField(position, property, ref y);
                    break;
                case Controls.InputSourceType.InputActionReference:
                    DrawInputActionReferenceField(position, property, ref y);
                    break;
            }
        }

        protected override string FoldoutLabel(SerializedProperty property) {
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.sourceType));
            var keyProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.Key));
            var axisNameProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.AxisName));
#if ENABLE_INPUT_SYSTEM
            var actionRefProp = property.FindPropertyRelative("ActionRef");
#else
            SerializedProperty actionRefProp = null;
#endif
            var controlProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.EmulatorButtonName));
            var controllerProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.Controller));

            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
            string source = sourceType switch {
                Controls.InputSourceType.KeyCode => $"{keyProp.enumDisplayNames[keyProp.enumValueIndex]}",
                Controls.InputSourceType.LegacyAxis => axisNameProp.stringValue,
                Controls.InputSourceType.InputActionReference => actionRefProp?.objectReferenceValue?.name ?? "None",
                _ => "Unknown"
            };

            return FormatMappingLabel(source, controlProp, controllerProp);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            if (!property.isExpanded) return LINE_HEIGHT;
            
            var sourceTypeProp = property.FindPropertyRelative(nameof(Controls.ButtonMapping.sourceType));
            var sourceType = (Controls.InputSourceType)sourceTypeProp.enumValueIndex;
            
            float height = LINE_HEIGHT; // Foldout header
            height += (LINE_HEIGHT + SPACING) * 3; // SourceType, Control, Controller
            
            // Add height for input source field
            height += LINE_HEIGHT + SPACING;
            
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
