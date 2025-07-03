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

    [UnityTest]
    public IEnumerator TestRamWatch()
    {
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
            Array.Reverse(cs);
            return new string(cs);
        });
        
        string _submittedResult = null;
        e.RegisterLuaCallback("submitResult", (arg) => {
            Debug.Log("submitResult");
            _submittedResult = arg;
            return "";
        });
        
        e.Reset();

        // yield return WaitForAWhile(e);
        yield return WaitForDuration(e, 10f, null); // Not really sure why but 5s is not long enough here
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
