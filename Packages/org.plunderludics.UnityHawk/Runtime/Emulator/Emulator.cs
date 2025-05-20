// This is the main user-facing MonoBehaviour
// handles starting up and communicating with the BizHawk process

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
    public bool captureEmulatorAudio = false;

    [Header("Files")]
    public Rom romFile;
    public Savestate saveStateFile;
    public Config configFile;
    public LuaScript luaScriptFile;
    public RamWatch ramWatchFile;

    [SerializeField, HideInInspector]
    bool _isEnabled = false; // hack to only show the forceCopyFilesToBuild field when component is inactive

    [HideIf("_isEnabled")]
    [Tooltip("Copy files into build even though Emulator is not active")]
    public bool forceCopyFilesToBuild = false;

    [Header("Development")]
    [Tooltip("if the bizhawk gui should be visible")]
    public bool showBizhawkGui = false;

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

    // Just for convenient reading from inspector:
    [Foldout("Debug")]
    [ReadOnly, SerializeField] Vector2Int _textureSize;

    // Options for using hardcoded filenames instead of Assets

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

    BizhawkArgs _currentBizhawkArgs; // remember the params corresponding to the currently running process

    /// Dictionary of registered methods that can be called from bizhawk lua
    readonly Dictionary<string, LuaCallback> _registeredLuaCallbacks = new();

    /// a set of all the called methods
    readonly HashSet<string> _invokedLuaCallbacks = new();

    SharedAudioBuffer _sharedAudioBuffer;

    /// buffer to receive lua callbacks from bizhawk
    CallMethodRpcBuffer _luaCallbacksRpcBuffer;

    /// buffer for main-thread write-only bizhawk api calls
    ApiCommandBuffer _apiCommandBuffer;

    /// buffer for read/write non-main-thread bizhawk api calls
    ApiCallRpcBuffer _apiCallRpcBuffer;

    /// buffer to send keyInputs to bizhawk
    SharedInputBuffer _sharedInputBuffer;

    /// buffer to receive screen texture from bizhawk
    SharedTextureBuffer _sharedTextureBuffer;

    Texture2D _bufferTexture;

    static readonly string textureCorrectionShaderName = "TextureCorrection";
    Material _textureCorrectionMat;

    Material _renderMaterial; // just used for rendering in edit mode

    StreamWriter _bizHawkLogWriter;

    [SerializeField]
    [Foldout("Debug")]
    [ShowIf("captureEmulatorAudio")]
    AudioResampler _audioResampler;

    float _startedTime;

    private const double BizhawkSampleRate = 44100f;

    private const string _savestateExtension = "savestate";

    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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
    ///// MonoBehaviour lifecycle
    ///
#if UNITY_EDITOR
    void OnValidate() {
	    if (!config) {
		    config = (UnityHawkConfig)AssetDatabase.LoadAssetAtPath(Paths.defaultUnityHawkConfigPath, typeof(UnityHawkConfig));
            if (config == null) {
                Debug.LogError("UnityHawkConfigDefault.asset not found");
            }
        }
    }
