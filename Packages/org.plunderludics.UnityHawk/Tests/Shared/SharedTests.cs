using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

using System.IO;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// using System;

// (A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
// `yield return null;` to skip a frame.)

// What we really need but don't have is a way to test the build process
// to make sure all the files get copied in correctly etc. Not sure how to do it yet

namespace UnityHawk.Tests {

// bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui
[TestFixture(true, false, false)]
[TestFixture(true, true, false)]
[TestFixture(false, false, false)]
[TestFixture(false, false, true)]
public class SharedTests
{
    private const float WhileDuration = 5f;
    private const float MomentDuration = 0.5f;
    // private Rom eliteRom;
    private Rom swoopRom;
    private Savestate eliteSavestate2000;
    private Savestate eliteSavestate5000;
    private LuaScript testCallbacksLua;

    protected Emulator e;
    
    private bool _passInputFromUnity;
    private bool _captureEmulatorAudio;
    private bool _showBizhawkGui;

    public SharedTests(bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui) {
        _passInputFromUnity = passInputFromUnity;
        _captureEmulatorAudio = captureEmulatorAudio;
        _showBizhawkGui = showBizhawkGui;
    }

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Load the test scene
        yield return LoadScene("UnityHawkTestScene");

        // Test scene contains:
        // - Emulator with swoop rom and no savestate, on a disabled game object
        // - SharedTestAssets with other needed assets

        SharedTestAssets assets = Object.FindObjectOfType<SharedTestAssets>();
        swoopRom = assets.swoopRom;
        eliteSavestate2000 = assets.eliteSavestate2000;
        eliteSavestate5000 = assets.eliteSavestate5000;
        testCallbacksLua = assets.testCallbacksLua;

        // Set up Emulator object
        e = Object.FindObjectOfType<Emulator>(includeInactive: true);
        SetParamsOnEmulator(e);
    }

    protected void ActivateEmulator() {
        e.gameObject.SetActive(true);
    }

    // [TearDown]
    // public void TearDown() {
    //     GameObject.DestroyImmediate(e.gameObject);
    // }

