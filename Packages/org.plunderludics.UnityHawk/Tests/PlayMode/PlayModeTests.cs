// For any tests we want to run in play mode but not edit mode

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityHawk.Tests {
public class PlayModeTests : SharedTests {
    public PlayModeTests(bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui)
        : base(passInputFromUnity, captureEmulatorAudio, showBizhawkGui)
    {
    }


    // Check that default NES controls get loaded
    [UnityTest]
    public IEnumerator TestDefaultControls() {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        // No custom input provider set so by default should add a BasicInputProvider, and select the default NES controls after startup
        Debug.Log(e.inputProvider);
        Assert.That(e.inputProvider is BasicInputProvider);
        Assert.That((e.inputProvider as BasicInputProvider).controlsObject.name == "NES");
    }

    // Not very comprehensive but at least check that sending inputs via the InputProvider API does something
    [UnityTest]
    public IEnumerator TestAddInputEvent() {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        uint u = 0;

        // Memory address that stays constant (==72) on the title screen but changes after hitting Start
        e.WatchUnsigned(0x00EB, 1, true, domain: null, (value) => u = value);

        yield return WaitForAMoment(e);
        Assert.That(u, Is.EqualTo(72));

        e.inputProvider.AddInputEvent(new InputEvent("Start", 1, Controller.P1, false));

        yield return WaitForAWhile(e);
        Assert.That(u, Is.Not.EqualTo(72));
    }
}
}