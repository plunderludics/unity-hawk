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
using System.Threading;
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

    [Tooltip("If true, Unity will pass keyboard input to the emulator. If false, BizHawk will get input directly from the OS")]
    public bool passInputFromUnity = true;
    [Tooltip("If true, audio will be played via an attached AudioSource (may induce some latency). If false, BizHawk will play audio directly to the OS")]
    public bool captureEmulatorAudio = true;

    [Header("Files")]
    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAssetPath)
    public string romFileName = "Roms/mario.nes";
    public string configFileName = ""; // Leave empty for default config.ini
    public string saveStateFileName = ""; // Leave empty to boot clean
    public string luaScriptFileName;

    private const string firmwareDirName = "Firmware"; // Firmware loaded from StreamingAssets/Firmware

    [Header("Development")]
    public new bool runInEditMode = false;
    public bool showBizhawkGui = false;
    [Header("Debug")]
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
    public bool IsRunning => _isRunning; // is the emuhawk process running (best guess, might be wrong)

    Process emuhawk;

    // Dictionary of registered methods that can be called from bizhawk lua
    // bytes-to-bytes only rn but some automatic de/serialization for different types would be nice
    public delegate string Method(string arg);
    Dictionary<string, Method> _registeredMethods;

    private string _audioRpcBufferName;
    // Audio needs two rpc buffers, one for Bizhawk to request 'samples needed' value from Unity,
    // one for Unity to request the audio buffer from Bizhawk
    RpcBuffer _audioRpcBuffer;
    RpcBuffer _samplesNeededRpcBuffer;
    
    private string _callMethodRpcBufferName;
    RpcBuffer _callMethodRpcBuffer;

    private string _sharedTextureBufferName;
    SharedArray<int> _sharedTextureBuffer;

    private IInputProvider inputProvider; // this is fixed for now but could be configurable in the future
    private string _sharedInputBufferName;
    CircularBuffer _sharedInputBuffer;

    Texture2D _bufferTexture;
    RenderTexture _renderTexture; // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    static int AudioBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(2*44100*0.05);
    // ^ This is the actual 'buffer' part - samples that are retained after passing audio to unity.
    // Smaller surplus -> less latency but more clicks & pops (when bizhawk fails to provide audio in time)
    // 50ms seems to be an ok compromise (but probably depends on host machine, users can configure if needed)
    short[] _audioBuffer; // circular buffer (queue) to locally store audio samples accumulated from the emulator
    int _audioBufferStart, _audioBufferEnd;
    int _audioSamplesNeeded; // track how many samples unity wants to consume

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

    Material _renderMaterial; // just used for rendering in edit mode

    StreamWriter _bizHawkLogWriter;

    // Track how many times we skip audio, log a warning if it's too much
    float _audioSkipCounter;
    float _acceptableSkipsPerSecond = 1f;

    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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

    // Register a method that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    public void RegisterMethod(string methodName, Method method)
    {
        if (_registeredMethods == null) {
            _registeredMethods = new Dictionary<string, Method>();
            // This will never get cleared when running in edit mode but maybe that's fine
        }
        _registeredMethods[methodName] = method;
    }

    void OnEnable()
    {
        Debug.Log("Emulator OnEnable");
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

        // In headless mode, if bizhawk steals focus, steal it back
        // [Checking this every frame seems to be the only thing that works
        //  - fortunately for some reason it doesn't steal focus when clicking into a different application]
        if (!showBizhawkGui && emuhawk != null) {
            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            IntPtr bizhawkWindow = emuhawk.MainWindowHandle;
            IntPtr focusedWindow = GetForegroundWindow();
            if (focusedWindow != unityWindow) {
            //    Debug.Log("refocusing unity window");
                ShowWindow(unityWindow, 5);
                SetForegroundWindow(unityWindow);
            }
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
            if (_audioRpcBuffer != null && _samplesNeededRpcBuffer != null) {
                // request audio buffer over rpc
                // Don't want to do this every frame so only do it if more samples are needed
                if (AudioBufferCount() < audioBufferSurplus) {
                    CaptureBizhawkAudio();
                }
                _audioSkipCounter += _acceptableSkipsPerSecond*Time.deltaTime;
                if (_audioSkipCounter < 0f) {
                    if (Time.realtimeSinceStartup > 5f) { // ignore the first few seconds while bizhawk is starting up
                        Debug.LogWarning("Suffering frequent audio drops (consider increasing audioBufferSurplus value)");
                    }
                    _audioSkipCounter = 0f;
                }
            } else {
                AttemptOpenAudioRpcBuffers();
            }
        }

        if (emuhawk != null && emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
            Deactivate();
        }
    }

    void OnDisable() {
        Debug.Log("Emulator OnDisable");
        if (_initialized) {
            Deactivate();
        }
    }

    void Initialize() {
        Debug.Log("Emulator Initialize");
        _isRunning = false;

        _audioSkipCounter = 0f;

        _textureCorrectionMat = new Material(Resources.Load<Shader>(textureCorrectionShaderName));

        if (useAttachedRenderer) {
            // Default to the attached Renderer component, if there is one
            targetRenderer = GetComponent<Renderer>();
            if (!targetRenderer) {
                Debug.LogWarning("No Renderer attached, will not display emulator graphics");
            }
        }

        if (captureEmulatorAudio && GetComponent<AudioSource>() == null) {
            Debug.LogWarning("captureEmulatorAudio is enabled but no AudioSource is attached, will not play audio");
        }

        // Init audio buffer
        _audioBuffer = new short[AudioBufferSize];
        _audioSamplesNeeded = 0;
        AudioBufferClear();

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

        if (!showBizhawkGui) {
            args.Add("--headless");
            emuhawk.StartInfo.CreateNoWindow = true;
            emuhawk.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        System.Random random = new System.Random();
        int randomNumber = random.Next();

        _sharedTextureBufferName = $"unityhawk-texture-{randomNumber}";
        args.Add($"--write-texture-to-shared-buffer={_sharedTextureBufferName}");

        _callMethodRpcBufferName = $"unityhawk-callmethod-{randomNumber}";
        args.Add($"--unity-call-method-buffer={_callMethodRpcBufferName}");

        if (passInputFromUnity) {
            _sharedInputBufferName = $"unityhawk-input-{randomNumber}";
            args.Add($"--read-input-from-shared-buffer={_sharedInputBufferName}");

            // for now use a fixed implementation of IInputProvider but
            // in principle could be configurable - easy to add input events programmatically, etc
            inputProvider = new InputProvider();

            if (runInEditMode) {
                Debug.LogWarning("passInputFromUnity and runInEditMode are both enabled but input passing will not work in edit mode");
            }
        }

        if (captureEmulatorAudio) {
            _audioRpcBufferName = $"unityhawk-audio-{randomNumber}";
            args.Add($"--share-audio-over-rpc-buffer={_audioRpcBufferName}");

            if (runInEditMode) {
                Debug.LogWarning("captureEmulatorAudio and runInEditMode are both enabled but emulator audio cannot be captured in edit mode");
            }
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
        _isRunning = true;

        AttemptOpenSharedTextureBuffer();
        AttemptOpenCallMethodRpcBuffer();
        if (passInputFromUnity) {
            AttemptOpenSharedInputBuffer();
        }
        if (captureEmulatorAudio) {
            AttemptOpenAudioRpcBuffers();
        }

        _initialized = true;
    }

    string GetUniqueId() {
        return "" + GetInstanceID();
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

    private byte[] Serialize(object obj)
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
    // [this really shouldn't be running every Unity frame, totally unnecessary]
    static readonly ProfilerMarker s_BizhawkRpcGetSamples = new ProfilerMarker("s_BizhawkRpcGetSamples");
    static readonly ProfilerMarker s_ReceivedBizhawkAudio = new ProfilerMarker("ReceivedBizhawkAudio");

    void CaptureBizhawkAudio() {
        s_BizhawkRpcGetSamples.Begin();

        RpcResponse response = _audioRpcBuffer.RemoteRequest(new byte[] {}, timeoutMs: 500);
        s_BizhawkRpcGetSamples.End();
        if (!response.Success) {
            // This happens sometimes, especially when audioCaptureFramerate is high, i have no idea why
            Debug.LogWarning("Rpc call to get audio from BizHawk failed for some reason");
            return;
        }

        // convert bytes into short
        byte[] bytes = response.Data;
        if (bytes == null || bytes.Length == 0) {
            // This is fine, sometimes bizhawk just doesn't have any samples ready
            return;
        }
        s_ReceivedBizhawkAudio.Begin();
        short[] samples = new short[bytes.Length/2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        // Debug.Log($"Got audio over rpc: {samples.Length} samples");
        // Debug.Log($"first = {samples[0]}; last = {samples[samples.Length-1]}");

        // Append samples to running audio buffer to be played back later
        lock (_audioBuffer) { // (lock since OnAudioFilterRead reads from the buffer in a different thread)
            // [Doing an Array.Copy here instead would probably be way faster but not a big deal]
            for (int i = 0; i < samples.Length; i++) {
                if (AudioBufferCount() == _audioBuffer.Length - 1) {
                    Debug.LogWarning("local audio buffer full, dropping samples");
                }
                AudioBufferEnqueue(samples[i]);
            }
        }
        s_ReceivedBizhawkAudio.End();
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
        Debug.Log("Emulator Deactivate");

        _initialized = false;
        if (_bizHawkLogWriter != null) {
            _bizHawkLogWriter.Close();
        }
        if (emuhawk != null && !emuhawk.HasExited) {
            // Kill the emuhawk process
            emuhawk.Kill();
        }
        _isRunning = false;
        if (_sharedTextureBuffer != null) {
            _sharedTextureBuffer.Close();
            _sharedTextureBuffer = null;
        }
        if (_sharedInputBuffer != null) {
            _sharedInputBuffer.Close();
            _sharedInputBuffer = null;
        }
        if (_audioRpcBuffer != null) {
            Debug.Log("Dispose audio buffer");
            _audioRpcBuffer.Dispose();
            _audioRpcBuffer = null;
        }
        if (_samplesNeededRpcBuffer != null) {
            _samplesNeededRpcBuffer.Dispose();
            _samplesNeededRpcBuffer = null;
        }
        if (_callMethodRpcBuffer != null) {
            _callMethodRpcBuffer.Dispose();
            _callMethodRpcBuffer = null;
        }
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

    void AttemptOpenCallMethodRpcBuffer() {
        try {
            _callMethodRpcBuffer = new (
                name: _callMethodRpcBufferName,
                (msgId, payload) => {
                    // Debug.Log($"callmethod rpc request {string.Join(", ", payload)}");
                    byte[] returnData;

                    // deserialize payload into method name and args (separated by 0)
                    int methodNameLength = System.Array.IndexOf(payload, (byte)0);
                    byte[] methodNameBytes = new byte[methodNameLength];
                    byte[] argBytes = new byte[payload.Length - methodNameLength - 1];
                    Array.Copy(
                        sourceArray: payload,
                        sourceIndex: 0,
                        destinationArray: methodNameBytes,
                        destinationIndex: 0,
                        length: methodNameLength);
                    Array.Copy(
                        sourceArray: payload,
                        sourceIndex: methodNameLength + 1,
                        destinationArray: argBytes,
                        destinationIndex: 0,
                        length: argBytes.Length);

                    string methodName = System.Text.Encoding.ASCII.GetString(methodNameBytes);
                    string argString = System.Text.Encoding.ASCII.GetString(argBytes);

                    // call corresponding method
                    if (_registeredMethods != null && _registeredMethods.ContainsKey(methodName)) {
                        string returnString = _registeredMethods[methodName](argString);
                        returnData = System.Text.Encoding.ASCII.GetBytes(returnString);
                        // Debug.Log($"Calling registered method {methodName}");
                    } else {
                        Debug.LogWarning($"Tried to call a method named {methodName} from lua but none was registered");
                        returnData = null;
                    }
                    return returnData;
                }
            );
            Debug.Log("Connected to callmethod rpc buffer");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }

    void AttemptOpenAudioRpcBuffers() {
        try {
            _audioRpcBuffer = new (name: _audioRpcBufferName);
            _samplesNeededRpcBuffer = new (
                name: _audioRpcBufferName+"-samples-needed", // [suffix must match UnityHawkSound.cs in BizHawk]
                (msgId, payload) => {
                    // This method should get called once after each emulated frame by bizhawk
                    // Returns int _audioSamplesNeeded as a 4 byte array
                    int nSamples = _audioSamplesNeeded;

                    // Debug.Log($"GetSamplesNeeded() RPC call: Returning {nSamples}");

                    _audioSamplesNeeded = 0; // Reset audio sample counter each frame

                    byte[] data = BitConverter.GetBytes(nSamples);
                    return data;
                }
            );
            Debug.Log("Connected to both audio and samplesNeeded rpc buffers");
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
    // (this method gets called by Unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (_audioRpcBuffer == null) return;
        if (channels != 2) {
            Debug.LogError("AudioSource must be set to 2 channels");
            return;
        }

        // track how many samples we wanna request from bizhawk next time
        _audioSamplesNeeded += out_buffer.Length;

        // copy from the local running audio buffer into unity's buffer, convert short to float
        lock (_audioBuffer) { // (lock since the buffer gets filled in a different thread)
            for (int out_i = 0; out_i < out_buffer.Length; out_i++) {
                if (AudioBufferCount() > 0) {
                    out_buffer[out_i] = AudioBufferDequeue()/32767f;
                } else {
                    // we didn't have enough bizhawk samples to fill the unity audio buffer
                    // log a warning if this happens frequently enough
                    _audioSkipCounter -= 1f;
                    break;
                }
            }
            // Clear buffer except for a small amount of samples leftover (as buffer against skips/pops)
            // (kind of a dumb way of doing this, could just reset _audioBufferEnd but whatever)
            while (AudioBufferCount() > audioBufferSurplus) {
                _ = AudioBufferDequeue();
            }
        }
    }

    // helper methods for circular audio buffer [should probably go in different class]
    private int AudioBufferCount() {
        return (_audioBufferEnd - _audioBufferStart + _audioBuffer.Length)%_audioBuffer.Length;
    }
    private void AudioBufferClear() {
        _audioBufferStart = 0;
        _audioBufferEnd = 0;
    }
    // consume a sample from the queue
    private short AudioBufferDequeue() {
        short s = _audioBuffer[_audioBufferStart];
        _audioBufferStart = (_audioBufferStart + 1)%_audioBuffer.Length;
        return s;
    }
    private void AudioBufferEnqueue(short x) {
        _audioBuffer[_audioBufferEnd] = x;
        _audioBufferEnd++;
        _audioBufferEnd %= _audioBuffer.Length;
    }
    private short GetAudioBufferAt(int i) {
        return _audioBuffer[(_audioBufferStart + i)%_audioBuffer.Length];
    }
}
}