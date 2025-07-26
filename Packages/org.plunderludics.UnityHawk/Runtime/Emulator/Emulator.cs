// This is the main user-facing MonoBehaviour
// handles starting up and communicating with the BizHawk process
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NaughtyAttributes;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Unity.Profiling;
using UnityEngine.Assertions;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine.Serialization;

namespace UnityHawk {

public enum EmulatorRenderMode {
    AttachedRenderer,
    ExternalRenderer,
    RenderTexture,
}

[ExecuteInEditMode]
public partial class Emulator : MonoBehaviour {
    /// the sample rate for bizhawk audio
    const double BizhawkSampleRate = 44100f;

    /// the file extension of savestates
    const string SavestateExtension = "savestate";

    /// the shader name for texture correction
    const string TextureCorrectionShaderName = "TextureCorrection";

    /// the texture format of the buffer texture
    const TextureFormat TextureFormat = UnityEngine.TextureFormat.BGRA32;

    /// the texture format of the render texture
    const RenderTextureFormat RenderTextureFormat = UnityEngine.RenderTextureFormat.BGRA32;

    /// the _MainTex shader property id
    static readonly int Shader_MainTex = Shader.PropertyToID("_MainTex");

    /// if we are targetting mac
    const bool IsTargetMac =
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        true;
#else
        false;
#endif

    [Tooltip("if the emulator launches on start")]
    public bool runOnEnable = true;

    [Header("Game")]
    public Savestate saveStateFile;

    /// .
    bool SaveStateFileIsNull => saveStateFile is null;

    [HideIf(nameof(SaveStateFileIsNull))]
    [Tooltip("get romfile automatically from the savestate")]
    public bool autoSelectRomFile = true;

    /// .
    //TODO: even if the rom is not in the db, in theory we can still match to a savestate using hash or filename? Not very concerned about this edge case though
    bool EnableRomFileSelection => !autoSelectRomFile || SaveStateFileIsNull || saveStateFile.RomInfo.NotInDatabase;

    [EnableIf(nameof(EnableRomFileSelection))]
    [Tooltip("a rom file")]
    public Rom romFile;

    ///// Rendering
    [Header("Rendering")]
    public EmulatorRenderMode renderMode;

    [ShowIf(nameof(renderMode), EmulatorRenderMode.ExternalRenderer)]
    public Renderer targetRenderer;

    [ShowIf(nameof(renderMode), EmulatorRenderMode.RenderTexture)]
    [Tooltip("render to a specific render texture instead of creating a default one")]
    public bool customRenderTexture = false;

    [EnableIf(nameof(customRenderTexture))]
    [Tooltip("the render texture to write to")]
    public RenderTexture renderTexture;

    ///// Input
    [Header("Input")]
    [Tooltip("If true, Unity will pass keyboard input to the emulator (only in play mode!). If false, BizHawk will accept input directly from the OS")]
    public bool passInputFromUnity = true;

    [Tooltip("If null and no InputProvider component attached, defaults to BasicInputProvider. Subclass InputProvider for custom behavior.")]
    [ShowIf(nameof(passInputFromUnity))]
    public InputProvider inputProvider = null;

    ///// Audio
    [Header("Audio")]
    [Tooltip("If true, audio will be played via an attached AudioSource (may induce some latency). If false, BizHawk will play audio directly to the OS")]
    public bool captureEmulatorAudio = false;

    [ShowIf(nameof(captureEmulatorAudio))]
    [SerializeField]
    AudioResampler audioResampler;

    ///// Additional Files
    [Header("Additional Files")]
    [Tooltip("a lua script file that will be loaded by the emulator (.lua)")]
    public LuaScript luaScriptFile;

    [Tooltip("a lua script file that will be loaded by the emulator (.lua)")]
    public RamWatch ramWatchFile;

    ///// Bizhawk Config
    [FormerlySerializedAs("configFile")]
    [Foldout("BizHawk Config")]
    [Tooltip("a BizHawk config file (.ini) that will be copied for this instance")]
    public Config baseConfigFile;

    ///// Development
    [Foldout("Development")]
    [Tooltip("if the bizhawk gui should be visible while running in unity editor")]
    public bool showBizhawkGuiInEditor = false;

