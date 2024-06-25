using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityHawk;

public class Test : MonoBehaviour {
    Emulator _emulator;

    void Awake() {
        _emulator = GetComponent<Emulator>();
        _emulator.RegisterMethod("DoSomething", (s) => {
            Debug.Log(s);
            return "hello from unity";
        });

        _emulator.OnRunning += () => Debug.Log("Emulator started running with {_emulator}");
    }
}