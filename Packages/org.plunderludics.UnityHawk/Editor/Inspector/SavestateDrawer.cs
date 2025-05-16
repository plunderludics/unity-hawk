using UnityEngine;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine.Search;

namespace UnityHawk.Editor
{
[CustomPropertyDrawer(typeof(Savestate))]
public class SavestateDrawer : PropertyDrawer
{
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Draw the default property field
            // EditorGUI.PropertyField(position, property, label);

            EditorGUI.BeginProperty(position, label, property);

            // Sort of a hack - get the Rom romFile property attached to parent object if there is one 
            // (so that we can filter the savestates by rom)
            var romProperty = property.serializedObject.FindProperty("romFile");
            Rom rom = romProperty?.objectReferenceValue as Rom;
            if (rom == null)
            {
                EditorGUI.LabelField(position, "Select rom first");
            }
            else
            {
                float buttonWidth = 20f;
                // First draw greyed out object picker
                var savestateRect = new Rect(position.x, position.y, position.width - buttonWidth, EditorGUIUtility.singleLineHeight);
                GUI.enabled = false;
                EditorGUI.ObjectField(savestateRect, property, label);
                GUI.enabled = true;

                // Then draw the button
                var buttonRect = new Rect(position.x + position.width - buttonWidth, position.y, buttonWidth, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(buttonRect, "⊙"))
                {
                    // Open search picker
                    var context = SearchService.CreateContext("t:savestate");

                    // This sucks, there's no way to pass SearchViewFlags.HideSearchBar with this method,
                    // and the alternative ShowPicker(ViewState) doesn't take a filterHandler argument
                    // Best thing is probably to implement a custom SearchProvider that does the filtering but what a pain..
                    // This works ok for now anyway, just annoying that the search bar is visible

                    SearchService.ShowPicker(context,
                        selectHandler: (SearchItem item, bool canceled) =>
                        {
                            if (canceled) return;
                            // Set the property to the selected savestate
                            property.objectReferenceValue = item?.ToObject() as Savestate;
                            property.serializedObject.ApplyModifiedProperties();
                        },
                        filterHandler: (SearchItem item) =>
                        {
                            // Attempt to get the Savestate object from the item's id or a custom property
                            var savestate = item?.ToObject() as Savestate;
                            // Check if the savestate belongs to the same rom
                            // TODO: should check hash here instead
                            return savestate?.RomInfo.Name == rom.name;
                        }
                    );
                }
            }

            EditorGUI.EndProperty();
        }
}
}