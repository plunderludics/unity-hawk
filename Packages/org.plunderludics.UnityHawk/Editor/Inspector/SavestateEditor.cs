using UnityEditor;
using UnityEngine;

namespace UnityHawk.Editor {
[CustomEditor(typeof(Savestate))]
public class SavestateEditor : UnityEditor.Editor {
    public override bool HasPreviewGUI() => target is Savestate { Screenshot: not null };

    public override void OnPreviewGUI(Rect r, GUIStyle background) {
        Savestate savestate = (Savestate)target;
        if (savestate.Screenshot is not null) {
            GUI.DrawTexture(r, savestate.Screenshot, ScaleMode.ScaleToFit);
        }
    }

    public override GUIContent GetPreviewTitle() {
        return new GUIContent(target.name);
    }

    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height) {
        Savestate savestate = (Savestate)target;

        if (savestate == null || savestate.Screenshot == null) return null;

        // (supported formats: ARGB32, RGBA32, RGB24, Alpha8 or one of float formats)
        Texture2D tex = new Texture2D (width, height);
        EditorUtility.CopySerialized (savestate.Screenshot, tex);

        return tex;
    }
}

// [InitializeOnLoad]
// public static class SavestateIconSetter {
//     static SavestateIconSetter() {
//         EditorApplication.delayCall += SetIcons;
//     }

//     [MenuItem("Examples/Set Custom Icon on GameObject")]
//     static void SetIcons() {
//         var assets = AssetDatabase.FindAssets("t:Savestate");
//         foreach (var guid in assets) {
//             var path = AssetDatabase.GUIDToAssetPath(guid);
//             Savestate savestate = AssetDatabase.LoadAssetAtPath<Savestate>(path);
//             Debug.Log($"Setting icon for Savestate: {savestate.name}");

//             if (savestate != null && savestate.Screenshot != null) {
//                 EditorGUIUtility.SetIconForObject(savestate, savestate.Screenshot);
//             }
//         }
//     }
// }
}