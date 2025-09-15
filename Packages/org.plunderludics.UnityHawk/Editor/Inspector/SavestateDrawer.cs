using UnityEngine;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine.Search;
using System.Linq;

namespace UnityHawk.Editor
{
[CustomPropertyDrawer(typeof(Savestate))]
public class SavestateDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        // Draw the default property field
        // EditorGUI.PropertyField(position, property, label);

        EditorGUI.BeginProperty(position, label, property);

        // Sort of a hack - get the Rom romFile property attached to parent object if there is one 
        // (so that we can filter the savestates by rom)
        var romProperty = property.serializedObject.FindProperty("romFile");
        Rom rom = romProperty?.objectReferenceValue as Rom;
        float buttonWidth = 20f;
        // First draw object picker
        // (Could be grey/disabled - but this way you can pick a non-matching savestate if you need to)
        var savestateRect = new Rect(position.x, position.y, position.width - buttonWidth, EditorGUIUtility.singleLineHeight);
        // GUI.enabled = false;
        EditorGUI.ObjectField(savestateRect, property, label);
        // GUI.enabled = true;

        // Dropdown UI:
        // First find all savestates for the current rom
        // (Shouldn't really do this every frame, but it doesn't seem to be too bad)
        // (Some roms don't get hashed, so fallback to rom filename)
        // (Some older savestates don't have rominfo at all, those will get hidden)
        var savestates = AssetDatabase.FindAssets("t:savestate")
            .Select(guid => AssetDatabase.LoadAssetAtPath<Savestate>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(savestate => savestate.MatchesRom(rom));
        
        savestates = savestates.Prepend(null); // Add a null option to the list
        var savestateNames = savestates.Select(savestate => savestate != null ? savestate.name : "None");
        int currentIndex = savestates.ToList().IndexOf(property.objectReferenceValue as Savestate);

        // Create a dropdown menu with the savestates
        EditorGUI.BeginChangeCheck();
        var popupRect = new Rect(position.x + position.width - buttonWidth, position.y, buttonWidth, EditorGUIUtility.singleLineHeight);
        int selectedIndex = EditorGUI.Popup(popupRect, currentIndex, savestateNames.ToArray());
        if (EditorGUI.EndChangeCheck()) {
            // Set the property to the selected savestate
            var selectedSavestate = savestates.ElementAt(selectedIndex);
            property.objectReferenceValue = selectedSavestate;
            property.serializedObject.ApplyModifiedProperties();
        }

        // ShowPicker UI:
        // // Then draw the button
        // var buttonRect = new Rect(position.x + position.width - buttonWidth, position.y, buttonWidth, EditorGUIUtility.singleLineHeight);
        // if (GUI.Button(buttonRect, "⊙"))
        // {
        //     // Open search picker
        //     var context = SearchService.CreateContext("t:savestate");

        //     // This sucks, there's no way to pass SearchViewFlags.HideSearchBar with this method,
        //     // and the alternative ShowPicker(ViewState) doesn't take a filterHandler argument
        //     // Best thing is probably to implement a custom SearchProvider that does the filtering but what a pain..
        //     // This works ok for now anyway, just annoying that the search bar is visible

        //     SearchService.ShowPicker(context,
        //         selectHandler: (SearchItem item, bool canceled) =>
        //         {
        //             if (canceled) return;
        //             // Set the property to the selected savestate
        //             property.objectReferenceValue = item?.ToObject() as Savestate;
        //             property.serializedObject.ApplyModifiedProperties();
        //         },
        //         filterHandler: (SearchItem item) =>
        //         {
        //             // Attempt to get the Savestate object from the item's id or a custom property
        //             var savestate = item?.ToObject() as Savestate;
        //             // Check if the savestate belongs to the same rom
        //             // TODO: should check hash here instead
        //             return savestate?.RomInfo.Name == rom.name;
        //         }
        //     );
        // }

        EditorGUI.EndProperty();
    }
}
}