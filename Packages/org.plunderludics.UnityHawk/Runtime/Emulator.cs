// This is the main user-facing component (ie MonoBehaviour)
// handles starting up and communicating with the BizHawk process

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
using UnityEngine.Serialization;

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
    [FormerlySerializedAs("writeToTexture")]
    public bool customRenderTexture = false;
    [ShowIf("customRenderTexture")]
    [Tooltip("The render texture to write to")]
    public RenderTexture renderTexture;
    // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    [Tooltip("If true, Unity will pass keyboard input to the emulator (only in play mode!). If false, BizHawk will accept input directly from the OS")]
    public bool passInputFromUnity = true;
    
    [Tooltip("If null, defaults to BasicInputProvider. Subclass InputProvider for custom behavior.")]
    [ShowIf("passInputFromUnity")]
    public InputProvider inputProvider = null;

    [Tooltip("If true, audio will be played via an attached AudioSource (may induce some latency). If false, BizHawk will play audio directly to the OS")]
    public bool captureEmulatorAudio = true;

    [Header("Files")]
    public bool useManualPathnames = true; // eventually should default to false but make it true for now to avoid breaking older projects
#if UNITY_EDITOR
// DefaultAsset is only defined in the editor. That's ok because useManualPathnames should always be true in the build (see BuildProcessing.cs)
    [HideIf("useManualPathnames")]
    public DefaultAsset romFile;
    [HideIf("useManualPathnames")]
    public DefaultAsset saveStateFile;
    [HideIf("useManualPathnames")]
    public DefaultAsset configFile;
    [HideIf("useManualPathnames")]
    public DefaultAsset luaScriptFile;
    [HideIf("useManualPathnames")]
    public DefaultAsset firmwareDirectory;
#endif // UNITY_EDITOR

    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAssetPath)
    [EnableIf("useManualPathnames")]
    public string romFileName = "Roms/mario.nes";
    [EnableIf("useManualPathnames")]
    public string saveStateFileName = ""; // Leave empty to boot clean
    [EnableIf("useManualPathnames")]
    public string configFileName = ""; // Leave empty for default config.ini
    [EnableIf("useManualPathnames")]
    public string luaScriptFileName;
    [EnableIf("useManualPathnames")]
    public string firmwareDirName = "Firmware"; // Firmware loaded from StreamingAssets/Firmware

    [Header("Development")]
    public bool showBizhawkGui = false;
    [ReadOnlyWhenPlaying]
    public new bool runInEditMode = false;
    [ShowIf("runInEditMode")]
    [ReadOnlyWhenPlaying]
    [Tooltip("Whether BizHawk will accept input when window is unfocused (in edit mode)")]
    public bool acceptBackgroundInput = true;

#if UNITY_EDITOR
    [HideIf("useManualPathnames")]
    [Tooltip("Default directory for BizHawk to save savestates (ignored in build)")]
    public DefaultAsset savestatesOutputDirectory;