    [UnityTest]
    public IEnumerator TestEmulatorIsRunning()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        
        AssertEmulatorIsRunning(e);
    }

    [UnityTest]
    public IEnumerator TestWithSavestate()
    {
        e.saveStateFile = eliteSavestate2000;

        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        Assert.That(e.CurrentFrame, Is.GreaterThan(2000)); // Hacky way of checking if the savestate actually got loaded
    }

    [UnityTest]
    public IEnumerator TestPauseAndUnpause()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        e.Pause();
        yield return WaitForAMoment(e); // Takes some time for the Pause message to reach the emulator
        int frame = e.CurrentFrame;
        Assert.That(frame, Is.GreaterThan(1));
        
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame));
        e.Unpause();

        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(frame));
    }

    [UnityTest]
    public IEnumerator TestFrameAdvance()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        e.Pause();
        yield return WaitForAMoment(e);
        int frame = e.CurrentFrame;
        Assert.That(frame, Is.GreaterThan(1));
        
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame));

        e.FrameAdvance();
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame+1));
        
        e.FrameAdvance();
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame+2));

        for (int i = 0; i < 20; i++) {
            e.FrameAdvance();
            yield return WaitForAMoment(e);
        }
        Assert.That(e.CurrentFrame, Is.EqualTo(frame+22));
    }

    [UnityTest]
    public IEnumerator TestLoadState()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        // Checking frame count is a hacky way of checking if the rom really got loaded
        e.LoadState(eliteSavestate5000);
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(5000));
        
        e.LoadState(eliteSavestate2000);
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(2000));
        Assert.That(e.CurrentFrame, Is.LessThan(5000));
    }

    [UnityTest]
    public IEnumerator TestLoadRom()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        // Checking texture size is a hacky way of knowing if the rom really got loaded
        // Since nes and n64 texture sizes are different
        Assert.That(e.Texture.width, Is.EqualTo(256));
        Assert.That(e.SystemId, Is.EqualTo("NES"));
        
        e.LoadRom(swoopRom);
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);
        Assert.That(e.Texture.width, Is.EqualTo(320));
        Assert.That(e.SystemId, Is.EqualTo("N64"));
    }

    [UnityTest]
    public IEnumerator TestRamWatch()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        uint u = 0;
        int s = 0;
        float f = 0f;

        // Meaningless/arbitrary bits of memory cause i'm lazy, but these seem to be static at the beginning of the elite rom, so should be fine for testing
        e.WatchUnsigned(0x00E2, 1, true, domain: null, (value) => u = value);
        e.WatchSigned(0x00A2, 4, false, domain: null, (value) => s = value);
        e.WatchFloat(0x00E2, false, domain: null, (value) => f = value);

        yield return WaitForAMoment(e);

        Assert.That(u, Is.EqualTo(96));
        Assert.That(s, Is.EqualTo(-16580608));
        Assert.That(f, Is.EqualTo(3.306212e-39f));
    }

    [UnityTest]
    public IEnumerator TestRamWrite()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        uint u = 0;
        int s = 0;
        float f = 0f;

        // Again, just random chunks of memory, on the elite title screen we seem to be able to write them without them changing 
        e.WatchUnsigned(0x00E2, 1, true, domain: null, (value) => u = value);
        e.WatchSigned(0x003C, 4, false, domain: null, (value) => s = value);
        e.WatchFloat(0x003C, false, domain: null, (value) => f = value);
        yield return WaitForAMoment(e);

        e.WriteUnsigned(0x00E2, value: 99, size: 1, isBigEndian: true);
        yield return WaitForAMoment(e);
        Assert.That(u, Is.EqualTo(99));

        e.WriteSigned(0x003C, value: -99, size: 4, isBigEndian: false);
        yield return WaitForAMoment(e);
        Assert.That(s, Is.EqualTo(-99));

        e.WriteFloat(0x003C, value: 123.4f, isBigEndian: false);
        yield return WaitForAMoment(e);
        Assert.That(f, Is.EqualTo(123.4f));
    }

    [UnityTest]
    public IEnumerator TestRamFreeze()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        long addr = 0x0063; // This address changes constantly on the elite title screen so we can try to freeze this

        uint v = 0;
        e.WatchUnsigned(addr, 1, true, domain: null, (value) => v = value);
        yield return WaitForAMoment(e);
        uint initialValue = v;
        yield return WaitForAMoment(e);

        bool allSame = true;
        for (int i = 0; i < 10; i++) {
            uint newValue = v;
            allSame &= (newValue == initialValue);
            yield return WaitForAMoment(e);
        }
        Assert.That(allSame, Is.False); // Haven't frozen yet, value should be different at least some of the time

        e.Freeze(addr, size: 1);
        yield return WaitForAMoment(e);

        initialValue = v;
        yield return WaitForAMoment(e);

        for (int i = 0; i < 10; i++) {
            uint newValue = v;
            yield return WaitForAMoment(e);
            Assert.That(newValue, Is.EqualTo(initialValue)); // Should be the same every time
        }
    }

    [UnityTest]
    public IEnumerator TestLuaCallbacks()
    {
        e.luaScriptFile = testCallbacksLua;
        e.RegisterLuaCallback("reverseString", (arg) => {
            Debug.Log("reverseString");
            char[] cs = arg.ToCharArray();
            System.Array.Reverse(cs);
            return new string(cs);
        });
        
        string _submittedResult = null;
        e.RegisterLuaCallback("submitResult", (arg) => {
            Debug.Log("submitResult");
            _submittedResult = arg;
            return "";
        });
        
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        Assert.That(_submittedResult, Is.EqualTo("tseT"));
    }

    [UnityTest]
    public IEnumerator TestRestart()
    {
        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        e.Restart();
        yield return WaitForAWhile(e);
        yield return WaitForAWhile(e); // Have to wait longer after restart for some reason
        AssertEmulatorIsRunning(e);
    }

    [UnityTest]
    // Test that the rom and savestatefiles are actually present
    // This test should work correctly in both editor and standalone player (?)
    public IEnumerator TestAssetPaths() {
        yield return null;

        var rom = e.romFile;
        // Hard to actually test that emulator is using the correct path but at least check that the Paths.GetAssetPath method
        // (which Emulator.cs uses) returns what we expect
        var romPath = Path.GetFullPath(
            Paths.GetAssetPath(rom)
        );
        var expectedRomPath = Path.GetFullPath(
#if UNITY_EDITOR
            // In editor, asset should be in <project_root>/Packages/...
            "Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes"
#else
            // In build, it's in <build_dir>/<project_name>_Data/Packages/...
            Path.Combine(Application.dataPath, "./Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes")
#endif
        );
        Assert.That(romPath, Is.EqualTo(expectedRomPath));
        Assert.That(File.Exists(romPath));

        var savestate = eliteSavestate5000;
        // Check that the savestate file path is correct and the file exists
        var savestatePath = Path.GetFullPath(
            Paths.GetAssetPath(savestate)
        );
        var expectedSavestatePath = Path.GetFullPath(
#if UNITY_EDITOR
            "Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestate5000.savestate"
#else
            Path.Combine(Application.dataPath, "./Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestate5000.savestate")
#endif
        );
        Assert.That(savestatePath, Is.EqualTo(expectedSavestatePath));
        Assert.That(File.Exists(savestatePath));
    }

    [UnityTest]
    public IEnumerator TestMultipleScenes() {
        // First scene is already loaded and tested.
        // Load a second scene and test
        yield return LoadScene("UnityHawkTestScene2");
        // This scene has an emulator with a savestate that's not referenced in the first scene

        e = Object.FindObjectOfType<Emulator>(includeInactive: true);
        SetParamsOnEmulator(e);

        ActivateEmulator();
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        Assert.That(e.CurrentFrame, Is.GreaterThan(3000)); // Check if the savestate was loaded correctly
    }

    // TODO More tests:
    // - test .cue files are copied into build
    // - test extraAssets and includeInactive params on BuildSettings
    // - test with mocked InputSystem input (?)
    // - test auto-restart when emuhawk proc killed
    // - test auto-restart in editor when params change
    // - test SetVolume, Mute, SetSpeed etc? Who cares I guess

    // Helpers

    public static void AssertEmulatorIsRunning(Emulator e) {
        Assert.That(e.IsRunning, Is.True);
        Assert.That(e.CurrentStatus, Is.EqualTo(Emulator.Status.Running));
        Assert.That(e.Texture, Is.Not.Null);
    }

    public static IEnumerator WaitForDuration(Emulator emulator, float duration) {
        float beginTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - beginTime < duration) {
            System.Threading.Thread.Sleep(10);
            if (!Application.isPlaying) {
                emulator.Update(); // Have to force the monobehavior to update in edit mode
            }
            yield return null;
        }
    }

    public static IEnumerator WaitForAMoment(Emulator emulator) {
        yield return WaitForDuration(emulator, MomentDuration);
    }
    public static IEnumerator WaitForAWhile(Emulator emulator) {
        yield return WaitForDuration(emulator, WhileDuration);
    }

    protected static IEnumerator LoadScene(string sceneName) {
        if (Application.isPlaying) {
            SceneManager.LoadScene(sceneName);
            yield return null; // Wait for scene to load
            if (SceneManager.GetActiveScene().name != sceneName) {
                Debug.LogError($"[unity-hawk] Test scene failed to load - probably needs to be included in build settings");
            }
        } else {
#if UNITY_EDITOR
            // Have to load by full path
            var scenePath = Path.Join("Packages/org.plunderludics.UnityHawk/Tests/Shared/", sceneName + ".unity");
            EditorSceneManager.OpenScene(scenePath);
#else
            throw new System.Exception("How can Application.isPlaying be false in a build");
#endif
        }
    }

    protected void SetParamsOnEmulator(Emulator e) {
        e.runInEditMode = true;
        e.passInputFromUnity = _passInputFromUnity;
        e.captureEmulatorAudio = _captureEmulatorAudio;
        e.showBizhawkGuiInEditor = _showBizhawkGui;
    }
}

}
