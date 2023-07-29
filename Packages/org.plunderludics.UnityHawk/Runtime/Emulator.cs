// This is the main user-facing component (ie MonoBehaviour)
// acts as a bridge between Unity and BizHawkInstance, handling input,
// graphics, audio, multi-threading, and framerate throttling.

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

using NaughtyAttributes;

using UnityEngine;
using Unity.Profiling;

using SharedMemory;

using Plunderludics;

namespace UnityHawk {

[ExecuteInEditMode]
public class Emulator : MonoBehaviour
{
    static readonly bool _targetMac = 
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        true;
#else
        false;
#endif

    public bool useAttachedRenderer = true;
    [HideIf("useAttachedRenderer")]
    public Renderer targetRenderer;

    [Tooltip("If false, BizHawk will get input directly from the OS")]
    public bool passInputFromUnity = true;
    public bool captureEmulatorAudio = false;

    [Header("Files")]
    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAssetPath)
    public string romFileName = "Roms/mario.nes";
    public string configFileName = ""; // Leave empty for default config.ini
    public string saveStateFileName = ""; // Leave empty to boot clean
    public string luaScriptFileName;

    private const string firmwareDirName = "Firmware"; // Firmware loaded from StreamingAssets/Firmware
    
    [Header("Debug")]
    public new bool runInEditMode = false;
    public bool showBizhawkGui = false;
    public bool writeBizhawkLogs = true;
    [ShowIf("writeBizhawkLogs")]
    [ReadOnly, SerializeField] string bizhawkLogLocation;

    [ReadOnly, SerializeField] bool _initialized;
    [ReadOnly, SerializeField] bool _isRunning;

    private static string bizhawkLogDirectory = "BizHawkLogs";

    // [Make these public for debugging texture stuff]
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    // Interface for other scripts to use
    public RenderTexture Texture => _renderTexture;
    public bool IsRunning => _isRunning;

    Process emuhawk;

    private string _audioRpcBufferName;
    RpcBuffer _audioRpcBuffer;

    private string _sharedTextureBufferName;
    SharedArray<int> _sharedTextureBuffer;

    private IInputProvider inputProvider; // this is fixed for now but could be configurable in the future
    private string _sharedInputBufferName;
    CircularBuffer _sharedInputBuffer;

    Texture2D _bufferTexture;
    RenderTexture _renderTexture; // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    static int AudioBufferSize = 44100*10; // Size of local audio buffer
    short[] _audioBuffer; // circular buffer (queue) to locally store audio samples accumulated from the emulator
    int _audioBufferStart, _audioBufferEnd;
    int _audioSamplesNeeded; // track how many samples unity wants to consume

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

    Material _renderMaterial; // just used for rendering in edit mode

    StreamWriter _bizHawkLogWriter;

#if UNITY_EDITOR
    // Set filename fields based on sample directory
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void LoadSample() {
        string path = EditorUtility.OpenFilePanel("Sample", "", "");
        if (!String.IsNullOrEmpty(path)) {
            SetFromSample(path);
        }
    }
#endif

    // Returns the path that will be loaded for a filename param (rom, lua, config, savestate)
    // [probably should go in Paths.cs]
    public static string GetAssetPath(string path) {
        if (path == Path.GetFullPath(path)) {
            // Already an absolute path, don't change it [Path.Combine below will do this anyway but just to be explicit]
            return path;
        } else {
            return Path.Combine(Application.streamingAssetsPath, path); // Load relative to StreamingAssets/
        }
    }

    void OnEnable()
    {
        _initialized = false;
        if (runInEditMode || Application.isPlaying) {
            Initialize();
        }
    }

    // For editor convenience: Set filename fields by reading a sample directory
    public void SetFromSample(string samplePath) {
        // Read the sample dir to get the necessary filenames (rom, config, etc)
        Sample s = Sample.LoadFromDir(samplePath);
        romFileName = s.romPath;
        configFileName = s.configPath;
        saveStateFileName = s.saveStatePath;
        var luaScripts = s.luaScriptPaths.ToList();
        if (luaScripts != null && luaScripts.Count > 0) {
            luaScriptFileName = luaScripts[0];
            if (luaScripts.Count > 1) {
                Debug.LogWarning($"Currently only support one lua script, loading {luaScripts[0]}");
                // because bizhawk only supports passing a single lua script from the command line
            }
        }
    }

