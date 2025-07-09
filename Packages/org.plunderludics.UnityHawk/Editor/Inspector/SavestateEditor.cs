using UnityEditor;
using UnityEngine;

namespace UnityHawk.Editor {
[CustomEditor(typeof(Savestate))]
public class SavestateEditor : UnityEditor.Editor
{
    public override bool HasPreviewGUI() => target is Savestate { Screenshot: not null };

    public override void OnPreviewGUI(Rect r, GUIStyle background) {
        Savestate asset = (Savestate)target;
        if (asset.Screenshot is not null) {
            GUI.DrawTexture(r, asset.Screenshot, ScaleMode.ScaleToFit);
        }
    }

    public override GUIContent GetPreviewTitle() {
        return new GUIContent(target.name);
    }
}
}