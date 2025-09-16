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
        var bip = e.inputProvider as BasicInputProvider;
        Assert.IsNotNull(bip);
        Assert.That(bip.useDefaultControls, Is.True);
        Assert.IsNotNull(bip.controlsObject);
        Assert.That(bip.controlsObject.name, Is.EqualTo("NES"));
        Assert.IsNotNull(bip.controlsObject.Controls);
        // First mapping should be Up Arrow -> P1 Up
        Assert.That(bip.controlsObject.Controls.ButtonMappings.Count, Is.GreaterThanOrEqualTo(1));
        var buttonMapping1 = bip.controlsObject.Controls.ButtonMappings[0];
        Assert.That(buttonMapping1.Enabled, Is.True);
        Assert.That(buttonMapping1.sourceType, Is.EqualTo(Controls.InputSourceType.KeyCode));
        Assert.That(buttonMapping1.Key, Is.EqualTo(KeyCode.UpArrow));
        Assert.That(buttonMapping1.EmulatorButtonName, Is.EqualTo("Up"));
        Assert.That(buttonMapping1.Controller, Is.EqualTo(Controller.P1));
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