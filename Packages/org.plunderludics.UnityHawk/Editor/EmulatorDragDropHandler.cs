using UnityEngine;
using UnityEditor;
#if UNITY_6000_3_OR_NEWER
using UnityEntityId = UnityEngine.EntityId;
#else
using UnityEntityId = System.Int32;
#endif

namespace UnityHawk.Editor {

[InitializeOnLoad]
internal static class EmulatorDragDropHandler {
    static EmulatorDragDropHandler() {
#if UNITY_6000_3_OR_NEWER
        DragAndDrop.AddDropHandlerV2(HierarchyDropHandler);
#else
        DragAndDrop.AddDropHandler(HierarchyDropHandler);
#endif
    }

    static DragAndDropVisualMode HierarchyDropHandler(
        UnityEntityId dropTargetEntityId,
        HierarchyDropFlags dropMode,
        Transform parentForDraggedObjects,
        bool perform) {
        if (DragAndDrop.objectReferences.Length != 1) return DragAndDropVisualMode.None;

        if ((dropMode & HierarchyDropFlags.DropUpon) == 0)
            return DragAndDropVisualMode.None;

        Rom romAsset = null;
        Savestate savestateAsset = null;

        foreach (Object draggedObject in DragAndDrop.objectReferences) {
            if (draggedObject is Rom rom) {
                romAsset = rom;
                break;
            }
            if (draggedObject is Savestate savestate) {
                savestateAsset = savestate;
                break;
            }
        }

        if (romAsset == null && savestateAsset == null) return DragAndDropVisualMode.None;

#if UNITY_6000_3_OR_NEWER
        GameObject gameObject = EditorUtility.EntityIdToObject(dropTargetEntityId) as GameObject;
#else
        GameObject gameObject = EditorUtility.InstanceIDToObject(dropTargetEntityId) as GameObject;
#endif
        if (gameObject == null) return DragAndDropVisualMode.None;

        Emulator emulator = gameObject.GetComponent<Emulator>();
        if (emulator == null) return DragAndDropVisualMode.None;

        if (!perform) return DragAndDropVisualMode.Copy;

        if (romAsset != null) {
            emulator.romFile = romAsset;
            if (emulator.autoSelectRomFile && !emulator.saveStateFile.MatchesRom(romAsset))
                emulator.saveStateFile = null;
        } else {
            emulator.saveStateFile = savestateAsset;
        }

        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(emulator);
        emulator.OnValidate();

        return DragAndDropVisualMode.Copy;
    }
}

}
