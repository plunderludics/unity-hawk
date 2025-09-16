// This is the base class for tests which has the logic to run with multiple combinations of
// (passInputFromUnity, captureEmulatorAudio, showBizhawkGui) params. No actual tests in this file
using NUnit.Framework;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace UnityHawk.Tests {

// bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui
[TestFixture(true, true, false)] // Seems like should be the default case - unity input, unity audio, and hide gui
[TestFixture(false, false, true)] // Test alternate params

// TODO: Instead of running every test twice, maybe better to just have a few extra tests for OS audio, OS input, and show gui
// (Even though it's hard to actually test those are working, but at least test the emulator runs fine)

// We could do more tests with other param combinations but seems like overkill for now, makes the tests run too long
// [TestFixture(false, false, false)]
// [TestFixture(false, false, true)]
public class SharedTestsCore
{
    protected bool _passInputFromUnity;
    protected bool _captureEmulatorAudio;
    protected bool _showBizhawkGui;

    private const float WhileDuration = 5f;
    private const float MomentDuration = 0.5f;
    // protected Rom eliteRom;
    protected Rom swoopRom;
    protected Savestate eliteSavestate2000;
    protected Savestate eliteSavestate5000;
    protected LuaScript testCallbacksLua;

    protected Emulator e;

    public SharedTestsCore(bool passInputFromUnity, bool captureEmulatorAudio, bool showBizhawkGui) {
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

    // Helpers

    protected void ActivateEmulator() {
        e.gameObject.SetActive(true);
    }

    protected static void AssertEmulatorIsRunning(Emulator e) {
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