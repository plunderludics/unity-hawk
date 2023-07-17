using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

// using ZetaIpc.Runtime.Client;
// using Cloudtoid.Interprocess;
using H.Pipes;
using H.Formatters;
using H.Pipes.Args;

public class TestIPC : MonoBehaviour
{
    public Renderer targetRenderer;
    // IpcClient ipcClient;
    Texture2D _bufferTexture;
    RenderTexture _renderTexture;
    public int videoBufferWidth, videoBufferHeight;
    
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    private Stopwatch stopwatch = new Stopwatch();
    PipeClient<int[]> client;

    int[] latestTexBuf;

    public Material _textureCorrectionMat;

    Process bizhawk;

    async void Start() {
        bizhawk = Process.Start("F:/pludics/BizHawk-src/output/EmuHawk.exe", "\"F:/pludics/roms/Creative Camp (USA)/Creative Camp (USA).cue\"");
        
        latestTexBuf = null;

        Debug.Log("Start");
        _bufferTexture = new Texture2D(videoBufferWidth, videoBufferHeight, textureFormat, false);
        _renderTexture = new RenderTexture(videoBufferWidth, videoBufferHeight, depth:0, format:renderTextureFormat);

        if (targetRenderer) targetRenderer.material.mainTexture = _renderTexture;

        client = new("bizhawk-pipe");
        client.MessageReceived += HandleBizHawkMessage;
        client.Disconnected += (o, args) => Debug.Log("Disconnected from server");
        client.Connected += (o, args) => Debug.Log("Connected to server");
        // client.ExceptionOccurred += (o, args) => OnExceptionOccurred(args.Exception);

        await client.ConnectAsync();

        RequestTexture();

        // await Task.Delay(Timeout.InfiniteTimeSpan);
    }
    
    void RequestTexture() {
        stopwatch.Start();

        // request new texture
        client.WriteAsync(null);
    }

    void HandleBizHawkMessage(object sender, ConnectionMessageEventArgs<int[]> args) {
        Debug.Log("HandleBizHawkMessage");
        try {
            Debug.Log($"Server {args.Connection.PipeName} says: {args.Message}");
            // Debug.Log($"Took {Time.time - _sendTime} seconds");
            // Debug.Log("Hi");
            stopwatch.Stop();
            Debug.Log($"Roundtrip - {stopwatch.Elapsed.TotalMilliseconds} ms");
            stopwatch.Reset();

            // string id = args.Message.id;
            latestTexBuf = args.Message;

            // Debug.Log($"width {width}; height: {height}");
            Debug.Log($"Sizeof buf: {latestTexBuf.Length}; buf[100]: {latestTexBuf[100]}");

        } catch (Exception e) {
            Debug.LogError(e);
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
    async void Update(){
        if (latestTexBuf != null) {
            _bufferTexture.SetPixelData(latestTexBuf, 0);
            _bufferTexture.Apply(/*updateMipmaps: false*/);

            // Correct issues with the texture by applying a shader and blitting to a separate render texture:
            Graphics.Blit(_bufferTexture, _renderTexture, _textureCorrectionMat, 0);
            
            latestTexBuf = null;

            RequestTexture();
        }
    }

    async void OnDisable() {
        bizhawk.Kill();
    }
}
