using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

namespace UnityHawk.Tests.PlayMode {

public class PlayModeTests
{
    // This is copy-pasted from EditModeTests.cs
    // TODO: find a way to share code between them
    [UnityTest]
    public IEnumerator BigTest1()
    {
        // First find rom
        var eliteRomFile = AssetDatabase.LoadAssetAtPath<Rom>(
            "Packages/org.plunderludics.UnityHawk/Tests/TestResources/eliteRomForTests.nes");
        var o = new GameObject();
        var e = o.AddComponent<Emulator>();

        e.romFile = eliteRomFile;
        e.runInEditMode = false;

        for (var i = 0; i < 100; i++) yield return null;

        Assert.That(e.IsRunning, Is.True);
    }
}

}