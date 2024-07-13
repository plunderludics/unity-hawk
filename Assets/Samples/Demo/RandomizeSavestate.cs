using System.Collections;
using UnityEngine;

namespace UnityHawk.Samples {

public class RandomizeSavestate : MonoBehaviour {
    public Emulator emu;
    public Savestate[] savestates;
    public float delay;

    void Start() {
        emu.OnRunning += () => {
            StartCoroutine(WaitAndLoad(delay));
        };
    }

    IEnumerator WaitAndLoad(float t) {
        while(true) {
            yield return new WaitForSeconds(t);
            var savestate = savestates[UnityEngine.Random.Range(0, savestates.Length)];
            emu.LoadState(savestate);
        }
    }

}

}