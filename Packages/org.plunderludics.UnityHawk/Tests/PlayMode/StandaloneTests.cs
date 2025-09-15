// Currently most of the tests don't work in standalone player because they load assets at runtime
// For now just have a separate basic test for standalone player to make sure the build works
// TODO: In future, all PlayModeTests should be runnable in standalone player
// (probably need to either use Addressables or a test fixture scene)

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using System.IO;

namespace UnityHawk.Tests {
public class StandaloneTests {
    [UnityTest]
    public IEnumerator TestStandalone() {
#if UNITY_EDITOR
        // TODO: once we integrate editor and standalone tests, this can just check that the asset paths are valid and the files exist in both cases
        Debug.LogError("Don't run standalone tests in the editor");
        yield return null;
#else
        // Load the test scene
        SceneManager.LoadScene("UnityHawkTestScene");
        yield return null; // Wait for scene to load
        
        var emulator = Object.FindObjectOfType<Emulator>();
        yield return SharedTests.WaitForAWhile(emulator);
        SharedTests.AssertEmulatorIsRunning(emulator);

        // Test that the files are actually within the build
        var rom = emulator.romFile;
        // Hard to actually test that emulator is using the correct path but at least check that the Paths.GetAssetPath method
        // (which Emulator.cs uses) returns what we expect
        var romPath = Path.GetFullPath(
            Paths.GetAssetPath(rom)
        );
        var expectedRomPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "./Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes")
        );
        Assert.That(romPath, Is.EqualTo(expectedRomPath));
        Assert.That(File.Exists(romPath));

        var savestate = emulator.saveStateFile;
        // Check that the savestate file path is correct and the file exists
        var savestatePath = Path.GetFullPath(
            Paths.GetAssetPath(savestate)
        );
        var expectedSavestatePath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "./Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestate5000.savestate")
        );
        Assert.That(savestatePath, Is.EqualTo(expectedSavestatePath));
        Assert.That(File.Exists(savestatePath));
#endif
    }

    // More tests:
    // - copying .cue files
}
}