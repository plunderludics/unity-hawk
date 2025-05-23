using UnityEngine;
using NaughtyAttributes;

namespace UnityHawk {

[ExecuteAlways]
public class ApiControls : MonoBehaviour
{
    [Header("params")]
    [SerializeField] Rom Rom;

    [SerializeField] Savestate Savestate;

    [SerializeField] string SavePath;

    [OnValueChanged(nameof(OnVolumeChanged))]
    [Range(0, 100)]
    [SerializeField] int Volume;

    [Header("refs")]
    [SerializeField] Emulator emulator;

    void OnVolumeChanged() {
        emulator.Volume = Volume;
    }

    void OnValidate() {
        if (emulator) {
            return;
        }

        emulator = GetComponent<Emulator>();

        if (!emulator) {
            Debug.LogWarning("ApiControls: no emulator target set");
        }
    }

#if UNITY_EDITOR
    [Button]
    void LoadRom() {
        emulator.LoadRom(Rom);
    }

    [Button]
    void LoadState() {
        emulator.LoadState(Savestate);
    }

    [Button]
    void SaveState() {
        emulator.SaveState(SavePath);
    }

    [Button]
    void Pause() {
        emulator.Pause();
    }

    [Button]
    void Unpause() {
        emulator.Unpause();
    }

    [Button]
    void FrameAdvance() {
        emulator.FrameAdvance();
    }
#endif
}

}