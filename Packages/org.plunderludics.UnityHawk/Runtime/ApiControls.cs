using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

using UnityHawk;

// Pretty janky editor interface to the bizhawk api, could be improved
public class ApiControls : MonoBehaviour
{
    public Emulator emulator;
    public string path;

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
        emulator.LoadRom(path);
    }
    [Button]
    private void LoadState() {
        emulator.LoadState(path);
    }
    [Button]
    private void SaveState() {
        emulator.SaveState(path);
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
