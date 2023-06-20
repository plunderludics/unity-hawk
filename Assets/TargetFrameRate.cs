using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetFrameRate : MonoBehaviour
{
    public int targetFrameRate = -1; // -1 means use the system default
    // Update is called once per frame
    void Update()
    {
        Application.targetFrameRate = targetFrameRate;
    }
}
