// This is the main user-facing MonoBehaviour
// handles starting up and communicating with the BizHawk process

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

using NaughtyAttributes;

using UnityEngine;
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
    public bool captureEmulatorAudio = false;

    bool EnableRomFileSelection => !autoSelectRomFile || SaveStateFileIsNull;
    
    [Header("Files")]
    [EnableIf("EnableRomFileSelection")]
    public Rom romFile;
    public Savestate saveStateFile;
    bool SaveStateFileIsNull => saveStateFile is null;
    [HideIf("SaveStateFileIsNull")]
    public bool autoSelectRomFile = true;

    public Config configFile;
    public LuaScript luaScriptFile;
    public RamWatch ramWatchFile;

    [Header("Development")]
    [Tooltip("if the bizhawk gui should be visible while running in unity editor")]
    public bool showBizhawkGuiInEditor = false;

    [ReadOnlyWhenPlaying]
    [Tooltip("whether bizhawk should run when in edit mode")]
    public new bool runInEditMode = false;
    [ShowIf("runInEditMode")]
    [ReadOnlyWhenPlaying]
    [Tooltip("Whether BizHawk will accept input when window is unfocused (in edit mode)")]
    public bool acceptBackgroundInput = true;

    public Action OnStarted;

    public Action OnRunning;

    [Foldout("Debug")]
    [ReadOnly, SerializeField] bool _initialized;

    [Foldout("Debug")]
    [ReadOnly, SerializeField] EmulatorStatus _status;

    [Foldout("Debug")]
    [ReadOnly, SerializeField] int _currentFrame; // The frame index of the most-recently grabbed texture

    [Foldout("Debug")]
    [ReadOnly, SerializeField] string _systemId; // The system ID of the current core (e.g. "N64", "PSX", etc.)

    // Just for convenient reading from inspector:
    [Foldout("Debug")]
    [ReadOnly, SerializeField] Vector2Int _textureSize;

    // Options for using hardcoded filenames instead of Assets

    [Foldout("Debug")]
    [SerializeField] bool showBizhawkGuiInBuild = false;
    [Foldout("Debug")]
    [SerializeField] bool muteBizhawkInEditMode = true;
    [Foldout("Debug")]
    [SerializeField] UnityHawkConfig config;
    [Tooltip("Prevent BizHawk from popping up windows for warnings and errors; these will still appear in logs")]
    public bool suppressBizhawkPopups = true;
    [Foldout("Debug")]
    public bool writeBizhawkLogs = true;
    [ShowIf("writeBizhawkLogs")]
    [Foldout("Debug")]
    [ReadOnly, SerializeField] string bizhawkLogLocation;

    [Foldout("Debug")]
    public string savestatesOutputPath;

    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    [SerializeField]
    AudioResampler _audioResampler;

    private TextureFormat textureFormat = TextureFormat.BGRA32;
    private RenderTextureFormat renderTextureFormat = RenderTextureFormat.BGRA32;

    Process _emuhawk;

    // Basically these are the params which, if changed, we want to reset the bizhawk process
    // Don't include the path params here, because then the process gets reset for every character typed/deleted
    struct BizhawkArgs {
#if UNITY_EDITOR
        public Rom romFile;
        public Savestate saveStateFile;
        public Config configFile;
        public LuaScript luaScriptFile;
        public RamWatch ramWatchFile;
#endif
        public bool passInputFromUnity;
        public bool captureEmulatorAudio;
        public bool acceptBackgroundInput;
        public bool showBizhawkGui;
    }


    bool ShowBizhawkGui => 
#if UNITY_EDITOR
        showBizhawkGuiInEditor
#else
        showBizhawkGuiInBuild
#endif
    ;

    BizhawkArgs _currentBizhawkArgs; // remember the params corresponding to the currently running process

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

    int[] _localTextureBuffer;

    Texture2D _bufferTexture;

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

    Material _renderMaterial; // just used for rendering in edit mode

    StreamWriter _bizHawkLogWriter;

    float _startedTime;

    private const double BizhawkSampleRate = 44100f;

    private const string _savestateExtension = "savestate";

    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    Action _deferredForMainThread = null; // Pretty ugly solution for rpc handlers to get stuff to run on the main thread

    [Button]
    public void Reset() {
        Deactivate();
        // Will be reactivated in Update on next frame
    }

