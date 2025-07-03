// Pretty janky editor interface to the bizhawk api, could be improved
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace UnityHawk {
public class BasicApiTool : MonoBehaviour
{
    [Header("params")]
    [SerializeField] Rom Rom;
    [SerializeField] Savestate Savestate;
    [Tooltip("Used for save state path, volume or speed percentage")]
    [SerializeField] string arg;

    [Header("refs")]
    [SerializeField] Emulator emulator;

    void OnEnable() {
        if (emulator == null) {
            emulator = GetComponent<Emulator>();
            if (emulator == null) {
                Debug.LogWarning("BasicApiTool: no emulator target set");
            }
        }
    }

#if UNITY_EDITOR
    [Button]
    private void LoadRom() {
        emulator.LoadRom(Rom);
    }

    [Button]
    private void LoadState() {
        emulator.LoadState(Savestate);
    }

    [Button]
    private void SaveState() {
        emulator.SaveState(arg);
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
    
    [Button]
    private void SetVolume() {
        emulator.SetVolume(float.Parse(arg));
    }

    [Button]
    private void SetSpeed() {
        emulator.SetSpeedPercent(int.Parse(arg));
    }
#endif
}

}