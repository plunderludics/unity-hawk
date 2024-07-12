using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

namespace UnityHawk.Tests {
    
public class PlayModeTests
{
    [UnityTest]
    public IEnumerator BigTest1()
    {
        Emulator e = Shared.AddEliteEmulatorForTesting();
        Debug.Log(e.romFile);

        yield return Shared.WaitForAWhile();
        
        Debug.Log(e.Status);
        Assert.That(e.IsRunning, Is.True);
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Started));
        
        yield return null;
    }
}

}
