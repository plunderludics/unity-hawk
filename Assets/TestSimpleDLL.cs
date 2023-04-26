using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleDLL;

public class TestSimpleDLL : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Log(Simple.GenerateRandom(10, 50));
    }
}