#endif
    [EnableIf("useManualPathnames")]
    public string savestatesOutputDirName = "";

    [Foldout("Debug")]
    [ReadOnly, SerializeField] bool _initialized;
    public enum EmulatorStatus {
        Inactive,
        Started, // Underlying bizhawk has been started, but not rendering yet
        Running  // Bizhawk is running and sending textures [technically gets set when shared texture channel is open]
    }
    [Foldout("Debug")]
    [ReadOnly, SerializeField] EmulatorStatus _status;
    [Foldout("Debug")]
    [ReadOnly, SerializeField] int _currentFrame; // The frame index of the most-recently grabbed texture

    // Just for convenient reading from inspector:
    [Foldout("Debug")]
    [ReadOnly, SerializeField] Vector2Int _textureSize;
    [Foldout("Debug")]
    public bool writeBizhawkLogs = true;
    [ShowIf("writeBizhawkLogs")]
    [Foldout("Debug")]
    [ReadOnly, SerializeField] string bizhawkLogLocation;
    
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [Tooltip("Higher value means more audio latency. Lower value may cause crackles and pops")]
    public int audioBufferSurplus = (int)(2*44100*0.05);

    private static string bizhawkLogDirectory = "BizHawkLogs";
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    // Interface for other scripts to use
    public RenderTexture Texture => renderTexture;
    public bool IsRunning => _status == EmulatorStatus.Running; // is the _emuhawk process running (best guess, might be wrong)
    public int CurrentFrame => _currentFrame;

    Process _emuhawk;

    // Basically these are the params which, if changed, we want to reset the bizhawk process
    // Don't include the path params here, because then the process gets reset for every character typed/deleted
    struct BizhawkArgs {
        public bool passInputFromUnity;
        public bool captureEmulatorAudio;
        public bool acceptBackgroundInput;
        public bool showBizhawkGui;
    }
    BizhawkArgs _currentBizhawkArgs; // remember the params corresponding to the currently running process

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

    string _sharedKeyInputBufferName;
    SharedKeyInputBuffer _sharedKeyInputBuffer;
    
    string _sharedAnalogInputBufferName;
    SharedAnalogInputBuffer _sharedAnalogInputBuffer;

    Texture2D _bufferTexture;

    static int AudioBufferSize = (int)(2*44100*1); // Size of local audio buffer, 1 sec should be plenty

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

    [Button]
    private void Reset() {
        Deactivate();
        // Will be reactivated in Update on next frame
    }

#if UNITY_EDITOR
    // Set filename fields based on sample directory
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void PickRom() {
        string path = EditorUtility.OpenFilePanel("Sample", Application.streamingAssetsPath, "");
        if (!String.IsNullOrEmpty(path)) {
            if (Paths.IsSubPath(Application.streamingAssetsPath, path)) {
                // File is within StreamingAssets, use relative path
                romFileName = Paths.GetRelativePath(path, Application.streamingAssetsPath);
            } else {
                // Outside StreamingAssets, use absolute path
                romFileName = path;
            }
            Reset();
        }
    }

    // Set filename fields based on sample directory
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void PickSample() {
        string path = EditorUtility.OpenFilePanel("Sample", "", "");
        if (!String.IsNullOrEmpty(path)) {
            SetFromSample(path);
            Reset();
        }
    }
    
    // Set filename fields based on sample directory
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void ShowBizhawkLogInOS() {
        EditorUtility.RevealInFinder(bizhawkLogLocation);
    }
