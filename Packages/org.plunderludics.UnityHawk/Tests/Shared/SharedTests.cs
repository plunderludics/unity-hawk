using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using System;


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
    private Rom eliteRom;
    private Rom swoopRom;
    private Savestate eliteSavestate2000;
    private Savestate eliteSavestate5000;
    private LuaScript testCallbacksLua;

    protected Emulator e;
    
    private bool _passInputFromUnity;
    private bool _captureEmulatorAudio;
    private bool _showBizhawkGui;

    public SharedTests(bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui)
    {
        // This breaks when trying to test in standalone player because AssetDatabase is unavailable
        // Probably have to use Addressables instead
        eliteRom = AssetDatabase.LoadAssetAtPath<Rom>("Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteRomForTests.nes");
        swoopRom = AssetDatabase.LoadAssetAtPath<Rom>("Packages/org.plunderludics.UnityHawk/Tests/Shared/swoopRomForTests.n64");

        eliteSavestate2000 = AssetDatabase.LoadAssetAtPath<Savestate>("Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestate2000.savestate");
        eliteSavestate5000 = AssetDatabase.LoadAssetAtPath<Savestate>("Packages/org.plunderludics.UnityHawk/Tests/Shared/eliteSavestate5000.savestate");

        testCallbacksLua = AssetDatabase.LoadAssetAtPath<LuaScript>("Packages/org.plunderludics.UnityHawk/Tests/Shared/testCallbacks.lua");

        Assert.That(eliteRom, Is.Not.Null);
        Assert.That(swoopRom, Is.Not.Null);
        Assert.That(eliteSavestate2000, Is.Not.Null);
        Assert.That(eliteSavestate5000, Is.Not.Null);
        Assert.That(testCallbacksLua, Is.Not.Null);

        _passInputFromUnity = passInputFromUnity;
        _captureEmulatorAudio = captureEmulatorAudio;
        _showBizhawkGui = showBizhawkGui;
    }

    [SetUp]
    public void SetUp() {
        // Set up Emulator object
        var o = new GameObject();
        e = o.AddComponent<Emulator>();
        e.romFile = eliteRom;
        e.runInEditMode = true;
        e.passInputFromUnity = _passInputFromUnity;
        e.captureEmulatorAudio = _captureEmulatorAudio;
        e.showBizhawkGuiInEditor = _showBizhawkGui;
    }
    
    [TearDown]
    public void TearDown() {
        GameObject.DestroyImmediate(e.gameObject);
    }

    [UnityTest]
    public IEnumerator TestEmulatorIsRunning()
    {
        yield return WaitForAWhile(e);
        
        AssertEmulatorIsRunning(e);
    }

    [UnityTest]
    public IEnumerator TestWithSavestate()
    {
        e.saveStateFile = eliteSavestate2000;
        e.Reset();

        yield return WaitForAWhile(e);
        
        AssertEmulatorIsRunning(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(2000)); // Hacky way of checking if the savestate actually got loaded
    }

    [UnityTest]
    public IEnumerator TestPauseAndUnpause()
    {
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

    [UnityTest]
    public IEnumerator TestFrameAdvance()
    {
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

        for (int i = 0; i < 100; i++) {
            e.FrameAdvance();
        }
        yield return WaitForAMoment(e);
        Assert.That(e.CurrentFrame, Is.EqualTo(frame+102));
    }

    [UnityTest]
    public IEnumerator TestLoadState()
    {
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        // Checking frame count is a hacky way of checking if the rom really got loaded
        e.LoadState(eliteSavestate5000);
        yield return WaitForAWhile(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(5000));
        
        e.LoadState(eliteSavestate2000);
        yield return WaitForAWhile(e);
        Assert.That(e.CurrentFrame, Is.GreaterThan(2000));
        Assert.That(e.CurrentFrame, Is.LessThan(5000));
    }

    [UnityTest]
    public IEnumerator TestLoadRom()
    {
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);

        // Checking texture size is a hacky way of knowing if the rom really got loaded
        // Since nes and n64 texture sizes are different
        Assert.That(e.Texture.width, Is.EqualTo(256));
        
        e.LoadRom(swoopRom);
        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);
        Assert.That(e.Texture.width, Is.EqualTo(320));
    }

    private string _submittedResult;
    [UnityTest]
    public IEnumerator TestLuaCallbacks()
    {
        e.luaScriptFile = testCallbacksLua;
        e.Reset();
        e.RegisterLuaCallback("reverseString", (arg) => {
            Debug.Log("reverseString");
            char[] cs = arg.ToCharArray();
            Array.Reverse(cs);
            return new string(cs);
        });
        e.RegisterLuaCallback("submitResult", (arg) => {
            Debug.Log("submitResult");
            _submittedResult = arg;
            return "";
        });

        yield return WaitForAWhile(e);
        AssertEmulatorIsRunning(e);
        Assert.That(_submittedResult, Is.EqualTo("tseT"));
    }

    
    // Helpers

    public static void AssertEmulatorIsRunning(Emulator e) {
        Assert.That(e.IsRunning, Is.True);
        Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Running));
        Assert.That(e.Texture, Is.Not.Null);
    }

    public static IEnumerator WaitForDuration(Emulator emulator, float duration, Action action = null) {
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

    public static IEnumerator WaitForAMoment(Emulator emulator, Action action = null) {
        yield return WaitForDuration(emulator, MomentDuration, action);
    }
    public static IEnumerator WaitForAWhile(Emulator emulator, Action action = null) {
        yield return WaitForDuration(emulator, WhileDuration, action);
    }
}

}
