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

    [Tooltip("Write to an existing render texture rather than creating one automatically")]
    public bool writeToTexture = false;
    [ShowIf("writeToTexture")]
    [Tooltip("The render texture to write to")]
    public RenderTexture renderTexture;
    // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    [Tooltip("If true, Unity will pass keyboard input to the emulator. If false, BizHawk will get input directly from the OS")]
    public bool passInputFromUnity = true;
    
    [Tooltip("If null, defaults to BasicInputProvider. Subclass InputProvider for custom behavior.")]
    [ShowIf("passInputFromUnity")]
    public InputProvider inputProvider = null;

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
    public RenderTexture Texture => renderTexture;
    public bool IsRunning => _isRunning; // is the _emuhawk process running (best guess, might be wrong)

    Process _emuhawk;

    // Dictionary of registered methods that can be called from bizhawk lua
    // bytes-to-bytes only rn but some automatic de/serialization for different types would be nice
    public delegate string Method(string arg);
    Dictionary<string, Method> _registeredMethods;

    string _sharedAudioBufferName;
    // Audio needs two rpc buffers, one for Bizhawk to request 'samples needed' value from Unity,
    // one for Unity to request the audio buffer from Bizhawk
    SharedAudioBuffer _sharedAudioBuffer;

    string _callMethodRpcBufferName;
    CallMethodRpcBuffer _callMethodRpcBuffer;

    string _apiCallBufferName;
    ApiCallBuffer _apiCallBuffer;

    string _sharedTextureBufferName;
    SharedTextureBuffer _sharedTextureBuffer;

    string _sharedInputBufferName;
    SharedInputBuffer _sharedInputBuffer;

    Texture2D _bufferTexture;

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

    ///// Public methods

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

    ///// Bizhawk API methods
    ///// [should maybe move these into a Emulator.BizhawkApi subobject or similar]
    // For LoadState/SaveState/LoadRom, path should be relative to StreamingAssets (same as for rom/savestate/lua params in the inspector)
    // can also pass absolute path (but this will most likely break in build!)
    public void Pause() {
        _apiCallBuffer.CallMethod("Pause", null);
    }
    public void Unpause() {
        _apiCallBuffer.CallMethod("Unpause", null);
    }
    public void LoadState(string path) {
        path = GetAssetPath(path);
        _apiCallBuffer.CallMethod("LoadState", path);
    }
    public void SaveState(string path) {
        path = GetAssetPath(path);
        _apiCallBuffer.CallMethod("SaveState", path);
    }
    public void LoadRom(string path) {
        path = GetAssetPath(path);
        _apiCallBuffer.CallMethod("LoadRom", path);
    }
    public void FrameAdvance() {
        _apiCallBuffer.CallMethod("FrameAdvance", null);
    }

    ///// MonoBehaviour lifecycle

    void OnEnable()
    {
        // Debug.Log("Emulator OnEnable");
        _initialized = false;
        if (runInEditMode || Application.isPlaying) {
            Initialize();
        }
    }

    void Update() {
        _Update();
    }

    void OnDisable() {
        // Debug.Log("Emulator OnDisable");
        if (_initialized) {
            Deactivate();
        }
    }

    ////// Core methods

    void Initialize() {
        // Debug.Log("Emulator Initialize");
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

        // Init local audio buffer
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

        // start _emuhawk.exe w args
        string exePath = Path.GetFullPath(Paths.emuhawkExePath);
        _emuhawk = new Process();
        _emuhawk.StartInfo.UseShellExecute = false;
        var args = _emuhawk.StartInfo.ArgumentList;
        if (_targetMac) {
            // Doesn't really work yet, need to make some more changes in the bizhawk executable
            _emuhawk.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Paths.dllDir;
            _emuhawk.StartInfo.EnvironmentVariables["MONO_PATH"] = Paths.dllDir;
            _emuhawk.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono";
            if (showBizhawkGui) {
                Debug.LogWarning("'Show Bizhawk Gui' is not supported on Mac'");
            }
            args.Add(exePath);
        } else {
            // Windows
            _emuhawk.StartInfo.FileName = exePath;
            _emuhawk.StartInfo.UseShellExecute = false;
        }

        args.Add($"--firmware={GetAssetPath(firmwareDirName)}"); // could make this configurable but idk if that's really useful

        if (!showBizhawkGui) {
            args.Add("--headless");
            _emuhawk.StartInfo.CreateNoWindow = true;
            _emuhawk.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        System.Random random = new System.Random();
        int randomNumber = random.Next();

        _sharedTextureBufferName = $"unityhawk-texture-{randomNumber}";
        args.Add($"--write-texture-to-shared-buffer={_sharedTextureBufferName}");

        _callMethodRpcBufferName = $"unityhawk-callmethod-{randomNumber}";
        args.Add($"--unity-call-method-buffer={_callMethodRpcBufferName}");

        _apiCallBufferName = $"unityhawk-apicall-{randomNumber}";
        args.Add($"--api-call-method-buffer={_apiCallBufferName}");

        if (passInputFromUnity) {
            _sharedInputBufferName = $"unityhawk-input-{randomNumber}";
            args.Add($"--read-input-from-shared-buffer={_sharedInputBufferName}");

            // default to BasicInputProvider (maps keys directly from keyboard)
            if (inputProvider == null) {
                inputProvider = gameObject.AddComponent<BasicInputProvider>();
            }

            if (runInEditMode) {
                Debug.LogWarning("passInputFromUnity and runInEditMode are both enabled but input passing will not work in edit mode");
            }
        }

        if (captureEmulatorAudio) {
            _sharedAudioBufferName = $"unityhawk-audio-{randomNumber}";
            args.Add($"--share-audio-over-rpc-buffer={_sharedAudioBufferName}");

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

            _emuhawk.StartInfo.RedirectStandardOutput = true;
            _emuhawk.StartInfo.RedirectStandardError = true;
            _emuhawk.OutputDataReceived += new DataReceivedEventHandler((sender, e) => LogBizHawk(sender, e, false));
            _emuhawk.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => LogBizHawk(sender, e, true));
        }

        Debug.Log($"{exePath} {string.Join(' ', args)}");
        _emuhawk.Start();
        _emuhawk.BeginOutputReadLine();
        _emuhawk.BeginErrorReadLine();
        _isRunning = true;

        // init shared buffers
        _sharedTextureBuffer = new SharedTextureBuffer(_sharedTextureBufferName);
        _callMethodRpcBuffer = new CallMethodRpcBuffer(_callMethodRpcBufferName, CallRegisteredMethod);
        _apiCallBuffer = new ApiCallBuffer(_apiCallBufferName);
        if (passInputFromUnity) {        
            _sharedInputBuffer = new SharedInputBuffer(_sharedInputBufferName);
        }
        if (captureEmulatorAudio) {
            _sharedAudioBuffer = new SharedAudioBuffer(_sharedAudioBufferName, getSamplesNeeded: () => {
                int nSamples = _audioSamplesNeeded;
                _audioSamplesNeeded = 0; // Reset audio sample counter each frame
                return nSamples;
            });
        }

        _initialized = true;
    }

    void _Update() {
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
        if (!_targetMac && !showBizhawkGui && _emuhawk != null) {
            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            IntPtr bizhawkWindow = _emuhawk.MainWindowHandle;
            IntPtr focusedWindow = GetForegroundWindow();
            if (focusedWindow != unityWindow) {
            //    Debug.Log("refocusing unity window");
                ShowWindow(unityWindow, 5);
                SetForegroundWindow(unityWindow);
            }
        }

        if (_sharedTextureBuffer.IsOpen()) {
            UpdateTextureFromBuffer();
        } else {
            AttemptOpenBuffer(_sharedTextureBuffer);
        }

        if (passInputFromUnity) {
            List<InputEvent> inputEvents = inputProvider.InputForFrame();
            if (_sharedInputBuffer.IsOpen()) {
                WriteInputToBuffer(inputEvents);
            } else {
                AttemptOpenBuffer(_sharedInputBuffer);
            }
        }

        if (!_callMethodRpcBuffer.IsOpen()) {
            AttemptOpenBuffer(_callMethodRpcBuffer);
        }
        if (!_apiCallBuffer.IsOpen()) {
            AttemptOpenBuffer(_apiCallBuffer);
        }

        if (captureEmulatorAudio) {
            if (_sharedAudioBuffer.IsOpen()) {
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
                AttemptOpenBuffer(_sharedAudioBuffer);
            }
        }

        if (_emuhawk != null && _emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
            Deactivate();
        }
    }

    string GetUniqueId() {
        return "" + GetInstanceID();
    }

    void WriteInputToBuffer(List<InputEvent> inputEvents) {
        // Get input from inputProvider, serialize and write to the shared memory
        foreach (InputEvent ie in inputEvents) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            BizHawk.UnityHawk.InputEvent bie = ConvertInput.ToBizHawk(ie);
            _sharedInputBuffer.Write(bie);
        }
    }

    // Request audio samples (since last call) from Bizhawk, and store them into a buffer
    // to be played back in OnAudioFilterRead
    // [this really shouldn't be running every Unity frame, totally unnecessary]
    static readonly ProfilerMarker s_BizhawkRpcGetSamples = new ProfilerMarker("s_BizhawkRpcGetSamples");
    static readonly ProfilerMarker s_ReceivedBizhawkAudio = new ProfilerMarker("ReceivedBizhawkAudio");

    void CaptureBizhawkAudio() {
        short[] samples = _sharedAudioBuffer.GetSamples();
        if (samples == null) return; // This is fine, sometimes bizhawk just doesn't have any samples ready

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
        || renderTexture == null
        ||  _bufferTexture.width != width
        ||  _bufferTexture.height != height)) {
            InitTextures(width, height);
        }

        if (_bufferTexture) {
            _bufferTexture.SetPixelData(localTextureBuffer, 0);
            _bufferTexture.Apply(/*updateMipmaps: false*/);

            // Correct issues with the texture by applying a shader and blitting to a separate render texture:
            Graphics.Blit(_bufferTexture, renderTexture, _textureCorrectionMat, 0);
        }
    }

    void Deactivate() {
        // Debug.Log("Emulator Deactivate");

        _initialized = false;
        if (_bizHawkLogWriter != null) {
            _bizHawkLogWriter.Close();
        }
        if (_emuhawk != null && !_emuhawk.HasExited) {
            // Kill the _emuhawk process
            _emuhawk.Kill();
        }
        _isRunning = false;

        foreach (ISharedBuffer buf in new ISharedBuffer[] {
            _sharedTextureBuffer,
            _sharedAudioBuffer,
            _callMethodRpcBuffer,
            _apiCallBuffer
        }) {
            if (buf != null && buf.IsOpen()) {
                buf.Close();
            }
        } 

        // if (_sharedTextureBuffer != null) {
        //     _sharedTextureBuffer.Close();
        //     _sharedTextureBuffer = null;
        // }
        // if (_sharedInputBuffer != null) {
        //     _sharedInputBuffer.Close();
        //     _sharedInputBuffer = null;
        // }
        // if (_audioRpcBuffer != null) {
        //     _audioRpcBuffer.Dispose();
        //     _audioRpcBuffer = null;
        // }
        // if (_samplesNeededRpcBuffer != null) {
        //     _samplesNeededRpcBuffer.Dispose();
        //     _samplesNeededRpcBuffer = null;
        // }
        // if (_callMethodRpcBuffer != null) {
        //     _callMethodRpcBuffer.Dispose();
        //     _callMethodRpcBuffer = null;
        // }
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _bufferTexture = new         Texture2D(width, height, textureFormat, false);

        if (!writeToTexture)
        {
            renderTexture = new RenderTexture(width, height, depth:0, format:renderTextureFormat);
        }

        if (targetRenderer) {
            if (!Application.isPlaying) {
                // running in edit mode, manually make a clone of the renderer material
                // to avoid unity's default cloning behavior that spits out an error message
                // (probably still leaks materials into the scene but idk if it matters)
                if (_renderMaterial == null) {
                    _renderMaterial = Instantiate(targetRenderer.sharedMaterial);
                    _renderMaterial.name = targetRenderer.sharedMaterial.name;
                }
                _renderMaterial.mainTexture = renderTexture;

                targetRenderer.material = _renderMaterial;
            } else  {
                // play mode, just let unity clone the material the default way
                targetRenderer.material.mainTexture = renderTexture;
            }
        }
    }

    void AttemptOpenBuffer(ISharedBuffer buf) {
        try {
            buf.Open();
            Debug.Log($"Connected to {buf}");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
        }
    }

    string CallRegisteredMethod(string methodName, string argString) {
        // call corresponding method
        if (_registeredMethods != null && _registeredMethods.ContainsKey(methodName)) {
            return _registeredMethods[methodName](argString);
            // Debug.Log($"Calling registered method {methodName}");
        } else {
            Debug.LogWarning($"Tried to call a method named {methodName} from lua but none was registered");
            return null;
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
        if (!_sharedAudioBuffer.IsOpen()) return;
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