using System;
using System.IO;
using System.Threading.Tasks;
using BizHawk.Emulation.Common;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Events;

namespace UnityHawk.Editor {

[CustomEditor(typeof(SavestateImporter))]
public class SavestateImporterEditor : ScriptedImporterEditor {
    const string k_ErrorMessage =
        "the savestate does not contain the info for the game, add the rom below and press 'validate' to update savestate";
    Rom _rom;

    public override async void OnInspectorGUI()
    {
        // if the imported savestate has no gameinfo
        var savestate = assetTarget as Savestate;

        if (!savestate || savestate.RomName == GameInfo.NullInstance.Name) {
            EditorGUILayout.HelpBox(k_ErrorMessage, MessageType.Error);
            _rom = EditorGUILayout.ObjectField("rom", _rom, typeof(Rom), _rom) as Rom;

            // TODO: the idea is that you should be able to open the rom
            // and resave the savestate with the required information
            // open bizhawk with the savestate and just immediately resave
            // not working yet
            // EditorGUI.BeginDisabledGroup(!_rom);
            // could also just be able to manually input the info here for old savestates
            EditorGUI.BeginDisabledGroup(true);
            if (GUILayout.Button("validate")) {
                var path = _rom ? Path.GetFullPath(AssetDatabase.GetAssetPath(_rom)) : "";
                var go = new GameObject();
                try {
                    var emulator = go.AddComponent<Emulator>();
                    emulator.romFile = _rom;
                    emulator.saveStateFile = savestate;

                    var saved = false;
                    emulator.OnRunning += () => {
                        Debug.Log("STARTED");
                        emulator.Pause();
                        // emulator.SaveState($"{path}");
                        emulator.SaveState($"Assets/validated-{_rom}");
                        emulator.OnRunning = null;
                        Debug.Log("SAVING STATE");
                        DestroyImmediate(go);
                        saved = true;
                    };

                    await Task.Delay(1000);
                    emulator.runInEditMode = true;
                    // emulator.showBizhawkGui = true;
                    while (!saved) {
                        await Task.Delay(10);
                    }
                }
                catch {
                    DestroyImmediate(go);
                    throw;
                }
            }
            EditorGUI.BeginDisabledGroup(!_rom);
        }

        base.ApplyRevertGUI();
    }

}
}