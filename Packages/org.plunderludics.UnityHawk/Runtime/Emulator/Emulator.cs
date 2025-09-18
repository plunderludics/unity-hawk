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
using System.Threading;
using EditorBrowsable = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine.Serialization;

namespace UnityHawk {

/// <summary>
/// The component responsible for initializing,
/// running, and managing a single BizHawk emulator instance.
/// </summary>
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
    [Tooltip("Savestate file to load")]
    public Savestate saveStateFile;

    /// .
    bool SaveStateFileIsNull => saveStateFile is null;

    [HideIf(nameof(SaveStateFileIsNull))]
    [Tooltip("select rom file automatically based on savestate")]
    public bool autoSelectRomFile = true;

    /// .
    //TODO: even if the rom is not in the db, in theory we can still match to a savestate using hash or filename? Not very concerned about this edge case though
    bool EnableRomFileSelection => !autoSelectRomFile || SaveStateFileIsNull || saveStateFile.RomInfo.NotInDatabase;

    [EnableIf(nameof(EnableRomFileSelection))]
    [Tooltip("Rom file to run")]
    public Rom romFile;

    ///// Rendering
    public enum RenderMode {
        AttachedRenderer,
        ExternalRenderer,
        RenderTexture,
    }

    [Header("Rendering")]
    public RenderMode renderMode;

    [ShowIf(nameof(renderMode), RenderMode.ExternalRenderer)]
    public Renderer targetRenderer;

    [ShowIf(nameof(renderMode), RenderMode.RenderTexture)]
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
    [OnValueChanged(nameof(OnSetLogLevel))]
    [SerializeField] Logger.LogLevel logLevel = Logger.LogLevel.Warning;

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
    [ReadOnly, SerializeField] Status _status;

    [Foldout("State")]
    [ReadOnly, SerializeField] int _currentFrame; // The frame index of the most-recently grabbed texture

    [Foldout("State")]
    [ReadOnly, SerializeField] string _systemId; // The system ID of the current core (e.g. "N64", "PSX", etc.)

    ///// props
    /// the bizhawk emulator process
    Process _emuhawk;
    
    /// Helper method to check if the emulator process is alive
    bool IsEmuHawkProcessAlive {
        get {
            if (_emuhawk == null) return false;
            
            // Weird api, sometimes process is disposed which can't be checked other than via try-catch
            try {
                return !_emuhawk.HasExited;
            } catch (InvalidOperationException) {
                // Process is disposed or invalid
                return false;
            }
        }
    }

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

    /// Logger for unity-side logs
    Logger _logger;
    public Logger Logger => _logger ??= new(this, logLevel);

    /// the time the emulator started running
    float _startedTime;
    float SystemTime => (float)System.DateTime.Now.Ticks / System.TimeSpan.TicksPerSecond;

    /// for actions deferred to main thread Update call
    /// (used to make sure OnStarted and OnRunning actions get invoked on main thread)
    Action _deferredForMainThread = null;