#endif

    // (These methods are public only for convenient testing)
    public void OnEnable()
    {
        _isEnabled = true;
#if UNITY_EDITOR && UNITY_2022_2_OR_NEWER
        if (Undo.isProcessing) return; // OnEnable gets called after undo/redo, but ignore it
#endif
        _initialized = false;

        if (!runInEditMode && (!Application.isPlaying || romFile == null)) return;

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
            if (showBizhawkGui) {
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

        if (!showBizhawkGui) {
            args.Add("--headless");
            _emuhawk.StartInfo.CreateNoWindow = true;
            _emuhawk.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }
        
        List<string> userData = new(); // Userdata args get used by UnityHawk external tool

        // add buffers
        // get a random number to identify the buffers
        var randomNumber = new System.Random().Next();

        // create & register sharedTextureBuffer
        var sharedTextureBufferName = $"unityhawk-texture-{randomNumber}";
        userData.Add($"unityhawk-texture-buffer:{sharedTextureBufferName}");
        _sharedTextureBuffer = new SharedTextureBuffer(sharedTextureBufferName);

        // create & register lua callbacks rpc
        // TODO
        // var luaCallbacksRpcBufferName = $"unityhawk-callmethod-{randomNumber}";
        // args.Add($"--unity-call-method-buffer={luaCallbacksRpcBufferName}");
        // _luaCallbacksRpcBuffer = new CallMethodRpcBuffer(luaCallbacksRpcBufferName, CallRegisteredLuaCallback);

        // create & register api call buffer
        // TODO
        // var apiCommandBufferName = $"unityhawk-apicall-{randomNumber}";
        // args.Add($"--api-call-method-buffer={apiCommandBufferName}");
        // _apiCommandBuffer = new ApiCommandBuffer(apiCommandBufferName);

        var apiCallRpcBufferName = $"api-call-rpc-{randomNumber}";
        userData.Add($"unityhawk-api-call-rpc:{apiCallRpcBufferName}");
        _apiCallRpcBuffer = new ApiCallRpcBuffer(apiCallRpcBufferName);

        // create & register audio buffer
        // TODO
        // if (captureEmulatorAudio) {
        //     if (runInEditMode && !Application.isPlaying) {
        //         Debug.LogWarning("captureEmulatorAudio is enabled but emulator audio cannot be captured in edit mode");
        //     } else {
        //         var sharedAudioBufferName = $"unityhawk-audio-{randomNumber}";
        //         args.Add($"--share-audio-over-rpc-buffer={sharedAudioBufferName}");
        //         _sharedAudioBuffer = new SharedAudioBuffer(sharedAudioBufferName);

        //         if (_audioResampler == null) {
        //             _audioResampler = new();
        //         }
        //         _audioResampler.Init(BizhawkSampleRate/AudioSettings.outputSampleRate);
        //     }
        // }

        // create & register input buffers
        if (Application.isPlaying) {
            if (passInputFromUnity) {
                var sharedInputBufferName = $"input-{randomNumber}";
                // args.Add($"--read-input-from-shared-buffer={sharedKeyInputBufferName}");
                userData.Add($"unityhawk-input-buffer:{sharedInputBufferName}");
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

        _emuhawk.Start();
        _emuhawk.BeginOutputReadLine();
        _emuhawk.BeginErrorReadLine();
        Status = EmulatorStatus.Started;
        _startedTime = Time.realtimeSinceStartup;

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
        //  (e.g. the game view aspect ratio config) it gets closed. To avoid this only do the check in the first 5 seconds after starting up]
        if (Time.realtimeSinceStartup - _startedTime < 5f && Application.isPlaying && !_targetMac && !showBizhawkGui && _emuhawk != null) {
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
            // TODO: Maybe should be after first texture data is received, not immediately after texture buffer is open?
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

        // if (!_luaCallbacksRpcBuffer.IsOpen()) {
        //     AttemptOpenBuffer(_luaCallbacksRpcBuffer);
        // }
        // if (!_apiCommandBuffer.IsOpen()) {
        //     AttemptOpenBuffer(_apiCommandBuffer);
        // }
        // if (!_apiCommandBuffer.IsOpen()) {
        //     AttemptOpenBuffer(_apiCommandBuffer);
        // }
        if (!_apiCallRpcBuffer.IsOpen()) {
            AttemptOpenBuffer(_apiCallRpcBuffer);
        }

        // if (captureEmulatorAudio && Application.isPlaying) {
        //     if (_sharedAudioBuffer.IsOpen()) {
        //         if (Status == EmulatorStatus.Running) {
        //             short[] samples = _sharedAudioBuffer.GetSamples();
        //             // Updating audio before the emulator is actually running messes up the resampling algorithm
        //             _audioResampler.PushSamples(samples);
        //         }
        //     } else {
        //         AttemptOpenBuffer(_sharedAudioBuffer);
        //     }
        // }

        if (_emuhawk != null && _emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
            Deactivate();
        }
    }

    void WriteInputToBuffer(List<InputEvent> inputEvents) {
        // Get input from inputProvider, serialize and write to the shared memory
        foreach (UnityHawk.InputEvent ie in inputEvents) {
            // Convert Unity InputEvent to BizHawk InputEvent
            // [for now only supporting keys, no gamepad]
            var bie = ConvertInput.ToBizHawk(ie);
            _sharedInputBuffer.Write(bie);
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
        Status = EmulatorStatus.Inactive;

        foreach (ISharedBuffer buf in new ISharedBuffer[] {
            _sharedTextureBuffer,
            _sharedAudioBuffer,
            _luaCallbacksRpcBuffer,
            _apiCommandBuffer,
            _apiCallRpcBuffer
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

    bool CallRegisteredLuaCallback(string callbackName, string argString, out string returnString) {
        // call corresponding method
        returnString = "";
        var exists = _registeredLuaCallbacks.TryGetValue(callbackName, out var callback);
        if (exists) {
            returnString = callback(argString);
        }

        // add to set of called methods to not spam this warning
        if (!exists && !_invokedLuaCallbacks.Contains(callbackName)){
            Debug.LogWarning($"Tried to call a method named {callbackName} from lua but none was registered");
        }

        _invokedLuaCallbacks.Add(callbackName);

        return exists;
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
            showBizhawkGui = showBizhawkGui
        };
    }
}
}