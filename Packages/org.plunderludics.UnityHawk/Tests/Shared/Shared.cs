using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using System;

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

namespace UnityHawk.Tests {
    
public class Shared
{
    public static Emulator AddEliteEmulatorForTesting() {
        // First find rom
        var eliteRomFile = AssetDatabase.LoadAssetAtPath<Rom>(
            "Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes");
        Assert.That(eliteRomFile, Is.Not.Null);

        var o = new GameObject();
        var e = o.AddComponent<Emulator>();
        e.romFile = eliteRomFile;
        e.suppressBizhawkPopups = false;
        e.showBizhawkGui = true;

        return e;
    }

    public static IEnumerator WaitForAWhile(float duration = 3f, Action action = null) {
        float beginTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - beginTime < duration) {
            System.Threading.Thread.Sleep(10);
            action?.Invoke();
            yield return null;
        }
    }
}

}
