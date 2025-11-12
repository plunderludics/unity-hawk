// Inspector-visible fields and attributes for the Emulator component
// This file consolidates all inspector-related content to ensure consistent ordering

using UnityEngine;
using UnityEngine.Serialization;
using TriInspector;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityHawk {

[DeclareFoldoutGroup("BizHawk Config")]
[DeclareFoldoutGroup("Development")]
[DeclareFoldoutGroup("State")]
[DeclareFoldoutGroup("Debug")]
public partial class Emulator {
    [Tooltip("if the emulator launches on start")]
    public bool runOnEnable = true;

    ///// Game
    [Header("Game")]
    [Tooltip("Savestate file to load")]
    public Savestate saveStateFile;

    [HideIf(nameof(SaveStateFileIsNull))]
    [Tooltip("select rom file automatically based on savestate")]
    public bool autoSelectRomFile = true;

    [EnableIf(nameof(EnableRomFileSelection))]
    [Tooltip("Rom file to run")]
    public Rom romFile;

    ///// Rendering
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

    [Tooltip("a bizhawk ram watch file to open alongside the emulator (.wch)")]
    public RamWatch ramWatchFile;

    ///// BizHawk Config Group
    [FormerlySerializedAs("configFile")]
    [Group("BizHawk Config")]
    [Tooltip("a BizHawk config file (.ini) that will be copied for this instance")]
    public Config baseConfigFile;

    [Group("BizHawk Config")]
    [OnValueChanged(nameof(OnSetVolume))]
    [Range(0, 100)]
    [Tooltip("the volume of the emulator, 0-100")]
    [SerializeField] int volume = 100;

    [Group("BizHawk Config")]
    [OnValueChanged(nameof(OnSetIsMuted))]
    [Tooltip("if the emulator is muted")]
    [SerializeField] bool isMuted;

    [Group("BizHawk Config")]
    [OnValueChanged(nameof(OnSetIsPaused))]
    [Tooltip("if the emulator is paused")]
    [SerializeField] bool isPaused;
    
    [Group("BizHawk Config")]
    [OnValueChanged(nameof(OnSetSpeedPercent))]
    [Range(0, 200)]
    [Tooltip("emulator speed as a percentage")]
    [SerializeField] int speedPercent = 100;

    ///// Development Group
    [Group("Development")]
    [Tooltip("if the bizhawk gui should be visible while running in unity editor")]
    public bool showBizhawkGuiInEditor = false;

    [Group("Development")]
    [ReadOnlyWhenPlaying]
    [Tooltip("whether bizhawk should run when in edit mode")]
#pragma warning disable CS0109
    // Hides inherited member; new keyword is not required
    // (I don't get why but unity seems to flip-flop between complaining the new keyword is missing or that it's redundant - just ignore)
    public new bool runInEditMode = false;
#pragma warning restore CS0109

    [Group("Development")]
    [ShowIf(nameof(runInEditMode))]
    [ReadOnlyWhenPlaying]
    [Tooltip("Whether BizHawk will accept input when window is unfocused (in edit mode)")]
    public bool acceptBackgroundInput = true;

    [Group("Development")]
    [SerializeField] bool muteBizhawkInEditMode = true;

    ///// Debug Group
    [Group("Debug")]
    [OnValueChanged(nameof(OnSetLogLevel))]
    [SerializeField] Logger.LogLevel logLevel = Logger.LogLevel.Warning;

    [Group("Debug")]
    [SerializeField] UnityHawkConfig config;

    [Group("Debug")]
    [Tooltip("if the bizhawk gui should be visible in the build")]
#pragma warning disable CS0414 // Suppress 'assigned but never used' - only unused in editor
    [SerializeField] bool showBizhawkGuiInBuild = false;
#pragma warning restore CS0414

    [Group("Debug")]
    [Tooltip("Prevent BizHawk from popping up windows for warnings and errors; these will still appear in logs")]
    [SerializeField] bool suppressBizhawkPopups = true;

    [Group("Debug")]
    [SerializeField] bool writeBizhawkLogs = true;

    [ShowIf(nameof(writeBizhawkLogs))]
    [Group("Debug")]
    [ReadOnly, ShowInInspector] string bizhawkLogLocation;

    ///// State Group
    [Group("State")]
    [ReadOnly, ShowInInspector] Status status; // Just for displaying in inspector - the actual internal state is _status but we don't want to serialize that

    [Group("State")]
    [ReadOnly, ShowInInspector] int _currentFrame; // The frame index of the most-recently grabbed texture

    [Group("State")]
    [ReadOnly, SerializeField] string _systemId; // The system ID of the current core (e.g. "N64", "PSX", etc.)



    [Button("Restart")]
    void _Restart() => Restart();

#if UNITY_EDITOR
    [Button]
    void ShowBizhawkLogInOS() {
        EditorUtility.RevealInFinder(bizhawkLogLocation);
    }
#endif
}

}
