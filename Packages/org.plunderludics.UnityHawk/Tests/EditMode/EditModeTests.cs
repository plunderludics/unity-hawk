using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityHawk;
using UnityEditor;
using System;

namespace UnityHawk.Tests {

// For any extra tests we need to run in edit mode but not play mode
public class EditModeTests: SharedTests
{
    public EditModeTests(bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui)
        : base(passInputFromUnity, captureEmulatorAudio, showBizhawkGui)
    {
    }

    [UnityTest]
    public IEnumerator TestNotRunningInEditMode()
    {
        e.runInEditMode = false;

        yield return WaitForAWhile(e);
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Inactive));
        Assert.That(e.IsRunning, Is.False);
    }
}

}