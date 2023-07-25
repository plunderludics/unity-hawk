// This is the main user-facing component (ie MonoBehaviour)
// acts as a bridge between Unity and BizHawkInstance, handling input,
// graphics, audio, multi-threading, and framerate throttling.

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

using NaughtyAttributes;

using UnityEngine;
using Unity.Profiling;

using SharedMemory;

using BizHawk.Plunderludics;

namespace UnityHawk {

[ExecuteInEditMode]
public class Emulator : MonoBehaviour
{
    static readonly bool _targetMac = false; // not really supported yet

    public bool useAttachedRenderer = true;
    [HideIf("useAttachedRenderer")]
    public Renderer targetRenderer;

    [Header("Files")]
    // All pathnames are loaded relative to ./StreamingAssets/, unless the pathname is absolute (see GetAssetPath)
    public string romFileName = "Roms/mario.nes";
    public string configFileName = ""; // Leave empty for default config.ini
    public string saveStateFileName = ""; // Leave empty to boot clean
    public string luaScriptFileName;
    
    [Header("Debug")]
    public bool runInEditMode = false;
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
    private bool linearTexture; // [seems so make no difference visually]
    private bool forceReinitTexture;
    private bool blitTexture = true;
    private bool doTextureCorrection = true;

    // Interface for other scripts to use
    public RenderTexture Texture => _renderTexture;
    public bool IsRunning => _isRunning;

    SharedArray<int> sharedTextureBuffer;
    Process emuhawk;
    private string _sharedTextureMemoryName;

    Texture2D _bufferTexture;
    RenderTexture _renderTexture; // We have to maintain a separate rendertexture just for the purpose of flipping the image we get from the emulator

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
        if (sharedTextureBuffer != null && sharedTextureBuffer.Length > 0) {
            // Get the texture buffer and dimensions from BizHawk via the shared memory file
            // protocol has to match MainForm.cs in BizHawk
            // TODO should probably put this protocol in some shared schema file or something idk
            int[] localTextureBuffer = new int[sharedTextureBuffer.Length];
            sharedTextureBuffer.CopyTo(localTextureBuffer, 0);
            int width = localTextureBuffer[sharedTextureBuffer.Length - 2];
            int height = localTextureBuffer[sharedTextureBuffer.Length - 1];

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
        } else {
            if (sharedTextureBuffer != null) {
                // i don't get why but this happens sometimes
                Debug.LogWarning($"shared buffer length was 0 or less: {sharedTextureBuffer.Length}");
            }
            AttemptOpenSharedTextureBuffer();
        }

        if (emuhawk != null && emuhawk.HasExited) {
            Debug.LogWarning("EmuHawk process was unexpectedly killed");
        }
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
            args.Add(exePath);
        } else {
            // Windows
            emuhawk.StartInfo.FileName = exePath;
            emuhawk.StartInfo.UseShellExecute = false;
        }

        if (!showBizhawkGui) args.Add("--headless");
        
        _sharedTextureMemoryName = "unityhawk-texbuf-" + GetInstanceID();
        args.Add($"--share-texture={_sharedTextureMemoryName}");

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

        _initialized = true;
    }

    void Deactivate() {
        _initialized = false;
        _bizHawkLogWriter.Close();
        if (emuhawk != null && !emuhawk.HasExited) {
            // Kill the emuhawk process
            emuhawk.Kill();
        }
        _isRunning = false;
        if (sharedTextureBuffer != null) sharedTextureBuffer.Close();
        sharedTextureBuffer = null;
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
        try {
            sharedTextureBuffer = new (name: _sharedTextureMemoryName);
            _isRunning = true; // if we're able to connect to the shared buffer, assume the rom is running ok
            Debug.Log("Connected to shared texture buffer");
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
}
}