    [Foldout("Development")]
    [ReadOnlyWhenPlaying]
    [Tooltip("whether bizhawk should run when in edit mode")]
    public new bool runInEditMode = false;

    [Foldout("Development")]

    [ShowIf(nameof(runInEditMode))]
    [ReadOnlyWhenPlaying]
    [Tooltip("Whether BizHawk will accept input when window is unfocused (in edit mode)")]
    public bool acceptBackgroundInput = true;

    [Foldout("Development")]
    [SerializeField] bool muteBizhawkInEditMode = true;

    ///// Debug
    [Foldout("Debug")]
    [SerializeField] UnityHawkConfig config;

    [Foldout("Debug")]
    [Tooltip("if the bizhawk gui should be visible in the build")]
    [SerializeField] bool showBizhawkGuiInBuild = false;

    [Foldout("Debug")]
    [Tooltip("Prevent BizHawk from popping up windows for warnings and errors; these will still appear in logs")]
    [SerializeField] bool suppressBizhawkPopups = true;

    [Foldout("Debug")]
    [SerializeField] bool writeBizhawkLogs = true;

    [ShowIf(nameof(writeBizhawkLogs))]
    [Foldout("Debug")]
    [ReadOnly, SerializeField] string bizhawkLogLocation;

    ///// State
    [Foldout("State")]
    [ReadOnly, SerializeField] EmulatorStatus _status;

    [Foldout("State")]
    [ReadOnly, SerializeField] int _currentFrame; // The frame index of the most-recently grabbed texture

    [Foldout("State")]
    [ReadOnly, SerializeField] string _systemId; // The system ID of the current core (e.g. "N64", "PSX", etc.)

    ///// props
    /// the bizhawk emulator process
    Process _emuhawk;

    /// when the emulator boots up
    public Action OnStarted;

    /// when the emulator starts running its game
    public Action OnRunning;

    /// Basically these are the params which, if changed, we want to reset the bizhawk process
    // Don't include the path params here, because then the process gets reset for every character typed/deleted
    struct BizhawkArgs {
        public Rom RomFile;
        public Savestate SaveStateFile;
        public Config ConfigFile;
        public LuaScript LuaScriptFile;
        public RamWatch RamWatchFile;
        public bool PassInputFromUnity;
        public bool CaptureEmulatorAudio;
        public bool AcceptBackgroundInput;
        public bool ShowBizhawkGui;
    }

    /// if the bizhawk gui should be shown
    bool ShowBizhawkGui =>
#if UNITY_EDITOR
        showBizhawkGuiInEditor
#else
        showBizhawkGuiInBuild
#endif
    ;

    /// the params corresponding to the currently running process
    /// (used for auto-restarting when params change, only in editor)
    BizhawkArgs _currentBizhawkArgs;

    /// Dictionary of registered methods that can be called from bizhawk lua
    readonly Dictionary<string, LuaCallback> _registeredLuaCallbacks = new();

    /// a set of all the called methods
    readonly HashSet<string> _invokedLuaCallbacks = new();

    /// Dictionaries to store callbacks for watched memory
    readonly Dictionary<int, ((long Addr, int Size, bool IsBigEndian, WatchType Type, string Domain) Key, Action<string> Callback)> _watchCallbacks = new(); // The actual key is the hashcode of Key (for opaque retrieval)

    /// buffer to send audio data to bizhawk
    SharedAudioBuffer _sharedAudioBuffer;

    /// buffer to receive lua callbacks from bizhawk
    CallMethodRpcBuffer _callMethodRpcBuffer;

    /// buffer for main-thread write-only bizhawk api calls
    ApiCommandBuffer _apiCommandBuffer;

    /// buffer for read/write non-main-thread bizhawk api calls
    // ApiCallRpcBuffer _apiCallRpcBuffer;

    /// buffer to send keyInputs to bizhawk
    SharedInputBuffer _sharedInputBuffer;

    /// buffer to receive screen texture from bizhawk
    SharedTextureBuffer _sharedTextureBuffer;

    /// the local texture buffer
    int[] _localTextureBuffer;

