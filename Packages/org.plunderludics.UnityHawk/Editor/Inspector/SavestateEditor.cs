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

    // This sets the icon for the asset in the Project window
    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height) {
        Savestate savestate = (Savestate)target;

        if (savestate == null || savestate.Screenshot == null) return null;

        // (supported formats: ARGB32, RGBA32, RGB24, Alpha8 or one of float formats)
        Texture2D tex = new Texture2D (width, height);
        EditorUtility.CopySerialized (savestate.Screenshot, tex);

        return tex;
    }
}
}