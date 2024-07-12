using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

using UnityHawk;

// Pretty janky editor interface to the bizhawk api, could be improved
public class ApiControls : MonoBehaviour
{
    public Emulator emulator;
    public Rom rom;
    public Savestate savestate;
    public string pathToSave;


    void OnEnable() {
        if (emulator == null) {
            emulator = GetComponent<Emulator>();
            if (emulator == null) {
                Debug.LogWarning("ApiControls: no emulator target set");
            }
        }
    }

#if UNITY_EDITOR
    [Button]
    private void LoadRom() {
        emulator.LoadRom(rom);
    }
    [Button]
    private void LoadState() {
        emulator.LoadState(savestate);
    }
    [Button]
    private void SaveState() {
        emulator.SaveState(pathToSave);
    }
    [Button]
    private void Pause() {
        emulator.Pause();
    }
    [Button]
    private void Unpause() {
        emulator.Unpause();
    }
    [Button]
    private void FrameAdvance() {
        emulator.FrameAdvance();
    }
#endif
}
