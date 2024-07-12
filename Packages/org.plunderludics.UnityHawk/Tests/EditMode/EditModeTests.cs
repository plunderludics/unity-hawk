using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityHawk;
using UnityEditor;
using System;

namespace UnityHawk.Tests {

public class EditModeTests
{
    // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator BigTest1()
    {
        Emulator e = Shared.AddEliteEmulatorForTesting();

        e.runInEditMode = false;
        yield return Shared.WaitForAWhile(action: () => e.Update());
        Assert.That(e.IsRunning, Is.False); // Emulator should not be running since runInEditMode is false

        e.runInEditMode = true;
        yield return Shared.WaitForAWhile(action: () => e.Update());
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Running));
        Assert.That(e.IsRunning, Is.True);
    }
}

}