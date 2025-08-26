using UnityEngine;
using UnityEditor;

namespace UnityHawk.Editor {

[InitializeOnLoad]
public static class EmulatorDragDropHandler {
    static EmulatorDragDropHandler() {
        // Register for drag and drop events in the hierarchy
        DragAndDrop.AddDropHandler(HierarchyDropHandler);
    }
    
    static DragAndDropVisualMode HierarchyDropHandler(int dropTargetInstanceID, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform) {
        // Debug.Log($"HierarchyDropHandler: {dropTargetInstanceID}, {dropMode}, {parentForDraggedObjects}, {perform}");
        // Only handle exactly one dragged object
        if (DragAndDrop.objectReferences.Length != 1) return DragAndDropVisualMode.None;
        
        if ((dropMode & HierarchyDropFlags.DropUpon) == 0) {
            // Only handle dragging directly onto an object
            return DragAndDropVisualMode.None;
        }

        // Check if we're dragging Rom or Savestate assets
        bool hasValidAsset = false;
        Rom romAsset = null;
        Savestate savestateAsset = null;
        
        foreach (Object draggedObject in DragAndDrop.objectReferences) {
            // Debug.Log($"draggedObject: {draggedObject}");
            if (draggedObject is Rom rom) {
                hasValidAsset = true;
                romAsset = rom;
                break;
            } else if (draggedObject is Savestate savestate) {
                hasValidAsset = true;
                savestateAsset = savestate;
                break;
            }
        }
        
        if (!hasValidAsset) return DragAndDropVisualMode.None;
        
        // Get the GameObject at this hierarchy item
        GameObject gameObject = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
        if (gameObject == null) return DragAndDropVisualMode.None;
        
        // Check if this GameObject has an Emulator component
        Emulator emulator = gameObject.GetComponent<Emulator>();
        // Debug.Log($"emulator: {emulator}");
        if (emulator == null) return DragAndDropVisualMode.None;
        
        // Show that this is a valid drop target
        if (!perform) {
            return DragAndDropVisualMode.Copy;
        }
        
        // Perform the drop
        if (romAsset != null) {
            emulator.romFile = romAsset;
            
            // Debug.Log($"Rom asset '{romAsset.name}' assigned to Emulator on '{gameObject.name}'");
        } else if (savestateAsset != null) {
            // Assign the Savestate to the Emulator
            emulator.saveStateFile = savestateAsset;
            
            // Debug.Log($"Savestate asset '{savestateAsset.name}' assigned to Emulator on '{gameObject.name}'");
        }

        // Mark the scene as dirty so Unity saves the change
        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(emulator);

        emulator.OnValidate(); // A bit hacky but this doesn't get called automatically
        
        return DragAndDropVisualMode.Copy;
    }
}

}