#if UNITY_EDITOR
    [Button]
    private void ShowBizhawkLogInOS() {
        EditorUtility.RevealInFinder(bizhawkLogLocation);
    }
#endif

#if UNITY_EDITOR
    public void OnValidate() {
        if (!Equals(_currentBizhawkArgs, MakeBizhawkArgs())) {
            // Params set in inspector have changed since the bizhawk process was started, needs restart
            Deactivate();
        }
        
        if (!runInEditMode && Status != EmulatorStatus.Inactive) {
            Deactivate();
        }

        if (useAttachedRenderer) {
            // Default to the attached Renderer component, if there is one
            targetRenderer = GetComponent<Renderer>();
            if (!targetRenderer) {
                Debug.LogWarning("No Renderer attached, will not display emulator graphics");
            }
        }

	    if (!config) {
		    config = (UnityHawkConfig)AssetDatabase.LoadAssetAtPath(Paths.defaultUnityHawkConfigPath, typeof(UnityHawkConfig));
            if (config == null) {
                Debug.LogError("UnityHawkConfigDefault.asset not found");
            }
        }

        // Select rom file automatically based on save state (if possible)
        if (autoSelectRomFile && saveStateFile != null) {
            var roms = AssetDatabase.FindAssets("t:rom")
                .Select(guid => AssetDatabase.LoadAssetAtPath<Rom>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(rom => saveStateFile.MatchesRom(rom));
            
            if (roms.Any()) {
                var rom = roms.First();
                if (roms.Count() > 1) {
                    Debug.LogWarning($"Multiple roms found matching savestate {saveStateFile.name}, using first match: {rom}");
                }
                romFile = rom;
            } else {
                Debug.LogWarning($"No rom found matching savestate {saveStateFile.name}");
            }
        }

        // If emulator not running, set texture to savestate screenshot
        if (!IsRunning && saveStateFile?.Screenshot is not null) {
            InitTextures(saveStateFile.Screenshot.width, saveStateFile.Screenshot.height);
        }
    }
#endif

    ///// MonoBehaviour lifecycle
    // (These methods are public only for convenient testing)
    public void OnEnable()
    {
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _initialized = false;

        TryInitialize();
    }

    public void Update() {
        _Update();
    }

    public void OnDisable() {
        // Debug.Log($"Emulator OnDisable");
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnDisable gets called after undo/redo, but ignore it
#endif
        if (_initialized) {
            Deactivate();
        }
    }

    ////// Core methods

    bool TryInitialize() {
        if (!runInEditMode && !Application.isPlaying) return false;
        if (romFile == null) {
            Debug.LogError("No rom file set, cannot start emulator");
            return false;
        }

        _currentBizhawkArgs = MakeBizhawkArgs();

        // Debug.Log("Emulator Initialize");
        if (!customRenderTexture) renderTexture = null; // Clear texture so that it's forced to be reinitialized

        Status = EmulatorStatus.Inactive;
        _systemId = null;

        _textureCorrectionMat = new Material(Resources.Load<Shader>(textureCorrectionShaderName));

        if (captureEmulatorAudio && GetComponent<AudioSource>() == null) {
            Debug.LogWarning("captureEmulatorAudio is enabled but no AudioSource is attached, will not play audio");
        }

        // If using referenced assets then first map those assets to filenames
        // (Bizhawk requires a path to a real file on disk)

        // Start EmuHawk.exe w args
        var exePath = Path.GetFullPath(Paths.emuhawkExePath);
        _emuhawk = new Process();
        _emuhawk.StartInfo.UseShellExecute = false;
        var args = _emuhawk.StartInfo.ArgumentList;
        if (_targetMac) {
            // Doesn't really work yet, need to make some more changes in the bizhawk executable
            _emuhawk.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = Paths.dllDir;
            _emuhawk.StartInfo.EnvironmentVariables["MONO_PATH"] = Paths.dllDir;
            _emuhawk.StartInfo.FileName = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono";
            if (ShowBizhawkGui) {
                Debug.LogWarning("'Show Bizhawk Gui' is not supported on Mac'");
            }
            args.Add(exePath);
        } else {
            // Windows
            _emuhawk.StartInfo.FileName = exePath;
            _emuhawk.StartInfo.UseShellExecute = false;
        }

        // add rom path
        var romPath = Paths.GetAssetPath(romFile);
        args.Add(romPath);

        // add config path
        var configPath = configFile
            ? Paths.GetAssetPath(configFile)
            : Path.GetFullPath(Paths.defaultBizhawkConfigPath);

        args.Add($"--config={configPath}");

        // add save state path
        if (saveStateFile) {
            args.Add($"--load-state={Paths.GetAssetPath(saveStateFile)}");
        }

        // add ram watch file
        if (ramWatchFile) {
            args.Add($"--ram-watch-file={Paths.GetAssetPath(ramWatchFile)}");
        }

        // add lua script file
        if (luaScriptFile) {
            args.Add($"--lua={Paths.GetAssetPath(luaScriptFile)}");
        }

        // Save savestates with extension .savestate instead of .State, this is because Unity treats .State as some other kind of asset
        args.Add($"--savestate-extension={_savestateExtension}");

        // set savestates output dir
        // (default to rom parent directory when not provided)
        var savestatesOutputPath = string.IsNullOrEmpty(config.SavestatesOutputPath) ? Path.GetDirectoryName(romPath) : config.SavestatesOutputPath;
        var fullSavestatesOutputPath = Paths.GetFullPath(savestatesOutputPath);
        if (!Directory.Exists(fullSavestatesOutputPath)) {
            Directory.CreateDirectory(fullSavestatesOutputPath);
        }
        args.Add($"--savestates={fullSavestatesOutputPath}");

        // add firmware
        args.Add($"--firmware={Path.Combine(Application.streamingAssetsPath, config.FirmwarePath)}");

        // set ramwatch output dir
        var ramWatchOutputDirPath = config.RamWatchOutputPath;
        // use rom directory as default ramwatch output path
        if (string.IsNullOrEmpty(ramWatchOutputDirPath)) {
            ramWatchOutputDirPath = Path.GetDirectoryName(romPath);
        }
        args.Add($"--save-ram-watch={ramWatchOutputDirPath}");

        if (!ShowBizhawkGui) {
            args.Add("--headless");
            _emuhawk.StartInfo.CreateNoWindow = true;
            _emuhawk.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }
        
        List<string> userData = new(); // Userdata args get used by UnityHawk external tool

        // add buffers
        // get a random number to identify the buffers
        var randomNumber = new System.Random().Next();

        // create & register sharedTextureBuffer
        var sharedTextureBufferName = $"texture-{randomNumber}";
        userData.Add($"{Args.TextureBuffer}:{sharedTextureBufferName}");
        _sharedTextureBuffer = new SharedTextureBuffer(sharedTextureBufferName);

        // create & register callbacks rpc (used for lua callbacks and also the Watch memory api)
        var callMethodRpcBufferName = $"call-method-{randomNumber}";
        userData.Add($"{Args.CallMethodRpc}:{callMethodRpcBufferName}");
        _callMethodRpcBuffer = new CallMethodRpcBuffer(callMethodRpcBufferName, ProcessRpcCallback);

        // create & register api call buffers
        var apiCommandBufferName = $"api-command-{randomNumber}";
        userData.Add($"{Args.ApiCommandBuffer}:{apiCommandBufferName}");
        _apiCommandBuffer = new ApiCommandBuffer(apiCommandBufferName);

        // var apiCallRpcBufferName = $"api-call-rpc-{randomNumber}";
        // userData.Add($"{Args.ApiCallRpc}:{apiCallRpcBufferName}");
        // _apiCallRpcBuffer = new ApiCallRpcBuffer(apiCallRpcBufferName);

        // create & register audio buffer
        if (captureEmulatorAudio) {
            if (runInEditMode && !Application.isPlaying) {
                Debug.LogWarning("captureEmulatorAudio is enabled but emulator audio cannot be captured in edit mode");
            } else {
                var sharedAudioBufferName = $"audio-{randomNumber}";
                userData.Add($"{Args.AudioRpc}:{sharedAudioBufferName}");
                _sharedAudioBuffer = new SharedAudioBuffer(sharedAudioBufferName);

                if (_audioResampler == null) {
                    _audioResampler = new();
                }
                _audioResampler.Init(BizhawkSampleRate/AudioSettings.outputSampleRate);
            }
        }

        if (muteBizhawkInEditMode && !Application.isPlaying) {
            args.Add("--mute=true");
        }

        // create & register input buffers
        if (Application.isPlaying) {
            if (passInputFromUnity) {
                var sharedInputBufferName = $"input-{randomNumber}";
                // args.Add($"--read-input-from-shared-buffer={sharedKeyInputBufferName}");
                userData.Add($"{Args.InputBuffer}:{sharedInputBufferName}");
                _sharedInputBuffer = new SharedInputBuffer(sharedInputBufferName);

                // default to BasicInputProvider (maps keys directly from keyboard)
                if (inputProvider == null) {
                    if (!(inputProvider = GetComponent<InputProvider>())) {
                        inputProvider = gameObject.AddComponent<BasicInputProvider>();
                    }
                }
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
            // Redirect bizhawk output + error into a log file
            var logFileName = $"{name}-{GetInstanceID()}.log";
            var logPath = config.LogsPath;
            Directory.CreateDirectory (logPath);
            bizhawkLogLocation = Path.Combine(logPath, logFileName);

            _bizHawkLogWriter?.Dispose();
            _bizHawkLogWriter = new(bizhawkLogLocation);

            _emuhawk.StartInfo.RedirectStandardOutput = true;
            _emuhawk.StartInfo.RedirectStandardError = true;
            _emuhawk.OutputDataReceived += (sender, e) => LogBizHawk(sender, e, false);
            _emuhawk.ErrorDataReceived += (sender, e) => LogBizHawk(sender, e, true);
        }

        Debug.Log($"[unity-hawk] {exePath} {string.Join(' ', args)}");

        _emuhawk.Start(); // [This seems to block for ~20s sometimes, not sure why. Should maybe run in a separate thread?]
        _emuhawk.BeginOutputReadLine();
        _emuhawk.BeginErrorReadLine();
        Status = EmulatorStatus.Started;
        _startedTime = Time.realtimeSinceStartup;

        _initialized = true;
        return true;
    }

    void _Update() {
        if (!Application.isPlaying && !runInEditMode) {
            return;
        }

        if (!_initialized && !TryInitialize()) {
            return;
        }

        // In headless mode, if bizhawk steals focus, steal it back
        // [Checking this every frame seems to be the only thing that works
        //  - fortunately for some reason it doesn't steal focus when clicking into a different application]
        // [Except this has a nasty side effect, in the editor in play mode if you try to open a unity modal window
        //  (e.g. the game view aspect ratio config) it gets closed. To avoid this only do the check in the first 5 seconds after starting up]
        if (Time.realtimeSinceStartup - _startedTime < 5f && Application.isPlaying && !_targetMac && !ShowBizhawkGui && _emuhawk != null) {
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
            UpdateTextureFromBuffer();
        } else {
            AttemptOpenBuffer(_sharedTextureBuffer);
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
        if (!_apiCommandBuffer.IsOpen()) {
            AttemptOpenBuffer(_apiCommandBuffer);
        }
        // if (!_apiCallRpcBuffer.IsOpen()) {
        //     AttemptOpenBuffer(_apiCallRpcBuffer);
        // }

        if (captureEmulatorAudio && Application.isPlaying) {
            if (_sharedAudioBuffer.IsOpen()) {
                if (Status == EmulatorStatus.Running) {
                    short[] samples = _sharedAudioBuffer.GetSamples();
                    // Updating audio before the emulator is actually running messes up the resampling algorithm
                    _audioResampler.PushSamples(samples);
                }
            } else {
                AttemptOpenBuffer(_sharedAudioBuffer);
            }
        }

        if (_deferredForMainThread != null) {
            _deferredForMainThread();
        }

        if (_emuhawk != null && _emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
            Deactivate();
        }
    }

    void WriteInputToBuffer(List<UnityHawk.InputEvent> inputEvents) {
        // Get input from inputProvider, serialize and write to the shared memory
        foreach (UnityHawk.InputEvent ie in inputEvents) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            Plunderludics.UnityHawk.Shared.InputEvent bie = ConvertInput.ToBizHawk(ie);
            _sharedInputBuffer.Write(bie);
        }
    }

    void UpdateTextureFromBuffer() {
        // Get the texture buffer and dimensions from BizHawk via the shared memory file
        // protocol has to match MainForm.cs in BizHawk
        // TODO should probably put this protocol in some shared schema file or something idk
        if (_localTextureBuffer == null || _localTextureBuffer.Length != _sharedTextureBuffer.Length) {
            _localTextureBuffer = new int[_sharedTextureBuffer.Length];
        }
        _sharedTextureBuffer.CopyTo(_localTextureBuffer, 0);
        int width = _localTextureBuffer[_sharedTextureBuffer.Length - 3];
        int height = _localTextureBuffer[_sharedTextureBuffer.Length - 2];
        _currentFrame = _localTextureBuffer[_sharedTextureBuffer.Length - 1]; // frame index of this texture [hacky solution to sync issues]

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
            _bufferTexture.SetPixelData(_localTextureBuffer, 0);
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
            _apiCommandBuffer,
            // _apiCallRpcBuffer
        }) {
            if (buf != null && buf.IsOpen()) {
                buf.Close();
            }
        }
    }

    // Init/re-init the textures for rendering the screen - has to be done whenever the source dimensions change (which happens often on PSX for some reason)
    void InitTextures(int width, int height) {
        _textureSize = new Vector2Int(width, height);
        _bufferTexture = new Texture2D(width, height, textureFormat, false);

        if (!customRenderTexture) {
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
                _renderMaterial.mainTexture = Texture;

                targetRenderer.material = _renderMaterial;
            } else  {
                // play mode, just let unity clone the material the default way
                targetRenderer.material.mainTexture = Texture;
            }
        }
    }

    // Send audio from the emulator to the AudioSource
    // (this method gets called by Unity if there is an AudioSource component attached)
    void OnAudioFilterRead(float[] out_buffer, int channels) {
        if (!captureEmulatorAudio) return;
        if (!_sharedAudioBuffer.IsOpen()) return;
        if (Status != EmulatorStatus.Running) return;

        _audioResampler.GetSamples(out_buffer, channels);
    }

    void AttemptOpenBuffer(ISharedBuffer buf) {
        try {
            buf.Open();
            // Debug.Log($"Connected to {buf}");
        } catch (FileNotFoundException) {
            // Debug.LogError(e);
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
                        Debug.LogWarning($"Expected 6 arguments for callback '{callbackName}', but got {args.Length}: {argString}");
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
                        Debug.LogWarning($"Bizhawk tried to call a watch callback for key {key} but no callback was registered");
                    }
                    break;
                case SpecialCommands.OnRomLoaded:
                    // args: $"{systemID}"
                    _systemId = argString;
                    _deferredForMainThread += () => Status = EmulatorStatus.Running; // This is where the emulator is considered running
                    // (At this point I think we can be confident all the buffers should be open)
                    break;
                default:
                    Debug.LogWarning($"Bizhawk tried to call unknown special method {callbackName}");
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
                Debug.LogWarning($"Tried to call a method named {callbackName} from lua but none was registered");
            }

            _invokedLuaCallbacks.Add(callbackName);
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
#endif
            passInputFromUnity = passInputFromUnity,
            captureEmulatorAudio = captureEmulatorAudio,
            acceptBackgroundInput = acceptBackgroundInput,
            showBizhawkGui = ShowBizhawkGui
        };
    }
}
}