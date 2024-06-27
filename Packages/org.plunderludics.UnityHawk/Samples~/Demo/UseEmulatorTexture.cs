using UnityEngine;
using UnityHawk;

namespace UnityHawk.Samples {

public class UseEmulatorTexture : MonoBehaviour
{
    [SerializeField] Emulator emulator;

    bool _initialized = false;

    void Update()
    {
        if (!_initialized && emulator && emulator.Texture != null) {
            Renderer r = GetComponent<Renderer>();
            r.material.mainTexture = emulator.Texture;
            r.material.SetTexture("_EmissionMap", emulator.Texture);
        }
    }
}

}