    /// the local texture
    Texture2D _localTexture;

    /// the material for texture correction
    Material _textureCorrectionMat;

    /// material used for rendering in edit mode
    Material _renderMaterial;

    /// the material property block for setting properties in the material
    MaterialPropertyBlock _materialProperties;

    /// writes bizhawk output into a file
    StreamWriter _bizHawkLogWriter;

    /// the time the emulator started running
    float _startedTime;
    Action _deferredForMainThread = null; // Pretty ugly solution for rpc handlers to get stuff to run on the main thread

    /// if the game can be running right now (application playing or runInEditMode)
    bool CanRun {
        get => Application.isPlaying || runInEditMode;
    }

    ///// user32 dlls
    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    ///// MonoBehaviour lifecycle
    // (These methods are public only for convenient testing)
    [Button]
    public void Reset() {
        Deactivate();
        Initialize();
    }

#if UNITY_EDITOR
    public void OnValidate() {
        if (!config) {
            config = (UnityHawkConfig)AssetDatabase.LoadAssetAtPath(
                Paths.defaultUnityHawkConfigPath,
                typeof(UnityHawkConfig));

            if (!config) {
                Debug.LogError("UnityHawkConfigDefault.asset not found", this);
            }
        }

        // Select rom file automatically based on save state (if possible)
        // TODO: could this be saved on the saveStateFile asset, rather than having to happen on validate?
        if (autoSelectRomFile && saveStateFile) {
            var roms = AssetDatabase.FindAssets("t:rom")
                .Select(guid => AssetDatabase.LoadAssetAtPath<Rom>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(rom => saveStateFile.MatchesRom(rom))
                .ToArray();

            if (roms.Any()) {
                var rom = roms.First();
                if (roms.Count() > 1) {
                    Debug.LogWarning($"Multiple roms found matching savestate {saveStateFile.name}, using first match: {rom}", this);
                }
                romFile = rom;
            } else {
                Debug.LogWarning($"No rom found matching savestate {saveStateFile.name}", this);
            }
        }

        // If emulator not running, set texture to savestate screenshot
        // TODO: why is this happening only on validate
        // should this even be reiniting the texture if the dimensions are the same?
        if (!IsRunning && saveStateFile?.Screenshot is not null) {
            InitTextures(saveStateFile.Screenshot.width, saveStateFile.Screenshot.height);
        }

        if (Status != EmulatorStatus.Inactive) {
            if (!Equals(_currentBizhawkArgs, MakeBizhawkArgs())) {
                // Bizhawk params have changed since bizhawk process was started, needs restart
                Reset();
            }

            if (!CanRun) {
                Deactivate();
            }
        }

        if (!Application.isPlaying && runInEditMode && Status == EmulatorStatus.Inactive) {
            // In edit mode, initialize the emulator if it is not already running
            Initialize();
        }
    }
#endif

    public void OnEnable() {
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _textureCorrectionMat = new Material(Resources.Load<Shader>(TextureCorrectionShaderName));
        _materialProperties = new MaterialPropertyBlock();

        if (CanRun && runOnEnable && Status == EmulatorStatus.Inactive) {
            Initialize();
        }
    }

    public void OnDisable() {
        // Debug.Log($"Emulator OnDisable");
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnDisable gets called after undo/redo, but ignore it
#endif

        if (Status != EmulatorStatus.Inactive) {
            Deactivate();
        }
    }

    public void OnDestroy() {
        OnStarted = null;
        OnRunning = null;
    }

    public void Update() {
        _Update();
    }

    ////// Core methods

    System.Threading.Thread initThread;
    public void Initialize() {
        // Debug.Log("Emulator Initialize");
        // Pre-process inspector params
        if (renderMode == EmulatorRenderMode.AttachedRenderer || renderMode == EmulatorRenderMode.ExternalRenderer) {
            if (renderMode == EmulatorRenderMode.AttachedRenderer) {
                targetRenderer = GetComponent<Renderer>();
            }

            if (!targetRenderer) {
                Debug.LogWarning("No Renderer attached, will not display emulator graphics", this);
            }
        }

        if (customRenderTexture) {
            if (renderTexture == null) {
                Debug.LogWarning("customRenderTexture is enabled but no RenderTexture is set, will create a default one", this);
            }
        } else {
            // Clear texture so that it's forced to be reinitialized
            renderTexture = null;
        }

        if (Application.isPlaying) {    
            // default to BasicInputProvider (uses preset default keymapping)
            if (!inputProvider) {
                if (!(inputProvider = GetComponent<InputProvider>())) {
                    // TODO can't do this during onvalidate, urgh
                    inputProvider = gameObject.AddComponent<BasicInputProvider>();
                }
            }
        }

        if (captureEmulatorAudio) {
            if (!GetComponent<AudioSource>()) {
                Debug.LogWarning("captureEmulatorAudio is enabled but no AudioSource is attached, will not play audio", this);
            }

            if (runInEditMode && !Application.isPlaying) {
                Debug.LogWarning("Emulator audio cannot be captured in edit mode", this);
            } else {
                if (audioResampler == null) {
                    audioResampler = new();
                }

                audioResampler.Init(BizhawkSampleRate/AudioSettings.outputSampleRate);
            }
        }
        bool shareAudio = captureEmulatorAudio && Application.isPlaying;

        // Compute file paths (since Paths.GetAssetPath() can only run on main thread)
        string romPath;
        // add rom path
        if (romFile) {
            romPath = Paths.GetAssetPath(romFile);
        } else {
            Debug.LogError("No rom file set, cannot start emulator", this);
            return;
        }

        // add config path
        string configPath;

        if (baseConfigFile) {
            configPath = Paths.GetAssetPath(baseConfigFile);
            // Debug.Log($"[emulator] found config at {configPath}");
        } else {
            configPath = Path.GetFullPath(Paths.defaultBizhawkConfigPath);
            // Debug.Log($"[emulator] {name} using default config file at {configPath}");
        }

        string saveStatePath = saveStateFile ? Paths.GetAssetPath(saveStateFile) : null;
        string ramWatchPath = ramWatchFile ? Paths.GetAssetPath(ramWatchFile) : null;
        string luaScriptPath = luaScriptFile ? Paths.GetAssetPath(luaScriptFile) : null;

        // Setup logger
        // Redirect bizhawk output + error into a log file
        if (writeBizhawkLogs) {
            var logFileName = $"{name}-{GetInstanceID()}.log";
            var logPath = config.BizHawkLogsPath;
            Directory.CreateDirectory(logPath);
            bizhawkLogLocation = Path.Combine(logPath, logFileName);

            _bizHawkLogWriter?.Dispose();
            _bizHawkLogWriter = new(bizhawkLogLocation);
        }

        // Run _StartBizhawk in a separate thread to avoid blocking the main thread
        bool applicationIsPlaying = Application.isPlaying;
        initThread = new (() => _StartBizhawk(applicationIsPlaying, romPath, configPath, saveStatePath, ramWatchPath, luaScriptPath, shareAudio));
        initThread.IsBackground = true;
        initThread.Start();
    }

    static readonly ProfilerMarker StartBizhawk = new ("Emulator.StartBizhawk");
    void _StartBizhawk(
        bool applicationIsPlaying,
        string romPath,
        string configPath,
        string saveStatePath,
        string ramWatchPath,
        string luaScriptPath,
        bool shareAudio
    ) {
        // TODO: The separation between Initialize and _StartBizhawk is sort of messy,
        // mainly just moving anything that has to run on the main thread into Initialize
        // Would probably be better to make _StartBizhawk as small as possible, just the config loading and the process start

        StartBizhawk.Begin();

        if (Status != EmulatorStatus.Inactive) {
            // TODO: should we deactivate here and reinitialize?
            return;
        }

        // get a random number to identify the buffers
        var guid = new System.Random().Next();

        _currentBizhawkArgs = MakeBizhawkArgs();

        Status = EmulatorStatus.Inactive;
        _systemId = null;

        // If using referenced assets then first map those assets to filenames
        // (Bizhawk requires a path to a real file on disk)

        // Start EmuHawk.exe w args
        var exePath = Path.GetFullPath(Paths.emuhawkExePath);
        Process process = new Process();
        process.StartInfo.UseShellExecute = false;
        var args = process.StartInfo.ArgumentList;
        if (IsTargetMac) {
            // Doesn't really work yet, need to make some more changes in the bizhawk executable
            process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Paths.dllDir;
            process.StartInfo.EnvironmentVariables["MONO_PATH"] = Paths.dllDir;
            process.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono";
            if (ShowBizhawkGui) {
                Debug.LogWarning("'Show Bizhawk Gui' is not supported on Mac'", this);
            }
            args.Add(exePath);
        } else {
            // Windows
            process.StartInfo.FileName = exePath;
            process.StartInfo.UseShellExecute = false;
        }

        // add rom path
        Assert.IsTrue(romPath != null, "romPath must not be null");
        args.Add(romPath);
        string workingDir = Path.GetDirectoryName(romPath);

        var bizConfig = ConfigService.Load(configPath);

        // create a temporary file for this config
        string tempConfigPath = Path.GetFullPath($"{Path.GetTempPath()}/unityhawk-config-{guid}.ini");

        bizConfig.SoundVolume = Volume;
        bizConfig.StartPaused = IsPaused;
        bizConfig.SoundEnabled = !IsMuted;
        bizConfig.SpeedPercent = SpeedPercent;
        ConfigService.Save(tempConfigPath, bizConfig);

        args.Add($"--config={tempConfigPath}");

        // add save state path
        if (saveStatePath != null) {
            args.Add($"--load-state={saveStatePath}");
        }

        // add ram watch file
        if (ramWatchPath != null) {
            args.Add($"--ram-watch-file={ramWatchPath}");
        }

        // add lua script file
        if (luaScriptPath != null) {
            args.Add($"--lua={luaScriptPath}");
        }

        // Save savestates with extension .savestate instead of .State, this is because Unity treats .State as some other kind of asset
        args.Add($"--savestate-extension={SavestateExtension}");

        // set savestates output dir
        // (default to application directory when not provided)
        var saveStatesOutputPath = GetOrCreateDirectory(config.SavestatesOutputPath) ?? workingDir;
        args.Add($"--savestates={saveStatesOutputPath}");

        // add firmware
        args.Add($"--firmware={Path.Combine(Application.streamingAssetsPath, config.FirmwarePath)}");

        // set ramwatch output dir
        var ramWatchOutputDirPath = GetOrCreateDirectory(config.RamWatchOutputPath) ?? workingDir;
        args.Add($"--save-ram-watch={ramWatchOutputDirPath}");

        if (!ShowBizhawkGui) {
            args.Add("--headless");
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        List<string> userData = new(); // Userdata args get used by UnityHawk external tool

        // add buffers
        // create & register sharedTextureBuffer
        var sharedTextureBufferName = $"texture-{guid}";
        userData.Add($"{Args.TextureBuffer}:{sharedTextureBufferName}");
        _sharedTextureBuffer = new SharedTextureBuffer(sharedTextureBufferName);

        // create & register callbacks rpc (used for lua callbacks and also the Watch memory api)
        var callMethodRpcBufferName = $"call-method-{guid}";
        userData.Add($"{Args.CallMethodRpc}:{callMethodRpcBufferName}");
        _callMethodRpcBuffer = new CallMethodRpcBuffer(callMethodRpcBufferName, ProcessRpcCallback);

        // create & register api call buffers
        var apiCommandBufferName = $"api-command-{guid}";
        userData.Add($"{Args.ApiCommandBuffer}:{apiCommandBufferName}");
        _apiCommandBuffer = new ApiCommandBuffer(apiCommandBufferName);

        // var apiCallRpcBufferName = $"api-call-rpc-{guid}";
        // userData.Add($"{Args.ApiCallRpc}:{apiCallRpcBufferName}");
        // _apiCallRpcBuffer = new ApiCallRpcBuffer(apiCallRpcBufferName);

        // create & register audio buffer
        if (shareAudio) {
            var sharedAudioBufferName = $"audio-{guid}";
            userData.Add($"{Args.AudioRpc}:{sharedAudioBufferName}");
            _sharedAudioBuffer = new SharedAudioBuffer(sharedAudioBufferName);
        }

        if (muteBizhawkInEditMode && !applicationIsPlaying) {
            args.Add("--mute=true");
        }

        // create & register input buffers
        if (applicationIsPlaying) {
            if (passInputFromUnity) {
                var sharedInputBufferName = $"input-{guid}";
                // args.Add($"--read-input-from-shared-buffer={sharedKeyInputBufferName}");
                userData.Add($"{Args.InputBuffer}:{sharedInputBufferName}");
                _sharedInputBuffer = new SharedInputBuffer(sharedInputBufferName);
                args.Add($"--accept-background-input=false");
            } else {
                // Always accept background input in play mode if not getting input from unity (otherwise would be no input at all)
                args.Add($"--accept-background-input=true");
            }
        } else if (runInEditMode) {
            args.Add($"--accept-background-input={(acceptBackgroundInput ? "true" : "false")}");
        }

        if (suppressBizhawkPopups) {
            args.Add("--suppress-popups"); // Don't pop up windows for messages/exceptions (they will still appear in the logs)
        }

        if (userData.Count > 0) args.Add($"--userdata={string.Join(";", userData)}"); // Userdata args get used by UnityHawk external tool

        args.Add("--open-ext-tool-dll=UnityHawk"); // Open unityhawk external tool
        args.Add($"--ext-tools-dir={Path.GetFullPath(Paths.externalToolsDir)}"); // Has to be set since not running from the bizhawk directory

        if (writeBizhawkLogs) {
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.OutputDataReceived += (sender, e) => LogBizHawk(sender, e, false);
            process.ErrorDataReceived += (sender, e) => LogBizHawk(sender, e, true);
        }

        // Debug.Log($"[unity-hawk] {exePath} {string.Join(' ', args)}");

        process.Start();
        _emuhawk = process; // _emuhawk is non-null only after the process has started
        if (writeBizhawkLogs) {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        _deferredForMainThread += () => {
            Status = EmulatorStatus.Started;
            _startedTime = Time.realtimeSinceStartup;
        };

        StartBizhawk.End();

        return;

        // creates a folder on the specified path, if any
        static string GetOrCreateDirectory(string path) {
            var s = path;
            if (string.IsNullOrEmpty(s)) {
                return null;
            }

            s = Paths.GetFullPath(s);
            if (!Directory.Exists(s)) {
                Directory.CreateDirectory(s);
            }

            return s;
        }
    }

    void _Update() {
        _deferredForMainThread?.Invoke();
        _deferredForMainThread = null;

        if (Status == EmulatorStatus.Inactive) {
            return;
        }

        // In headless mode, if bizhawk steals focus, steal it back
        // [Checking this every frame seems to be the only thing that works
        //  - fortunately for some reason it doesn't steal focus when clicking into a different application]
        // [Except this has a nasty side effect, in the editor in play mode if you try to open a unity modal window
        //  (e.g. the game view aspect ratio config) it gets closed. To avoid this only do the check in the first 5 seconds after starting up]
        if (Time.realtimeSinceStartup - _startedTime < 5f && Application.isPlaying && !IsTargetMac && !ShowBizhawkGui && _emuhawk != null) {
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

        if (passInputFromUnity && Application.isPlaying) {
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

        if (!_apiCommandBuffer.IsOpen()) {
            AttemptOpenBuffer(_apiCommandBuffer);
        }

        if (captureEmulatorAudio && Application.isPlaying) {
            if (_sharedAudioBuffer.IsOpen()) {
                if (Status == EmulatorStatus.Running) {
                    short[] samples = _sharedAudioBuffer.GetSamples();
                    // Updating audio before the emulator is actually running messes up the resampling algorithm
                    audioResampler.PushSamples(samples);
                }
            } else {
                AttemptOpenBuffer(_sharedAudioBuffer);
            }
        }

        // texture buffer only exists if there's a game running
        if (_sharedTextureBuffer.IsOpen()) {
            UpdateTextureFromBuffer();
        } else {
            AttemptOpenBuffer(_sharedTextureBuffer);
        }

        if (_emuhawk != null && _emuhawk.HasExited) {
            Deactivate();
            // TODO: maybe we want an option to not restart bizhawk here?
            Debug.LogWarning("EmuHawk process was unexpectedly killed, restarting", this);
            Initialize();
        }
    }

    #if UNITY_EDITOR
        [Button]
        private void ShowBizhawkLogInOS() {
            EditorUtility.RevealInFinder(bizhawkLogLocation);
        }
    #endif

    void WriteInputToBuffer(List<InputEvent> inputEvents) {
        // Get input from inputProvider, serialize and write to the shared memory
        foreach (InputEvent ie in inputEvents) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            Plunderludics.UnityHawk.Shared.InputEvent bie = ConvertInput.ToBizHawk(ie);
            _sharedInputBuffer.Write(bie);
        }
    }

    void UpdateTextureFromBuffer() {
        if (_localTextureBuffer == null || _localTextureBuffer.Length != _sharedTextureBuffer.PixelDataLength) {
            _localTextureBuffer = new int[_sharedTextureBuffer.PixelDataLength];
        }

        Assert.IsTrue(_sharedTextureBuffer != null && _sharedTextureBuffer.IsOpen());

        // Get the texture buffer and dimensions from BizHawk via shared memory
        int size = _sharedTextureBuffer.Length;

        int width = _sharedTextureBuffer.Width;
        int height = _sharedTextureBuffer.Height;
        _currentFrame = _sharedTextureBuffer.Frame; // TODO: only copy pixel data if the frame has changed
        
        if (width <= 0 || height <= 0) {
            // Width and height are 0 for a few frames after the emulator starts up
            // - presumably the texture buffer is open but bizhawk hasn't sent any data yet
            return;
        }

        _sharedTextureBuffer.CopyPixelsTo(_localTextureBuffer);

        bool noTextures = !_localTexture || !renderTexture;

        int bWidth = _localTexture?.width ?? 0;
        int bHeight = _localTexture?.height ?? 0;
        bool newDimensions = bWidth != width || bHeight != height;

        if (newDimensions) {
            // Debug.Log($"new width and height received : {width} x {height} (was {bWidth}x{bHeight})");
        }

        // resize textures if necessary
        if (noTextures || newDimensions) {
            InitTextures(width, height);
        }

        int bSize = _localTexture!.width * _localTexture.height;
        if (bSize == 0) {
            return;
        }

        if (bSize > size) {
            Debug.LogWarning($"emulator: buffer bigger than received size {bSize} > {size}", this);
            return;
        }

        try {
            _localTexture.SetPixelData(_localTextureBuffer, 0);
            _localTexture.Apply(/*updateMipmaps: false*/);
        } catch (Exception e) {
            Debug.LogError($"{e}", this);
        }

        // Correct issues with the texture by applying a shader and blitting to a separate render texture:
        Graphics.Blit(_localTexture, renderTexture, _textureCorrectionMat, 0);
    }

    void Deactivate() {
        // Debug.Log("Emulator Deactivate");

        if (_emuhawk != null && !_emuhawk.HasExited) {
            // Kill the _emuhawk process
            _emuhawk.Kill();
            _emuhawk = null;
        }

        _bizHawkLogWriter?.Close();


        foreach (ISharedBuffer buf in new ISharedBuffer[] {
            _sharedTextureBuffer,
            _sharedAudioBuffer,
            _callMethodRpcBuffer,
            _apiCommandBuffer,
        }) {
            if (buf != null && buf.IsOpen()) {
                buf.Close();
            }
        }
        Status = EmulatorStatus.Inactive;
    }

    /// Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        // Debug.Log($"[emulator] creating new textures with dimensions {width}x{height}");

        // TODO: cache textures
        _localTexture = new Texture2D(width, height, TextureFormat, false);

        if (!customRenderTexture || renderTexture == null) {
            renderTexture = new RenderTexture(width, height, depth:0, format:RenderTextureFormat);
            renderTexture.name = name;
        }

        if (_materialProperties == null) {
            _materialProperties = new();
        }

        if (targetRenderer) {
            _materialProperties.SetTexture(Shader_MainTex, Texture);
            targetRenderer.SetPropertyBlock(_materialProperties);
        }
    }

    /// Send audio from the emulator to the AudioSource
    /// (this method gets called by Unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] outBuffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (_sharedAudioBuffer == null || !_sharedAudioBuffer.IsOpen()) return;
        if (Status != EmulatorStatus.Running) return;

        audioResampler.GetSamples(outBuffer, channels);
    }

    /// try opening a shared buffer
    void AttemptOpenBuffer(ISharedBuffer buf) {
        // Debug.Log($"Attempting to open buffer {buf} ({buf.GetType().Name})");
        try {
            buf.Open();
            // Debug.Log($"Connected to {buf}");
        } catch (FileNotFoundException) {
        }
    }

    void ProcessRpcCallback(string callbackName, string argString, out string returnString) {
        // Debug.Log($"Rpc callback from bizhawk: {callbackName}({argString})");

        // This is either a lua callback, or a 'special' bizhawk->unity call
        // (Should probably use a separate buffer but they share one for now)
        if (SpecialCommands.All.Contains(callbackName)) {
            returnString = ""; // Ignored on bizhawk side anyway
            switch (callbackName) {
                case SpecialCommands.ReceiveWatchedValue:
                    // args format: $"{addr},{size},{isBigEndian},{type},{domain},{value}";
                    var args = argString.Split(',');
                    if (args.Length != 6)
                    {
                        Debug.LogWarning($"Expected 6 arguments for callback '{callbackName}', but got {args.Length}: {argString}", this);
                        returnString = "";
                        break;
                    }
                    long addr = long.Parse(args[0]);
                    int size = int.Parse(args[1]);
                    bool isBigEndian = bool.Parse(args[2]);
                    WatchType type = Enum.Parse<WatchType>(args[3]);
                    string domain = args[4].Length > 0 ? args[4] : null; // domain is optional
                    string value = args[5];

                    var key = (addr, size, isBigEndian, type, domain);
                    var id = key.GetHashCode();
                    if (_watchCallbacks.TryGetValue(id, out var v)) {
                        var callback = v.Callback;
                        v.Callback(value);
                    } else {
                        Debug.LogWarning($"Bizhawk tried to call a watch callback for key {key} but no callback was registered", this);
                    }
                    break;
                case SpecialCommands.OnRomLoaded:
                    // args: $"{systemID}"
                    _systemId = argString;
                    _deferredForMainThread += () => Status = EmulatorStatus.Running; // This is where the emulator is considered running
                    // (At this point I think we can be confident all the buffers should be open)
                    break;
                default:
                    Debug.LogWarning($"Bizhawk tried to call unknown special method {callbackName}", this);
                    break;
            }
        } else {
            // This is a lua callback
            // call corresponding method
            returnString = "";
            var exists = _registeredLuaCallbacks.TryGetValue(callbackName, out var callback);
            if (exists) {
                returnString = callback(argString);
            }

            // add to set of called methods to not spam this warning every frame
            if (!exists && !_invokedLuaCallbacks.Contains(callbackName)){
                Debug.LogWarning($"Tried to call a method named {callbackName} from lua but none was registered", this);
            }

            _invokedLuaCallbacks.Add(callbackName);
        }
    }

    /// get logs from bizhawk
    void LogBizHawk(object sender, DataReceivedEventArgs e, bool isError) {
        string msg = e.Data;
        if (!string.IsNullOrEmpty(msg)) {
            // Log to file
            _bizHawkLogWriter.WriteLine(msg);
            _bizHawkLogWriter.Flush();
            if (isError) {
                Debug.LogWarning(msg, this);
            }
        }
    }

    BizhawkArgs MakeBizhawkArgs() {
        return new BizhawkArgs {
            RomFile = romFile,
            SaveStateFile = saveStateFile,
            ConfigFile = baseConfigFile,
            LuaScriptFile = luaScriptFile,
            PassInputFromUnity = passInputFromUnity,
            CaptureEmulatorAudio = captureEmulatorAudio,
            AcceptBackgroundInput = acceptBackgroundInput,
            ShowBizhawkGui = ShowBizhawkGui
        };
    }
}

}