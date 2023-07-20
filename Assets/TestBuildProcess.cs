using System.IO;
using UnityEngine;
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
