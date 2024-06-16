using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityHawk.Tests.EditMode {
    public class EditModeTests
    {
        // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
        // `yield return null;` to skip a frame.
        [UnityTest]
        public IEnumerator BigTest1()
        {
            // First find rom
            var eliteRomFile = (DefaultAsset)AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                "Packages/org.plunderludics.UnityHawk/Tests/TestResources/eliteRomForTests.nes");
            var o = new GameObject();
            var e = o.AddComponent<Emulator>();
            e.useManualPathnames = false;
            e.romFile = eliteRomFile;
            e.runInEditMode = false;
            e.Update();
        
            for (var i = 0; i < 100; i++) yield return null;
            Assert.That(e.IsRunning, Is.False); // Emulator should not be running since runInEditMode is false

            e.runInEditMode = true;
            e.Update();
        
            Assert.That(e.Status, Is.EqualTo(Emulator.EmulatorStatus.Started));
            for (var i = 0; i < 100; i++) {
                System.Threading.Thread.Sleep(10);
                e.Update();
                yield return null;
            }
        
            Assert.That(e.IsRunning, Is.True);
        }
    }
}