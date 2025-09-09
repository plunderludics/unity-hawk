using UnityEngine;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine.Search;
using System.Linq;
using System.Collections.Generic;

namespace UnityHawk.Editor
{
[CustomPropertyDrawer(typeof(Savestate))]
public class SavestateDrawer : PropertyDrawer {
    // There is not necessarily one SavestateDrawer instance for each Savestate field so keep things in a dictionary in case
    // (I think one instance gets created per Emulator component maybe? Not sure)
    // This field could be static but having it per-instance is a hacky way of ensuring the cache gets invalidated more frequently
    // TODO: this caching logic should go in a different class,
    // and also cache should refresh when savestate assets are created or deleted
    private Dictionary<Rom, List<Savestate>> _savestatesForRom = new ();

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
        if (!_savestatesForRom.TryGetValue(rom, out var savestates)) {
            // Debug.Log($"No cached savestates for rom {rom.name}, searching for savestates");
            savestates = AssetDatabase.FindAssets("t:savestate")
                    .Select(guid => AssetDatabase.LoadAssetAtPath<Savestate>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(savestate => savestate.MatchesRom(rom))
                    .ToList();
            _savestatesForRom[rom] = savestates;
        } else {
            // Debug.Log($"Using cached {savestates.Count} savestates for rom {rom.name}");
        }

        var dropdownSavestates = new List<Savestate>(savestates); // Clone savestates to add null option at the beginning
        dropdownSavestates.Insert(0, null);

        var savestateNames = dropdownSavestates.Select(savestate => savestate != null ? savestate.name : "None");
        int currentIndex = dropdownSavestates.IndexOf(property.objectReferenceValue as Savestate);

        // Create a dropdown menu with the savestates
        EditorGUI.BeginChangeCheck();
        var popupRect = new Rect(position.x + position.width - buttonWidth, position.y, buttonWidth, EditorGUIUtility.singleLineHeight);
        int selectedIndex = EditorGUI.Popup(popupRect, currentIndex, savestateNames.ToArray());
        if (EditorGUI.EndChangeCheck()) {
            // Set the property to the selected savestate
            var selectedSavestate = dropdownSavestates.ElementAt(selectedIndex);
            property.objectReferenceValue = selectedSavestate;
            property.serializedObject.ApplyModifiedProperties();

            // Kinda hacky, but invalidate the cache for this rom whenever selecting an option
            // - at least this way we can easily refresh the cache when needed
            _savestatesForRom.Remove(rom);
        }
        EditorGUI.EndProperty();
    }
}
}