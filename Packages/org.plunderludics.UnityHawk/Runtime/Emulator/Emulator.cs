// This is the main user-facing MonoBehaviour
// handles starting up and communicating with the BizHawk process

using System;
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

using Plunderludics;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace UnityHawk {

[ExecuteInEditMode]
public partial class Emulator : MonoBehaviour
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
    [EnableIf("customRenderTexture")]
    [Tooltip("The render texture to write to")]
    public RenderTexture renderTexture;
    // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

    [Tooltip("If true, Unity will pass keyboard input to the emulator (only in play mode!). If false, BizHawk will accept input directly from the OS")]
    public bool passInputFromUnity = true;

    [Tooltip("If null and no InputProvider component attached, defaults to BasicInputProvider. Subclass InputProvider for custom behavior.")]
    [ShowIf("passInputFromUnity")]
    public InputProvider inputProvider = null;

    [Tooltip("If true, audio will be played via an attached AudioSource (may induce some latency). If false, BizHawk will play audio directly to the OS")]
    public bool captureEmulatorAudio = true;

    [Header("Files")]
#if UNITY_EDITOR
    [HideIf("useManualPathnames")]
    public Rom romFile;
    [HideIf("useManualPathnames")]
    public Savestate saveStateFile;
    [HideIf("useManualPathnames")]
    public Config configFile;
    [HideIf("useManualPathnames")]
    public LuaScript luaScriptFile;
    [HideIf("useManualPathnames")]
    public DefaultAsset firmwareDirectory;
#endif // UNITY_EDITOR

    [SerializeField, HideInInspector]
    bool _isEnabled = false; // hack to only show the forceCopyFilesToBuild field when component is inactive

    [HideIf("_isEnabled")]
    [Tooltip("Copy files into build even though Emulator is not active")]
    public bool forceCopyFilesToBuild = false;

    [Header("Development")]
    public bool showBizhawkGui = false;
    [ReadOnlyWhenPlaying]
    public new bool runInEditMode = false;
    [ShowIf("runInEditMode")]
    [ReadOnlyWhenPlaying]
    [Tooltip("Whether BizHawk will accept input when window is unfocused (in edit mode)")]
    public bool acceptBackgroundInput = true;

    public Action OnStarted;

    public Action OnRunning;

#if UNITY_EDITOR
    [HideIf("useManualPathnames")]
    public DefaultAsset ramWatchFile;
    [HideIf("useManualPathnames")]
    [Tooltip("Default directory for BizHawk to save savestates (ignored in build)")]
    public DefaultAsset savestatesOutputDirectory;
#endif

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

    // Options for using hardcoded filenames instead of Assets
    // (hide this in the debug section since it's not recommended and takes up space)
    [Tooltip("[Not recommended] Reference files by pathname (absolute or relative to StreamingAssets/) instead of as Unity assets. Useful if you need to reference files outside of the Assets directory")]
    [Foldout("Debug")]
    public bool useManualPathnames = false;
    // When useManualPathnames is false, the derived pathnames will show up in the inspector as readonly values
    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAssetPath)
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string romFileName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string saveStateFileName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string configFileName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string luaScriptFileName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string firmwareDirName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string savestatesOutputDirName;
    [Foldout("Debug")]
    [EnableIf("useManualPathnames")]
    public string ramWatchFileName;

    [Foldout("Debug")]
    [Tooltip("Prevent BizHawk from popping up windows for warnings and errors; these will still appear in logs")]
    public bool suppressBizhawkPopups = true;
    [Foldout("Debug")]
    public bool writeBizhawkLogs = true;
    [ShowIf("writeBizhawkLogs")]
    [Foldout("Debug")]
    [ReadOnly, SerializeField] string bizhawkLogLocation;

    private static string bizhawkLogDirectory = "BizHawkLogs";
    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    // Interface for other scripts to use
    public RenderTexture Texture => renderTexture;
    public bool IsRunning => Status == EmulatorStatus.Running; // is the _emuhawk process running (best guess, might be wrong)
    public EmulatorStatus Status {
        get => _status;
        private set {
            if (_status != value) {
                var raise = value switch {
                    EmulatorStatus.Started => OnStarted,
                    EmulatorStatus.Running => OnRunning,
                    _ => null,
                };

                raise?.Invoke();
            }
            _status = value;
        }
    }

    public int CurrentFrame => _currentFrame;

    Process _emuhawk;

    // Basically these are the params which, if changed, we want to reset the bizhawk process
    // Don't include the path params here, because then the process gets reset for every character typed/deleted
    struct BizhawkArgs {
#if UNITY_EDITOR
        public Rom romFile;
        public Savestate saveStateFile;
        public Config configFile;
        public LuaScript luaScriptFile;
        public DefaultAsset firmwareDirectory;
        public DefaultAsset savestatesOutputDirectory;
        public DefaultAsset ramWatchFile;
#endif
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

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

    Material _renderMaterial; // just used for rendering in edit mode

    StreamWriter _bizHawkLogWriter;

    private const string _savestateExtension = "savestate";

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

    private void OnValidate()
    {
        if (!useManualPathnames) {
            SetFilenamesFromAssetReferences();
        }
    }

#if UNITY_EDITOR
    // Set rom filename field using OS file picker
    [ShowIf("useManualPathnames")]
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

    // Set filename fields based on sample directory (using OS file picker)
    [ShowIf("useManualPathnames")]
    [Button(enabledMode: EButtonEnableMode.Editor)]
    private void PickSample() {
        string path = EditorUtility.OpenFilePanel("Sample", "", "");
        if (!String.IsNullOrEmpty(path)) {
            SetFromSample(path);
            Reset();
        }
    }

    [Button]
    private void ShowBizhawkLogInOS() {
        EditorUtility.RevealInFinder(bizhawkLogLocation);
    }
#endif
    ///// MonoBehaviour lifecycle

    // (These methods are public only for convenient testing)
    public void OnEnable()
    {
        _isEnabled = true;
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _initialized = false;

        if (!runInEditMode && (!Application.isPlaying || string.IsNullOrEmpty(romFileName))) return;

        TryInitialize();
    }

    public void Update() {
        _Update();
    }

    public void OnDisable() {
        // Debug.Log($"Emulator OnDisable");
        _isEnabled = false;
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnDisable gets called after undo/redo, but ignore it
#endif
        if (_initialized) {
            Deactivate();
        }
    }

    ////// Core methods

    bool TryInitialize() {
        // Debug.Log("Emulator Initialize");
        if (!customRenderTexture) renderTexture = null; // Clear texture so that it's forced to be reinitialized

        Status = EmulatorStatus.Inactive;

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

        // If using referenced assets then first map those assets to filenames
        // (Bizhawk requires a path to a real file on disk)

        // Process filename args
        if (string.IsNullOrEmpty(romFileName)) {
            if (!useManualPathnames) {
                SetFilenamesFromAssetReferences();
            }
        }

        if (string.IsNullOrEmpty(romFileName)) {
            Debug.LogWarning("Attempt to initialize emulator without a rom");
            return false;
        }

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

        string ramWatchFullPath = null;
        if (!string.IsNullOrEmpty(ramWatchFileName)) {
            ramWatchFullPath = Paths.GetAssetPath(ramWatchFileName);
        }

        if (!Application.isEditor) {
            // BizHawk tries to create the savestate dir if it doesn't exist, which can cause crashes
            // As a hacky solution in the build just default to the rom parent directory, this overrides any absolute path that might be in the config file
            savestatesOutputDirName = null;
        }
        string savestatesOutputDirFullPath;
        if (string.IsNullOrEmpty(savestatesOutputDirName)) {
            // Default to parent folder of rom file
            savestatesOutputDirFullPath = Path.GetDirectoryName(romPath);
        } else {
            savestatesOutputDirFullPath = Paths.GetAssetPath(savestatesOutputDirName);
        }

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
                    if (!(inputProvider = GetComponent<InputProvider>())) {
                        inputProvider = gameObject.AddComponent<BasicInputProvider>();
                    }
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

            if (runInEditMode && !Application.isPlaying) {
                Debug.LogWarning("captureEmulatorAudio is enabled but emulator audio cannot be captured in edit mode");
            }
        }

        if (saveStateFullPath != null) {
            args.Add($"--load-state={saveStateFullPath}");
        }

        if (luaScriptFullPath != null) {
            args.Add($"--lua={luaScriptFullPath}");
        }

        if (ramWatchFullPath != null) {
            args.Add($"--ram-watch-file={ramWatchFullPath}");
        }

        args.Add($"--config={configPath}");

        // Save savestates with extension .savestate instead of .State, this is because Unity treats .State as some other kind of asset
        args.Add($"--savestate-extension={_savestateExtension}");

        if (suppressBizhawkPopups) {
            args.Add("--suppress-popups"); // Don't pop up windows for messages/exceptions (they will still appear in the logs)
        }

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
        Status = EmulatorStatus.Started;

        // init shared buffers
        _sharedTextureBuffer = new SharedTextureBuffer(_sharedTextureBufferName);
        _callMethodRpcBuffer = new CallMethodRpcBuffer(_callMethodRpcBufferName, CallRegisteredMethod);
        _apiCallBuffer = new ApiCallBuffer(_apiCallBufferName);
        if (passInputFromUnity) {
            _sharedKeyInputBuffer = new SharedKeyInputBuffer(_sharedKeyInputBufferName);
            _sharedAnalogInputBuffer = new SharedAnalogInputBuffer(_sharedAnalogInputBufferName);
        }
        if (captureEmulatorAudio) {
            InitAudio();
        }

        _currentBizhawkArgs = MakeBizhawkArgs();

        _initialized = true;
        return true;
    }

    void _Update() {
        if (!Equals(_currentBizhawkArgs, MakeBizhawkArgs())) {
            // Params set in inspector have changed since the bizhawk process was started, needs restart
            Deactivate();
        }

        if (!Application.isPlaying && !runInEditMode) {
            if (Status != EmulatorStatus.Inactive) {
                Deactivate();
            }
            return;
        }

        if (!_initialized && !TryInitialize())
        {
            return;
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
            Status = EmulatorStatus.Running;
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
                UpdateAudio();
            } else {
                AttemptOpenBuffer(_sharedAudioBuffer);
            }
        }

        if (_emuhawk != null && _emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
            Deactivate();
        }
    }

    void WriteInputToBuffer(List<InputEvent> inputEvents) {
        // Get input from inputProvider, serialize and write to the shared memory
        foreach (InputEvent ie in inputEvents) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            var bie = ConvertInput.ToBizHawk(ie);
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
        Status = EmulatorStatus.Inactive;

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
            renderTexture.name = this.name;
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
#if UNITY_EDITOR
            romFile = romFile,
            saveStateFile = saveStateFile,
            configFile = configFile,
            luaScriptFile = luaScriptFile,
            firmwareDirectory = firmwareDirectory,
            savestatesOutputDirectory = savestatesOutputDirectory,
            ramWatchFile = ramWatchFile,
#endif
            passInputFromUnity = passInputFromUnity,
            captureEmulatorAudio = captureEmulatorAudio,
            acceptBackgroundInput = acceptBackgroundInput,
            showBizhawkGui = showBizhawkGui
        };
    }

    // When using Asset references to set input file locations (ie when useManualPathnames == false)
    // Set the filename params based on the location of the Assets
    // (A little awkward but this is a public method because it also needs to be called at build time by BuildProcessing.cs)
    public void SetFilenamesFromAssetReferences() {
        // This should only ever be called in the editor - useManualPathnames should always be true in the build
#if UNITY_EDITOR
        // Set filename params based on asset locations
        // [using absolute path is not really ideal here but ok for now]
        static string GetDefaultAssetPathName(DefaultAsset f) =>
            f ? Path.GetFullPath(AssetDatabase.GetAssetPath(f)) : "";

        static string GetBizhawkAssetPathName(BizhawkAsset f) =>
            f ? Path.GetFullPath(f.Path) : "";

        romFileName = GetBizhawkAssetPathName(romFile);
        saveStateFileName = GetBizhawkAssetPathName(saveStateFile);
        configFileName = GetBizhawkAssetPathName(configFile);
        luaScriptFileName = GetBizhawkAssetPathName(luaScriptFile);

        firmwareDirName = GetDefaultAssetPathName(firmwareDirectory);
        savestatesOutputDirName = GetDefaultAssetPathName(savestatesOutputDirectory);
        ramWatchFileName = GetDefaultAssetPathName(ramWatchFile);
#else
        Debug.LogError("Something is wrong: SetFilenamesFromAssetReferences should never be called from within a build");
#endif
    }
}
}