    void Update() {
        if (!Application.isPlaying && !runInEditMode) {
            if (_isRunning) {
                Deactivate();
            }
            return;
        } else if (!_initialized) {
            Initialize();
        }

        if (_sharedTextureBuffer != null && _sharedTextureBuffer.Length > 0) {
            UpdateTextureFromBuffer();
        } else {
            if (_sharedTextureBuffer != null) {
                // i don't get why but this happens sometimes
                Debug.LogWarning($"shared buffer length was 0 or less: {_sharedTextureBuffer.Length}");
            }
            AttemptOpenSharedTextureBuffer();
        }

        if (passInputFromUnity) {
            inputProvider.Update();
            if (_sharedInputBuffer != null && _sharedInputBuffer.NodeCount > 0) {
                WriteInputToBuffer();
            } else {
                AttemptOpenSharedInputBuffer();
            }
        }

        if (captureEmulatorAudio) {
            if (_audioRpcBuffer != null) {
                // request audio buffer over rpc
                CaptureBizhawkAudio();
            } else {
                AttemptOpenAudioRpcBuffer();
            }
        }

        if (emuhawk != null && emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
        }

        _audioSamplesNeeded += (int)(44100*Time.deltaTime);
    }

    void OnDisable() {
        if (_initialized) {
            Deactivate();
        }
    }

    void Initialize() {
        _isRunning = false;

        _textureCorrectionMat = new Material(Resources.Load<Shader>(textureCorrectionShaderName));

        if (useAttachedRenderer) {
            // Default to the attached Renderer component, if there is one
            targetRenderer = GetComponent<Renderer>();
            if (!targetRenderer) {
                Debug.LogWarning("No Renderer attached, will not display emulator graphics");
            }
        }

        // Init audio buffer
        _audioBuffer = new short[AudioBufferSize];
        _audioSamplesNeeded = 0;
        ClearAudioBuffer();

        // Process filename args
        string configPath;
        if (String.IsNullOrEmpty(configFileName)) {
            configPath = Path.GetFullPath(Paths.defaultConfigPath);
        } else {
            configPath = GetAssetPath(configFileName);
        }

        string romPath = GetAssetPath(romFileName);

        string luaScriptFullPath = null;
        if (!string.IsNullOrEmpty(luaScriptFileName)) {
            luaScriptFullPath = GetAssetPath(luaScriptFileName);
        }

        string saveStateFullPath = null;
        if (!String.IsNullOrEmpty(saveStateFileName)) {
            saveStateFullPath = GetAssetPath(saveStateFileName);
        }

        // start emuhawk.exe w args
        string exePath = Path.GetFullPath(Paths.emuhawkExePath);
        emuhawk = new Process();
        emuhawk.StartInfo.UseShellExecute = false;
        var args = emuhawk.StartInfo.ArgumentList;
        if (_targetMac) {
            // Doesn't really work yet, need to make some more changes in the bizhawk executable
            emuhawk.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Paths.dllDir;
            emuhawk.StartInfo.EnvironmentVariables["MONO_PATH"] = Paths.dllDir;
            emuhawk.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono";
            if (showBizhawkGui) {
                Debug.LogWarning("'Show Bizhawk Gui' is not supported on Mac'");
            }
            args.Add(exePath);
        } else {
            // Windows
            emuhawk.StartInfo.FileName = exePath;
            emuhawk.StartInfo.UseShellExecute = false;
        }

        args.Add($"--firmware={GetAssetPath(firmwareDirName)}"); // could make this configurable but idk if that's really useful

        if (!showBizhawkGui) args.Add("--headless");

        _sharedTextureBufferName = "unityhawk-texture-" + GetInstanceID();
        args.Add($"--write-texture-to-shared-buffer={_sharedTextureBufferName}");

        if (passInputFromUnity) {
            _sharedInputBufferName = "unityhawk-input-" + GetInstanceID();
            args.Add($"--read-input-from-shared-buffer={_sharedInputBufferName}");

            // for now use a fixed implementation of IInputProvider but
            // in principle could be configurable - easy to add input events programmatically, etc
            inputProvider = new InputProvider();
        }

        if (captureEmulatorAudio) {
            _audioRpcBufferName = "unityhawk-audio-" + GetInstanceID();
            args.Add($"--share-audio-over-rpc-buffer={_audioRpcBufferName}");
        }

        if (saveStateFullPath != null) {
            args.Add($"--load-state={saveStateFullPath}");
        }

        if (luaScriptFullPath != null) {
            args.Add($"--lua={luaScriptFullPath}");
        }

        args.Add($"--config={configPath}");

        args.Add(romPath);

        if (writeBizhawkLogs) {
            // Redirect bizhawk output + error into a log file
            string logFileName = $"{this.name}-{GetInstanceID()}.log";
            Directory.CreateDirectory (bizhawkLogDirectory);
            bizhawkLogLocation = Path.Combine(bizhawkLogDirectory, logFileName);
            if (_bizHawkLogWriter != null) _bizHawkLogWriter.Dispose();
            _bizHawkLogWriter = new(bizhawkLogLocation);

            emuhawk.StartInfo.RedirectStandardOutput = true;
            emuhawk.StartInfo.RedirectStandardError = true;
            emuhawk.OutputDataReceived += new DataReceivedEventHandler((sender, e) => LogBizHawk(sender, e, false));
            emuhawk.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => LogBizHawk(sender, e, true));
        }

