using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using System;

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

namespace UnityHawk.Tests {
    
public class SharedTests
{
    private Rom eliteRom;
    private Savestate eliteSavestate;

    public SharedTests() {
        // This breaks when trying to test in standalone player because AssetDatabase is unavailable
        // Probably have to use Addressables instead
        eliteRom = AssetDatabase.LoadAssetAtPath<Rom>("Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes");
        eliteSavestate = AssetDatabase.LoadAssetAtPath<Savestate>("Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestateForTests.savestate");
        
        Assert.That(eliteRom, Is.Not.Null);
        Assert.That(eliteSavestate, Is.Not.Null);
    }
    [UnityTest]
    public IEnumerator TestEmulatorIsRunning()
    {
        Emulator e = AddEliteEmulatorForTesting();

        yield return WaitForAWhile(e);
        
        AssertEmulatorIsRunning(e);

        yield return null;
    }

    [UnityTest]
    public IEnumerator TestWithSavestate()
    {
        Emulator e = AddEliteEmulatorForTesting();
        e.saveStateFile = eliteSavestate;

        yield return WaitForAWhile(e);
        
        AssertEmulatorIsRunning(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(2000)); // Hacky way of checking if the savestate actually got loaded
        
        yield return null;
    }

    [UnityTest]
    public IEnumerator TestPauseAndUnpause()
    {
        Emulator e = AddEliteEmulatorForTesting();

        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        e.Pause();
        yield return WaitForAWhile(e); // Takes some time for the Pause message to reach the emulator
        int frame = e.CurrentFrame;
        Assert.That(frame, Is.GreaterThan(1));
        
        yield return WaitForAWhile(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame));
        e.Unpause();

        yield return WaitForAWhile(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(frame));
    }

    // Helpers

    public void AssertEmulatorIsRunning(Emulator e) {
        Assert.That(e.IsRunning, Is.True);
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Running));
        Assert.That(e.Texture, Is.Not.Null);
    }

    public Emulator AddEliteEmulatorForTesting() {
        var o = new GameObject();
        var e = o.AddComponent<Emulator>();
        e.romFile = eliteRom;
        e.suppressBizhawkPopups = false;
        e.showBizhawkGui = true;
        e.runInEditMode = true;

        return e;
    }

    public static IEnumerator WaitForAWhile(Emulator emulator, float duration = 3f, Action action = null) {
        float beginTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - beginTime < duration) {
            System.Threading.Thread.Sleep(10);
            if (!Application.isPlaying) {
                emulator.Update(); // Have to force the monobehavior to update in edit mode
            }
            action?.Invoke();
            yield return null;
        }
    }
}

}
