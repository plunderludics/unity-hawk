using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores.Nintendo.NES;
using BizHawk.Emulation.Cores.Arcades.MAME;
using System.IO;

// using System.Windows.Forms;
using System.Drawing;
using BizHawk.Bizware.OpenTK3;

using System.Runtime.InteropServices;
using System;
using System.Linq;

using System.Threading.Tasks;
using UnityEngine.SceneManagement;

using UnityHawk;

public class TestBuildProcess : MonoBehaviour
{

    void Start()
    {
#if UNITY_EDITOR
        Scene scene = SceneManager.GetActiveScene();
        BuildProcessing.ProcessScene(scene, "xx");
#endif
    }
}
