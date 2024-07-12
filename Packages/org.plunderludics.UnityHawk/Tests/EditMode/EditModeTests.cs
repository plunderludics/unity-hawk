using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityHawk;
using UnityEditor;
using System;

namespace UnityHawk.Tests {

// (A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
// `yield return null;` to skip a frame.)

public class EditModeTests: SharedTests
{
    [UnityTest]
    public IEnumerator TestNotRunningInEditMode()
    {
        Emulator e = AddEliteEmulatorForTesting();
        e.runInEditMode = false;

        yield return WaitForAWhile(e);
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Inactive));
        Assert.That(e.IsRunning, Is.False);
    }
}

}