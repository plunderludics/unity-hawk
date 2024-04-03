using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityHawk;
using UnityEditor;

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

public class PlayModeTests
{
    // This is copy-pasted from EditModeTests.cs
    // TODO: find a way to share code between them
    [UnityTest]
    public IEnumerator BigTest1()
    {
        // First find rom
        DefaultAsset eliteRomFile = (DefaultAsset)AssetDatabase.LoadAssetAtPath<DefaultAsset>(
            "Packages/org.plunderludics.UnityHawk/Tests/TestResources/eliteRomForTests.nes");
        GameObject o = new GameObject();
        Emulator e = o.AddComponent<Emulator>();
        e.useManualPathnames = false;
        e.romFile = eliteRomFile;
        e.runInEditMode = false;
        for (int i = 0; i < 100; i++) yield return null;
        Assert.That(e.IsRunning, Is.True);
    }
}
