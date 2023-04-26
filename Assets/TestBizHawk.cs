using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;

public class TestBizHawk : MonoBehaviour
{
    IEmulator emulator;
    IController controller;
    //RomLoader loader;

    void Start()
    {
		emulator = new NullEmulator();
        controller = new NullController();
    }

    void Update()
    {
        emulator.FrameAdvance(controller, true, true);
        Debug.Log(emulator.Frame);
    }
}