    /// if the game should be running right now (application playing or runInEditMode)
    bool ShouldRun {
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
    // (These methods are public for testing purposes, but use the EditorBrowsable attribute to hide them from docs)
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void OnValidate() {
        _logger ??= Logger; // ensure logger is initialized
#if UNITY_EDITOR
        _logger.LogVerbose("OnValidate");

        if (!config) {
            config = (UnityHawkConfig)AssetDatabase.LoadAssetAtPath(
                Paths.defaultUnityHawkConfigPath,
                typeof(UnityHawkConfig)
            );

            if (!config) {
                _logger.LogError("UnityHawkConfigDefault.asset not found");
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
                    _logger.LogWarning($"Multiple roms found matching savestate {saveStateFile.name}, using first match: {rom}");
                }
                romFile = rom;
            } else {
                _logger.LogWarning($"No rom found matching savestate {saveStateFile.name}");
            }
        }

        // If emulator not running, set texture to savestate screenshot
        // TODO: why is this happening only on validate
        // should this even be reiniting the texture if the dimensions are the same?
        if (!IsRunning && saveStateFile?.Screenshot != null) {
            InitTextures(saveStateFile.Screenshot.width, saveStateFile.Screenshot.height);
        }
        
        if (gameObject.activeInHierarchy && enabled) {
            // GameObject and Emulator are active, so check if we need to start the bizhawk process

            if (CurrentStatus != Status.Inactive) {
                if (!Equals(_currentBizhawkArgs, MakeBizhawkArgs())) {
                    // Bizhawk params have changed since bizhawk process was started, needs restart
                    Restart();
                }

                if (!ShouldRun) {
                    Deactivate();
                }
            }

            // [use EditorApplication.isPlayingOrWillChangePlaymode instead of Application.isPlaying
            //  to avoid OnEnable call as play mode is being entered]
            if (!EditorApplication.isPlayingOrWillChangePlaymode && runInEditMode && CurrentStatus == Status.Inactive) {
                // In edit mode, initialize the emulator if it is not already running
                Initialize();
            }
        }
#endif
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void OnEnable() {
        _logger ??= Logger; // ensure logger is initialized
        _logger.LogVerbose("OnEnable");
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _textureCorrectionMat = new Material(Resources.Load<Shader>(TextureCorrectionShaderName));
        _materialProperties = new MaterialPropertyBlock();

#if UNITY_EDITOR
        // In Editor when entering play mode OnEnable gets called twice
        // - once immediately before play mode starts, and once after - don't start up the emulator in the first case
        if (EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying) {
            return;
        }
#endif

        if (ShouldRun && runOnEnable && CurrentStatus == Status.Inactive) {
            Initialize();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void OnDisable() {
        _logger.LogVerbose("OnDisable");
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnDisable gets called after undo/redo, but ignore it
#endif

        if (CurrentStatus != Status.Inactive) {
            Deactivate();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void OnDestroy() {
        OnStarted = null;
        OnRunning = null;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Update() {
        _Update();
    }

    ////// Core methods
 
    Thread _initThread;
    CancellationTokenSource _initThreadCancellationTokenSource;

    void Initialize() {
        _logger.LogVerbose("Emulator: Initialize");

        // Don't allow re-initializing if already initialized
        if (CurrentStatus != Status.Inactive) {
            // TODO: or should we reset in this case?
            _logger.LogError($"Emulator is already initialized (Status = {CurrentStatus}), ignoring Initialize() call");
            return;
        }

        // Pre-process inspector params
        if (renderMode == RenderMode.AttachedRenderer || renderMode == RenderMode.ExternalRenderer) {
            if (renderMode == RenderMode.AttachedRenderer) {
                targetRenderer = GetComponent<Renderer>();
            }

            if (!targetRenderer) {
                _logger.LogWarning("No Renderer attached, will not display emulator graphics");
            }
        }

        if (customRenderTexture) {
            if (renderTexture == null) {
                _logger.LogWarning("customRenderTexture is enabled but no RenderTexture is set, will create a default one");
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
                _logger.LogWarning("captureEmulatorAudio is enabled but no AudioSource is attached, will not play audio");
            }

            if (runInEditMode && !Application.isPlaying) {
                _logger.LogWarning("Emulator audio cannot be captured in edit mode");
            } else {
                if (audioResampler == null) {
                    audioResampler = new(_logger);
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
            _logger.LogError("No rom file set, cannot start emulator");
            return;
        }

        // add config path
        string configPath;

        if (baseConfigFile) {
            configPath = Paths.GetAssetPath(baseConfigFile);
            _logger.LogVerbose($"found config at {configPath}");
        } else {
            configPath = Path.GetFullPath(Paths.defaultBizhawkConfigPath);
            _logger.LogVerbose($"{name} using default config file at {configPath}");
        }

        string saveStatePath = saveStateFile ? Paths.GetAssetPath(saveStateFile) : null;
        string ramWatchPath = ramWatchFile ? Paths.GetAssetPath(ramWatchFile) : null;
        string luaScriptPath = luaScriptFile ? Paths.GetAssetPath(luaScriptFile) : null;

        string logFilePath = null;
        if (writeBizhawkLogs) {
            var logFileName = $"{gameObject.name}-{GetInstanceID()}.log";
            var logPath = config.BizHawkLogsPath;
            Directory.CreateDirectory(logPath);
            logFilePath = Path.Combine(logPath, logFileName);
            bizhawkLogLocation = logFilePath;
        }

        // Run _StartBizhawk in a separate thread to avoid blocking the main thread
        bool applicationIsPlaying = Application.isPlaying;
        CurrentStatus = Status.Starting;

        if (_initThreadCancellationTokenSource != null) {
            _logger.LogError("Emulator.Activate: _initThreadCancellationTokenSource is not null, this should never happen");
        }

        _initThreadCancellationTokenSource = new CancellationTokenSource();

        if (_initThread != null && _initThread.IsAlive) {
            // This can happen if previous thread has been cancelled (from Deactivate()) but not yet finished running
            // TODO: What should we do here? Wait for thread to finish? Force kill it? Or just ignore - seems like nothing bad happens
            _logger.LogWarning("Emulator.Initialize: starting a new _initThread while previous one is still running");
        }

        _initThread = new(() => _StartBizhawk(
            applicationIsPlaying,
            logFilePath,
            romPath,
            configPath,
            saveStatePath,
            ramWatchPath,
            luaScriptPath,
            shareAudio,
            _initThreadCancellationTokenSource.Token));
        _initThread.IsBackground = true;
        _initThread.Start();
    }

    static readonly ProfilerMarker StartBizhawkTimer = new ("Emulator.StartBizhawk");
    void _StartBizhawk(
        bool applicationIsPlaying,
        string logFilePath, // null to disable logging
        string romPath,
        string configPath,
        string saveStatePath,
        string ramWatchPath,
        string luaScriptPath,
        bool shareAudio,
        CancellationToken cancellationToken
    ) {
        // TODO: The separation between Initialize and _StartBizhawk is sort of messy,
        // mainly just moving anything that has to run on the main thread into Initialize
        // Would probably be better to make _StartBizhawk as small as possible, just the config loading and the process start

        _logger.LogVerbose("_StartBizhawk");

        StartBizhawkTimer.Begin();

        // get a random number to identify the buffers
        var guid = new System.Random().Next();

        _currentBizhawkArgs = MakeBizhawkArgs();

        _systemId = null;

        // If using referenced assets then first map those assets to filenames
        // (Bizhawk requires a path to a real file on disk)

        // Start EmuHawk.exe w args
        var exePath = Path.GetFullPath(Paths.emuhawkExePath);
        var process = new Process();
        process.StartInfo.UseShellExecute = false;
        var args = process.StartInfo.ArgumentList;
        if (IsTargetMac) {
            // Doesn't really work yet, need to make some more changes in the bizhawk executable
            process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Paths.dllDir;
            process.StartInfo.EnvironmentVariables["MONO_PATH"] = Paths.dllDir;
            process.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono";
            if (ShowBizhawkGui) {
                _logger.LogWarning("'Show Bizhawk Gui' is not supported on Mac'");
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
        _sharedTextureBuffer = new SharedTextureBuffer(sharedTextureBufferName, _logger);

        // create & register callbacks rpc (used for lua callbacks and also the Watch memory api)
        var callMethodRpcBufferName = $"call-method-{guid}";
        userData.Add($"{Args.CallMethodRpc}:{callMethodRpcBufferName}");
        _callMethodRpcBuffer = new CallMethodRpcBuffer(callMethodRpcBufferName, ProcessRpcCallback, _logger);

        // create & register api call buffers
        var apiCommandBufferName = $"api-command-{guid}";
        userData.Add($"{Args.ApiCommandBuffer}:{apiCommandBufferName}");
        _apiCommandBuffer = new ApiCommandBuffer(apiCommandBufferName, _logger);

        // var apiCallRpcBufferName = $"api-call-rpc-{guid}";
        // userData.Add($"{Args.ApiCallRpc}:{apiCallRpcBufferName}");
        // _apiCallRpcBuffer = new ApiCallRpcBuffer(apiCallRpcBufferName);

        // create & register audio buffer
        if (shareAudio) {
            var sharedAudioBufferName = $"audio-{guid}";
            userData.Add($"{Args.AudioRpc}:{sharedAudioBufferName}");
            _sharedAudioBuffer = new SharedAudioBuffer(sharedAudioBufferName, _logger);
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
                _sharedInputBuffer = new SharedInputBuffer(sharedInputBufferName, _logger);
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

        // Setup logger
        // Redirect bizhawk output + error into a log file
        if (logFilePath != null) {
            // (Use FileShare.ReadWrite to avoid annoying multi-threading bug that I don't really understand)
            var fileStream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _bizHawkLogWriter = new(fileStream);

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.OutputDataReceived += (sender, e) => LogBizHawk(sender, e, false);
            process.ErrorDataReceived += (sender, e) => LogBizHawk(sender, e, true);
        }

        _logger.Log("Starting EmuHawk process");
        _logger.Log($"{exePath} {string.Join(' ', args)}");

        if (cancellationToken.IsCancellationRequested) {
            // Startup cancelled, don't start the process
            _logger.LogVerbose("Startup thread cancelled, not starting bizhawk process");
            return;
        }

        process.Start();

        if (writeBizhawkLogs) {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        if (cancellationToken.IsCancellationRequested) {
            // Startup cancelled, kill the process
            _logger.LogVerbose("Startup thread cancelled, killing bizhawk process");
            process.Kill();
            return;
        }

        _emuhawk = process;

        CurrentStatus = Status.Started;
        _startedTime = SystemTime; // Unity Time.realtimeSinceStartup can't run on non-main thread

        StartBizhawkTimer.End();

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
        // _logger.LogVerbose("_Update");

        _deferredForMainThread?.Invoke();
        _deferredForMainThread = null;

        if (CurrentStatus < Status.Started) {
            return;
        }

        // In headless mode, if bizhawk steals focus, steal it back
        // [Checking this every frame seems to be the only thing that works
        //  - fortunately for some reason it doesn't steal focus when clicking into a different application]
        // [Except this has a nasty side effect, in the editor in play mode if you try to open a unity modal window
        //  (e.g. the game view aspect ratio config) it gets closed. To avoid this only do the check in the first 5 seconds after starting up]
        if (SystemTime - _startedTime < 5f && Application.isPlaying && !IsTargetMac && !ShowBizhawkGui && IsEmuHawkProcessAlive) {
            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            IntPtr bizhawkWindow = _emuhawk.MainWindowHandle;
            IntPtr focusedWindow = GetForegroundWindow();
            if (focusedWindow != unityWindow) {
                // _logger.LogVerbose("refocusing unity window");
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
                if (CurrentStatus == Status.Running && !audioResampler.HasSourceBuffer) {
                    // Set source buffer directly instead of copying samples
                    audioResampler.SetSourceBuffer(_sharedAudioBuffer.SampleQueue);
                    // short[] samples = _sharedAudioBuffer.GetSamples();
                    // audioResampler.PushSamples(samples);
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

        if (!IsEmuHawkProcessAlive) {
            _logger.LogWarning("EmuHawk process was unexpectedly killed, restarting");
            // TODO: maybe we want an option to not restart bizhawk here?
            Deactivate();
            if (ShouldRun) {
                Initialize();
            }
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
        int width = _sharedTextureBuffer.Width;
        int height = _sharedTextureBuffer.Height;
        if (_sharedTextureBuffer.Frame == _currentFrame) {
            // Already read this texture, no need to update
            return;
        }

        _currentFrame = _sharedTextureBuffer.Frame;
        
        if (width <= 0 || height <= 0) {
            // Width and height are 0 for a few frames after the emulator starts up
            // - presumably the texture buffer is open but bizhawk hasn't sent any data yet
            return;
        }

        _sharedTextureBuffer.CopyPixelsTo(_localTextureBuffer);

        bool noTextures = !_localTexture || !renderTexture;

        int bWidth = _localTexture != null ? _localTexture.width : 0;
        int bHeight = _localTexture != null ? _localTexture.height : 0;
        bool newDimensions = bWidth != width || bHeight != height;

        if (newDimensions) {
            _logger.LogVerbose($"new width and height received : {width} x {height} (was {bWidth}x{bHeight})");
        }

        // resize textures if necessary
        if (noTextures || newDimensions) {
            InitTextures(width, height);
        }

        int bSize = _localTexture!.width * _localTexture.height;
        if (bSize == 0) {
            return;
        }

        if (bSize > _localTextureBuffer.Length) {
            _logger.LogWarning($"emulator: texture bigger than buffer size {bSize} > {_localTextureBuffer.Length}");
            return;
        }

        try {
            _localTexture.SetPixelData(_localTextureBuffer, 0);
            _localTexture.Apply(/*updateMipmaps: false*/);
        } catch (Exception e) {
            _logger.LogError($"{e}");
        }

        // Correct issues with the texture by applying a shader and blitting to a separate render texture:
        Graphics.Blit(_localTexture, renderTexture, _textureCorrectionMat, 0);
    }

    void Deactivate() {
        _logger.LogVerbose("Deactivate");

        // Cancel _initThread if it's running
        if (_initThread != null && _initThread.IsAlive) {
            if (_initThreadCancellationTokenSource == null) {
                _logger.LogError("_initThread is running but_initThreadCancellationTokenSource is null, this should never happen");
            }
            _initThreadCancellationTokenSource.Cancel();
            _initThreadCancellationTokenSource.Dispose();
        }
        _initThreadCancellationTokenSource = null;

        if (_emuhawk != null && IsEmuHawkProcessAlive) {
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
        CurrentStatus = Status.Inactive;
    }

    /// Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _logger.LogVerbose($"creating new textures with dimensions {width}x{height}");

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
        if (CurrentStatus != Status.Running) return;

        // _logger.LogVerbose($"OnAudioFilterRead {outBuffer.Length} samples, {channels} channels");
        audioResampler.GetSamples(outBuffer, channels);
    }

    /// try opening a shared buffer
    void AttemptOpenBuffer(ISharedBuffer buf) {
        // _logger.LogVerbose($"Attempting to open buffer {buf} ({buf.GetType().Name})");
        try {
            buf.Open();
            // _logger.LogVerbose($"Connected to {buf}");
        } catch (FileNotFoundException) {
        }
    }

    void ProcessRpcCallback(string callbackName, string argString, out string returnString) {
        if (!_callMethodRpcBuffer.IsOpen()) {
            throw new Exception("Emulator.ProcessRpcCallback: _callMethodRpcBuffer is not open but should be");
        }
        // _logger.LogVerbose($"Rpc callback from bizhawk: {callbackName}({argString})");

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
                        _logger.LogWarning($"Expected 6 arguments for callback '{callbackName}', but got {args.Length}: {argString}");
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
                        _logger.LogWarning($"Bizhawk tried to call a watch callback for key {key} but no callback was registered");
                    }
                    break;
                case SpecialCommands.OnRomLoaded:
                    // args: $"{systemID}"
                    _systemId = argString;
                    CurrentStatus = Status.Running; // This is where the emulator is considered running
                    // (At this point I think we can be confident all the buffers should be open)
                    break;
                default:
                    _logger.LogWarning($"Bizhawk tried to call unknown special method {callbackName}");
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
                _logger.LogWarning($"Tried to call a method named {callbackName} from lua but none was registered");
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
                _logger.LogWarning(msg, context: null); // Don't pass context object cause it breaks in non-main thread
                // TODO: Probably should add to a queue here and log the queue in the main thread
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