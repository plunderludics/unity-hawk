using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityHawk;

public class UseEmulatorTexture : MonoBehaviour
{
    public Emulator emulator;
    bool _initialized = false;
    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        if (!_initialized && emulator && emulator.Texture != null) {
            Renderer r = GetComponent<Renderer>();
            r.material.mainTexture = emulator.Texture;
            r.material.SetTexture("_EmissionMap", emulator.Texture);
        }
    }
}