        Debug.Log($"{exePath} {string.Join(' ', args)}");
        emuhawk.Start();
        emuhawk.BeginOutputReadLine();
        emuhawk.BeginErrorReadLine();

        AttemptOpenSharedTextureBuffer();
        if (passInputFromUnity) {
            AttemptOpenSharedInputBuffer();
        }
        if (captureEmulatorAudio) {
            AttemptOpenAudioRpcBuffer();
        }

        _initialized = true;
    }

    void WriteInputToBuffer() {
        // Get input from inputProvider, serialize and write to the shared memory
        InputEvent? ie;
        while ((ie = inputProvider.DequeueEvent()).HasValue) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            BizHawk.UnityHawk.InputEvent bie = ConvertInput.ToBizHawk(ie.Value);
            byte[] serialized = Serialize(bie);
            // Debug.Log($"Writing buffer: {ByteArrayToString(serialized)}");
            int amount = _sharedInputBuffer.Write(serialized, timeout: 0);
            if (amount <= 0) {
                Debug.LogWarning("Failed to write key event to shared buffer");
            }
        }
    }

    private byte [] Serialize(object obj)
    {
        int len = Marshal.SizeOf(obj);
        byte [] arr = new byte[len];
        IntPtr ptr = Marshal.AllocHGlobal(len);
        Marshal.StructureToPtr(obj, ptr, true);
        Marshal.Copy(ptr, arr, 0, len);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    // Request audio samples (since last call) from Bizhawk, and store them into a buffer
    // to be played back in OnAudioFilterRead
    void CaptureBizhawkAudio() {
        // _audioSamplesNeeded tracks how many samples have been requested by unity since last call to this method
        int nSamples = _audioSamplesNeeded;
        if (nSamples == 0) return;

        Debug.Log($"Requesting {nSamples} samples over RPC");

        byte[] payload = BitConverter.GetBytes(nSamples);
        RpcResponse response = _audioRpcBuffer.RemoteRequest(payload);
        if (!response.Success) {
            Debug.LogWarning("Rpc call to get audio from BizHawk failed for some reason");
            return;
        }
        _audioSamplesNeeded = 0; // Consumed samples from bizhawk so reset counter to 0

        // convert bytes into short
        byte[] bytes = response.Data;
        short[] samples = new short[bytes.Length/2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length);
        Debug.Log($"Got audio over rpc: {samples.Length} samples, first: {samples[0]}");

        if (samples.Length != nSamples) {
            Debug.LogWarning($"Requested {nSamples} samples but got {samples.Length}");
        }

        // Append samples to running audio buffer to be played back later
        for (int i = 0; i < nSamples; i++) {
            if (AudioBufferLength() == _audioBuffer.Length - 1) {
                Debug.LogWarning("local audio buffer full, dropping samples");
                break;
            }
            _audioBuffer[_audioBufferEnd] = samples[i];
            _audioBufferEnd++;
            _audioBufferEnd %= _audioBuffer.Length;
        }
    }

    void UpdateTextureFromBuffer() {
        // Get the texture buffer and dimensions from BizHawk via the shared memory file
        // protocol has to match MainForm.cs in BizHawk
        // TODO should probably put this protocol in some shared schema file or something idk
        int[] localTextureBuffer = new int[_sharedTextureBuffer.Length];
        _sharedTextureBuffer.CopyTo(localTextureBuffer, 0);
        int width = localTextureBuffer[_sharedTextureBuffer.Length - 2];
        int height = localTextureBuffer[_sharedTextureBuffer.Length - 1];

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
    }

    void Deactivate() {
        _initialized = false;
        if (_bizHawkLogWriter != null) {
            _bizHawkLogWriter.Close();
        }
        if (emuhawk != null && !emuhawk.HasExited) {
            // Kill the emuhawk process
            emuhawk.Kill();
        }
        _isRunning = false;
        if (_sharedTextureBuffer != null) _sharedTextureBuffer.Close();
        _sharedTextureBuffer = null;
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _bufferTexture = new     Texture2D(width, height, textureFormat, false);
        _renderTexture = new RenderTexture(width, height, depth:0, format:renderTextureFormat);
        if (targetRenderer) {
            if (!Application.isPlaying) {
                // running in edit mode, manually make a clone of the renderer material
                // to avoid unity's default cloning behavior that spits out an error message
                // (probably still leaks materials into the scene but idk if it matters)
                if (_renderMaterial == null) {
                    _renderMaterial = Instantiate(targetRenderer.sharedMaterial);
                    _renderMaterial.name = targetRenderer.sharedMaterial.name;
                }
                _renderMaterial.mainTexture = _renderTexture;

                targetRenderer.material = _renderMaterial;
            } else  {
                // play mode, just let unity clone the material the default way
                targetRenderer.material.mainTexture = _renderTexture;
            }
        }
    }

    void AttemptOpenSharedTextureBuffer() {
        // Debug.Log("AttemptOpenSharedTextureBuffer");
        try {
            _sharedTextureBuffer = new (name: _sharedTextureBufferName);
            _isRunning = true; // if we're able to connect to the texture buffer, assume the rom is running ok
            Debug.Log("Connected to shared texture buffer");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }
    
    void AttemptOpenSharedInputBuffer() {
        try {
            _sharedInputBuffer = new (name: _sharedInputBufferName);
            Debug.Log("Connected to shared input buffer");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }

    void AttemptOpenAudioRpcBuffer() {
        try {
            _audioRpcBuffer = new (name: _audioRpcBufferName);
            Debug.Log("Connected to audio rpc buffer");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }

    void LogBizHawk(object sender, DataReceivedEventArgs e, bool isError) {
        string msg = e.Data;
        if (!string.IsNullOrEmpty(msg)) {
            // Log to file
            _bizHawkLogWriter.WriteLine(msg);
            _bizHawkLogWriter.Flush();
            if (isError) {
                Debug.LogWarning(msg);
            }
        }
    }

    
    // Send audio from the emulator to the AudioSource
    // (this method gets called by unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (channels != 2) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }
        // track how many samples we wanna request from bizhawk next time
        _audioSamplesNeeded += out_buffer.Length;

        // copy from the local running audio buffer into unity's buffer, convert short to float
        for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
            if (AudioBufferLength() > 0) {
                out_buffer[out_i] = NextAudioSample()/32767f;
            } else {
                Debug.LogWarning("Not enough bizhawk samples to fill unity audio buffer");
                break;
            }
        }
        Debug.Log($"Discarding {AudioBufferLength()} samples");
        ClearAudioBuffer(); // throw away any remaining samples [not sure why this works tbh]
    }

    // helper methods for circular audio buffer [should probably go in different class]
    private int AudioBufferLength() {
        return (_audioBufferEnd - _audioBufferStart + _audioBuffer.Length)%_audioBuffer.Length;
    }
    private void ClearAudioBuffer() {
        _audioBufferStart = 0;
        _audioBufferEnd = 0;
    }
    // consume a sample from the queue
    private short NextAudioSample() {
        short s = _audioBuffer[_audioBufferStart];
        _audioBufferStart = (_audioBufferStart + 1)%_audioBuffer.Length;
        return s;
    }
    private short GetAudioBufferAt(int i) {
        return _audioBuffer[(_audioBufferStart + i)%_audioBuffer.Length];
    }
}
}