#endif

    ///// Public methods

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
        path = Paths.GetAssetPath(path);
        _apiCallBuffer.CallMethod("LoadState", path);
    }
    public void SaveState(string path) {
        path = Paths.GetAssetPath(path);
        _apiCallBuffer.CallMethod("SaveState", path);
    }
    public void LoadRom(string path) {
        path = Paths.GetAssetPath(path);
        _apiCallBuffer.CallMethod("LoadRom", path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();
        _status = EmulatorStatus.Started; // Not ready until new texture buffer is set up
    }
    public void FrameAdvance() {
        _apiCallBuffer.CallMethod("FrameAdvance", null);
    }

    ///// MonoBehaviour lifecycle

    void OnEnable()
    {
        // Debug.Log($"Emulator OnEnable");
#if UNITY_EDITOR
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _initialized = false;
        if (runInEditMode || Application.isPlaying) {
            Initialize();
        }
    }

    void Update() {
        _Update();
    }

    void OnDisable() {
        // Debug.Log($"Emulator OnDisable");
#if UNITY_EDITOR
        if (Undo.isProcessing) return; // OnDisable gets called after undo/redo, but ignore it
#endif
        if (_initialized) {
            Deactivate();
        }
    }

    ////// Core methods

    void Initialize() {
        // Debug.Log("Emulator Initialize");
        if (!customRenderTexture) renderTexture = null; // Clear texture so that it's forced to be reinitialized

        _status = EmulatorStatus.Inactive;

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

        // For input files, convert asset references to filenames
        // (because bizhawk needs the actual file on disk)
        if (!useManualPathnames) {
            SetFilenamesFromAssetReferences();
        }

        // Process filename args
        string romPath = Paths.GetAssetPath(romFileName);

        string saveStateFullPath = null;
        if (!string.IsNullOrEmpty(saveStateFileName)) {
            saveStateFullPath = Paths.GetAssetPath(saveStateFileName);
        }
        
        string configPath;
        if (string.IsNullOrEmpty(configFileName)) {
            configPath = Path.GetFullPath(Paths.defaultConfigPath);
        } else {
            configPath = Paths.GetAssetPath(configFileName);
        }
        
        string luaScriptFullPath = null;
        if (!string.IsNullOrEmpty(luaScriptFileName)) {
            luaScriptFullPath = Paths.GetAssetPath(luaScriptFileName);
        }

        string firmwareDirFullPath = null;
        if (!string.IsNullOrEmpty(firmwareDirName)) {
            firmwareDirFullPath = Paths.GetAssetPath(firmwareDirName);
        }

        if (!Application.isEditor) {
            // BizHawk tries to create the savestate dir if it doesn't exist, which can cause crashes
            // As a hacky solution in the build just point to StreamingAssets, this overrides any absolute path that might be in the config file
            savestatesOutputDirName = "";
        }
        // TODO should default to parent folder of rom if not given
        string savestatesOutputDirFullPath = Paths.GetAssetPath(savestatesOutputDirName);

        // Start EmuHawk.exe w args
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

        if (firmwareDirFullPath != null) {
            args.Add($"--firmware={firmwareDirFullPath}");
        }

        args.Add($"--savestates={savestatesOutputDirFullPath}");

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

        if (Application.isPlaying) {
            if (passInputFromUnity) {
                _sharedKeyInputBufferName = $"unityhawk-key-input-{randomNumber}";
                args.Add($"--read-key-input-from-shared-buffer={_sharedKeyInputBufferName}");

                _sharedAnalogInputBufferName = $"unityhawk-analog-input-{randomNumber}";
                args.Add($"--read-analog-input-from-shared-buffer={_sharedAnalogInputBufferName}");

                // default to BasicInputProvider (maps keys directly from keyboard)
                if (inputProvider == null) {
                    inputProvider = gameObject.AddComponent<BasicInputProvider>();
                }
            } else {
                // Always accept background input in play mode if not getting input from unity
                args.Add($"--accept-background-input");
            }
        } else if (runInEditMode) {
            if (acceptBackgroundInput) {
                args.Add($"--accept-background-input");
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
        _status = EmulatorStatus.Started;

        // init shared buffers
        _sharedTextureBuffer = new SharedTextureBuffer(_sharedTextureBufferName);
        _callMethodRpcBuffer = new CallMethodRpcBuffer(_callMethodRpcBufferName, CallRegisteredMethod);
        _apiCallBuffer = new ApiCallBuffer(_apiCallBufferName);
        if (passInputFromUnity) {
            _sharedKeyInputBuffer = new SharedKeyInputBuffer(_sharedKeyInputBufferName);
            _sharedAnalogInputBuffer = new SharedAnalogInputBuffer(_sharedAnalogInputBufferName);
        }
        if (captureEmulatorAudio) {
            _sharedAudioBuffer = new SharedAudioBuffer(_sharedAudioBufferName, getSamplesNeeded: () => {
                int nSamples = _audioSamplesNeeded;
                _audioSamplesNeeded = 0; // Reset audio sample counter each frame
                return nSamples;
            });
        }

        _currentBizhawkArgs = MakeBizhawkArgs();

        _initialized = true;
    }

    void _Update() {
        if (!Equals(_currentBizhawkArgs, MakeBizhawkArgs())) {
            // Params set in inspector have changed since the bizhawk process was started, needs restart
            Deactivate();
        }

        if (!Application.isPlaying && !runInEditMode) {
            if (_status != EmulatorStatus.Inactive) {
                Deactivate();
            }
            return;
        } else if (!_initialized) {
            Initialize();
        }

        // In headless mode, if bizhawk steals focus, steal it back
        // [Checking this every frame seems to be the only thing that works
        //  - fortunately for some reason it doesn't steal focus when clicking into a different application]
        // [Except this has a nasty side effect, in the editor in play mode if you try to open a unity modal window
        //  (e.g. the game view aspect ratio config) it gets closed. This is annoying but not sure how to fix]
        if (Application.isPlaying && !_targetMac && !showBizhawkGui && _emuhawk != null) {
            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            IntPtr bizhawkWindow = _emuhawk.MainWindowHandle;
            IntPtr focusedWindow = GetForegroundWindow();
            // Debug.Log($"unityWindow = {unityWindow}; bizhawkWindow = {bizhawkWindow}; focusedWindow = {focusedWindow}");
            if (focusedWindow != unityWindow) {
                // Debug.Log("refocusing unity window");
                ShowWindow(unityWindow, 5);
                SetForegroundWindow(unityWindow);
            }
        }

        if (_sharedTextureBuffer.IsOpen()) {
            _status = EmulatorStatus.Running; 
            UpdateTextureFromBuffer();
        } else {
            AttemptOpenBuffer(_sharedTextureBuffer);
        }

        if (passInputFromUnity && Application.isPlaying) {
            List<InputEvent> inputEvents = inputProvider.InputForFrame();
            if (_sharedKeyInputBuffer.IsOpen()) {
                WriteInputToBuffer(inputEvents);
            } else {
                AttemptOpenBuffer(_sharedKeyInputBuffer);
            }

            if (_sharedAnalogInputBuffer.IsOpen()) {
                WriteAxisValuesToBuffer(inputProvider.AxisValuesForFrame());
            } else {
                AttemptOpenBuffer(_sharedAnalogInputBuffer);
            }
        }

        if (!_callMethodRpcBuffer.IsOpen()) {
            AttemptOpenBuffer(_callMethodRpcBuffer);
        }
        if (!_apiCallBuffer.IsOpen()) {
            AttemptOpenBuffer(_apiCallBuffer);
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
            _sharedKeyInputBuffer.Write(bie);
        }
    }
    void WriteAxisValuesToBuffer(Dictionary<string, int> axisValues) {
        _sharedAnalogInputBuffer.Write(axisValues);
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
        int width = localTextureBuffer[_sharedTextureBuffer.Length - 3];
        int height = localTextureBuffer[_sharedTextureBuffer.Length - 2];
        _currentFrame = localTextureBuffer[_sharedTextureBuffer.Length - 1]; // frame index of this texture [hacky solution to sync issues]

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
        _status = EmulatorStatus.Inactive;

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
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _textureSize = new Vector2Int(width, height);
        _bufferTexture = new         Texture2D(width, height, textureFormat, false);

        if (!customRenderTexture)
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
            // Debug.Log($"Connected to {buf}");
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

    BizhawkArgs MakeBizhawkArgs() {
        return new BizhawkArgs {
            passInputFromUnity = passInputFromUnity,
            captureEmulatorAudio = captureEmulatorAudio,
            acceptBackgroundInput = acceptBackgroundInput,
            showBizhawkGui = showBizhawkGui
        };
    }
    
    // When using DefaultAsset references to set input file locations (ie when useManualPathnames == false)
    // Set the filename params based on the location of the DefaultAssets 
    // (A little awkward but this is a public method because it also needs to be called at build time by BuildProcessing.cs)
    public void SetFilenamesFromAssetReferences() {
        // This should only ever be called in the editor - useManualPathnames should always be true in the build
#if UNITY_EDITOR
        // Set filename params based on asset locations
        // [using absolute path is not really ideal here but ok for now]
        static string GetAssetPathName(DefaultAsset f) =>
            f ? Path.GetFullPath(AssetDatabase.GetAssetPath(f)) : "";
        romFileName = GetAssetPathName(romFile);
        saveStateFileName = GetAssetPathName(saveStateFile);
        configFileName = GetAssetPathName(configFile);
        luaScriptFileName = GetAssetPathName(luaScriptFile);
        firmwareDirName = GetAssetPathName(firmwareDirectory);
        savestatesOutputDirName = GetAssetPathName(savestatesOutputDirectory);
#else
        Debug.LogError("Something is wrong: useManualPathnames should always be enabled in the build");
#endif
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