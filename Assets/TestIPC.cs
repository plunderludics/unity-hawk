using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

using SharedMemory;

public class TestIPC : MonoBehaviour
{
    public Renderer targetRenderer;
    // IpcClient ipcClient;
    Texture2D _bufferTexture;
    RenderTexture _renderTexture;    
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    private Stopwatch stopwatch = new Stopwatch();
    SharedArray<int> sharedTextureBuffer; // single-element array

    int[] localTextureBuffer;

    public Material _textureCorrectionMat;

    Process bizhawk;

    public bool showBizhawkGui = false;

    public string rompath = "F:/pludics/roms/Creative Camp (USA)/Creative Camp (USA).cue";

    private string _sharedTextureMemoryName;

    void Start() {
        string args = "";
        if (!showBizhawkGui) args += "--headless ";
        
        _sharedTextureMemoryName = "unityhawk-texbuf-" + GetInstanceID();
        args += $"--share-texture={_sharedTextureMemoryName} ";

        args += '"' + rompath + '"';

        Debug.Log($"Attempting to start new process {UnityHawk.UnityHawk.emuhawkExePath} with args '{args}'");
        bizhawk = Process.Start(UnityHawk.UnityHawk.emuhawkExePath, args);
        
        AttemptOpenSharedTextureBuffer();
    }

    void AttemptOpenSharedTextureBuffer() {
        try {
            sharedTextureBuffer = new (name: _sharedTextureMemoryName);
            Debug.Log("Connected to shared texture buffer");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }
    // Start is called before the first frame update
    // void Start()
    // {
    //     ipcClient = new IpcClient();
    //     ipcClient.Initialize(12345);
    //     _bufferTexture = new Texture2D(videoBufferWidth, videoBufferHeight, textureFormat, true);

    //     if (targetRenderer) targetRenderer.material.mainTexture = _bufferTexture;

    //     Debug.Log("Started client.");
    // }

    // // Update is called once per frame
    void Update(){
        if (sharedTextureBuffer != null) {
            // int width;
            // int height;
            // sharedTextureBuffer.Read(out width, 0);
            // sharedTextureBuffer.Read(out height, 1);
            // if current local texbuf is smaller than width*height, make it bigger
            // if (localTextureBuffer.Length < width*height) {
               // Debug.Log($"Enlarging texture buffer from {localTextureBuffer.Length} to {width*height}");
                // localTextureBuffer = new int[width*height];
            // }
            
            int[] localTextureBuffer = new int[sharedTextureBuffer.Length];
            sharedTextureBuffer.CopyTo(localTextureBuffer, 0);

            int width = localTextureBuffer[sharedTextureBuffer.Length - 2];
            int height = localTextureBuffer[sharedTextureBuffer.Length - 1];

            // Debug.Log($"{width}, {height}");
            // resize textures if necessary
            if ((width != 0 && height != 0)
            && (_bufferTexture == null
            || _renderTexture == null
            ||  _bufferTexture.width != width
            ||  _bufferTexture.height != height)) {
                InitTextures(width, height);
            }

            if (_bufferTexture) {
                _bufferTexture.SetPixelData(localTextureBuffer, 0);
                _bufferTexture.Apply(/*updateMipmaps: false*/);

                // Correct issues with the texture by applying a shader and blitting to a separate render texture:
                Graphics.Blit(_bufferTexture, _renderTexture, _textureCorrectionMat, 0);
            }
        } else {
            AttemptOpenSharedTextureBuffer();
        }
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _bufferTexture = new     Texture2D(width, height, textureFormat, false);
        _renderTexture = new RenderTexture(width, height, depth:0, format:renderTextureFormat);
        if (targetRenderer) targetRenderer.material.mainTexture = _renderTexture;
    }

    void OnDisable() {
        bizhawk.Kill();
    }
}
