// Public methods for the Emulator component
// Including methods for interfacing with the BizHawk API (loading/saving states, etc)

using System;
using UnityEngine;

using NaughtyAttributes;
using System.Linq;

namespace UnityHawk {
public partial class Emulator {
    ///// props
    [Foldout("BizHawk Config")]
    [OnValueChanged(nameof(OnSetVolume))]
    [Range(0, 100)]
    [Tooltip("the volume of the emulator, 0-100")]
    [SerializeField] int volume = 100;

    [Foldout("BizHawk Config")]
    [OnValueChanged(nameof(OnSetIsMuted))]
    [Tooltip("if the emulator is muted")]
    [SerializeField] bool isMuted;

    [Foldout("BizHawk Config")]
    [OnValueChanged(nameof(OnSetIsPaused))]
    [Tooltip("if the emulator is paused")]
    [SerializeField] bool isPaused;
    
    [Foldout("BizHawk Config")]
    [OnValueChanged(nameof(OnSetSpeedPercent))]
    [Range(0, 200)]
    [Tooltip("emulator speed as a percentage")]
    [SerializeField] int speedPercent = 100;

    /// <summary>
    /// if the emulator is paused
    /// </summary>
    public bool IsPaused {
        get => isPaused;
        set {
            isPaused = value;
            OnSetIsPaused();
        }
    }

    /// <summary>
    /// the emulator current volume
    /// </summary>
    public int Volume {
        get => volume;
        set {
            volume = value;
            OnSetVolume();
        }
    }

    /// <summary>
    /// if the emulator is muted
    /// </summary>
    public bool IsMuted {
        get => isMuted;
        set {
            isMuted = value;
            OnSetIsMuted();
        }
    }

    /// <summary>
    /// the emulator speed as a percentage
    /// </summary>
    public int SpeedPercent {
        get => speedPercent;
        set {
            speedPercent = value;
            OnSetSpeedPercent();
        }
    }
    
    /// the emulator log level (for unity-side logs)
    public Logger.LogLevel LogLevel {
        get => logLevel;
        set {
            logLevel = value;
            OnSetLogLevel();
        }
    }

    /// <summary>
    /// Currently displayed emulator texture.
    /// </summary>
    /// <remarks>
    /// If emulator is not running but a savestate is set, will return the savestate preview texture
    /// </remarks>
    public Texture Texture => IsRunning ? renderTexture : saveStateFile?.Screenshot;

    /// <summary>
    /// is the emulator process currently starting up?
    /// </summary>
    public bool IsStarting => CurrentStatus == Status.Starting;

    /// <summary>
    /// is the emulator process started?
    /// </summary>
    public bool IsStarted => CurrentStatus >= Status.Started;

    /// <summary>
    /// is the emulator process running a game?
    /// </summary>
    public bool IsRunning => CurrentStatus >= Status.Running;

    /// <summary>
    /// ID of the current emulator platform (e.g. "N64", "PSX", etc.)
    /// Returns null if emulator is not running.
    /// </summary>
    public string SystemId => _systemId;

    /// <summary>
    /// when the emulator boots up
    /// </summary>
    /// <remarks>
    /// Will be a slight delay since this gets deferred to main thread Update
    /// </remarks>
    public Action OnStarted;

    /// <summary>
    /// when the emulator starts running a game
    /// </summary>
    /// <remarks>
    /// Will be a slight delay since this gets deferred to main thread Update
    /// </remarks>
    public Action OnRunning;
 
    /// <summary>
    /// possible values for the emulator status
    /// </summary>
    public enum Status {
        /// BizHawk hasn't started yet
        /// </summary>
        Inactive,

        /// <summary>
        /// The BizHawk process is starting up but not started yet
        /// </summary>
        Starting,

        /// <summary>
        /// BizHawk has been started, but not rendering yet
        /// </summary>
        Started,

        /// <summary>
        /// Bizhawk is running and sending textures [technically gets set when shared texture channel is open]
        /// </summary>
        Running
    }

    /// <summary>
    /// the current status of the emulator
    /// </summary>
    public Status CurrentStatus {
        get => _status;
        private set {
            if (_status != value) {
                _logger.LogVerbose($"Emulator status changed from {_status} to {value}", this);
                var raise = value switch {
                    Status.Started => OnStarted,
                    Status.Running => OnRunning,
                    _ => null,
                };

                _deferredForMainThread += () => raise?.Invoke();
            }
            _status = value;
            status = value; // Serialized value for displaying in inspector
        }
    }

    /// <summary>
    /// frame index of latest received texture
    /// </summary>
    public int CurrentFrame => _currentFrame;

    /// <summary>
    /// restarts the emulator
    /// </summary>
    [Button]
    public void Restart() {
        Deactivate();
        Initialize();
    }

    /// <summary>
    /// delegate for registering lua callbacks
    /// </summary>
    // TODO: string-to-string only rn but some automatic de/serialization for different types would be nice

    public delegate string LuaCallback(string arg);

    /// <summary>
    /// Register a callback that can be called via `unityhawk.callmethod('MethodName', argString)` in BizHawk lua
    /// </summary>
    /// <param name="methodName">The name of the method to register</param>
    /// <param name="luaCallback">The Unity-side callback</param>
    public void RegisterLuaCallback(string methodName, LuaCallback luaCallback) {
        if (SpecialCommands.All.Contains(methodName)) {
            _logger.LogWarning($"Tried to register a Lua callback for reserved method name '{methodName}', this will not work!", this);
            return;
        }
        _registeredLuaCallbacks[methodName] = luaCallback;
    }

    /// <summary>
    /// [Obsolete: use RegisterLuaCallback instead]
    /// Register a callback that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    /// </summary>
    [Obsolete("use RegisterLuaCallback instead")]
    public void RegisterMethod(string methodName, LuaCallback luaCallback) => RegisterLuaCallback(methodName, luaCallback);

    ///// Bizhawk API methods

    /// <summary>
    /// pauses the emulator
    /// </summary>
    public void Pause() {
        ThrowIfNotRunning();
        IsPaused = true;
    }

    /// <summary>
    /// unpauses the emulator
    /// </summary>
    public void Unpause() {
        ThrowIfNotRunning();
        IsPaused = false;
    }

    /// <summary>
    /// mutes the emulator (and disables sound engine)
    /// </summary>
    public void Mute() {
        ThrowIfNotRunning();
        IsMuted = true;
    }

    /// <summary>
    /// unmutes the emulator (and enables sound engine)
    /// </summary>
    public void Unmute() {
        ThrowIfNotRunning();
        IsMuted = false;
    }

    /// <summary>
    /// sets the emulator volume
    /// </summary>
    public void SetVolume(int value) {
        ThrowIfNotRunning();
        volume = value;
    }

    /// <summary>
    /// sets the speed of the emulator as integer percentage
    /// </summary>
    /// <remarks>
    /// This is the 'target' speed for the emulator - in reality might be slower if cpu-constrained
    /// </remarks>
    public void SetSpeedPercent(int percent) {
        ThrowIfNotRunning();
        speedPercent = percent;
    }

    /// <summary>
    /// saves a state to a given path
    /// </summary>
    /// <param name="path"></param>
    // TODO: how can this return a savestate object?
    public string SaveState(string path) {
        ThrowIfNotRunning();
        path = Paths.GetFullPath(path);
        if (!path.Contains(".savestate"))
        {
            path += ".savestate";
        }

        _apiCommandBuffer.CallMethod(ApiCommands.SaveState, path);

        // TODO: create savestate asset here, async?
        return path;
    }

    /// <summary>
    /// loads a state from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadState(string path) {
        ThrowIfNotRunning();
        // TODO: set emulator savestateFile?
        path = Paths.GetFullPath(path);
        _apiCommandBuffer.CallMethod(ApiCommands.LoadState, path);
    }

    /// <summary>
    /// loads a state from a Savestate asset
    /// </summary>
    /// <param name="sample"></param>
    public void LoadState(Savestate sample) {
        ThrowIfNotRunning();
        string path = Paths.GetAssetPath(sample);
        if (path == null) {
            _logger.LogError($"Savestate {sample} not found", this);
            return;
        }

        LoadState(path);
    }

    /// <summary>
    /// reloads the current state
    /// </summary>
    public void ReloadState() {
        ThrowIfNotRunning();
        LoadState(saveStateFile);
    }

    /// <summary>
    /// loads a rom from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadRom(string path) {
        ThrowIfNotRunning();
        path = Paths.GetFullPath(path);

        if (string.IsNullOrEmpty(path)) {
            _logger.LogWarning("[emulator] attempting to load rom with invalid path, ignoring...", this);
            return;
        }

        if (_status == Status.Inactive) {
            return;
        }

        // TODO: set emulator romFile?
        _apiCommandBuffer.CallMethod(ApiCommands.LoadRom, path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();

        CurrentStatus = Status.Started; // Not running until new texture buffer is set up
    }

    /// <summary>
    /// loads a rom from a Rom asset
    /// </summary>
    /// <param name="rom"></param>
    public void LoadRom(Rom rom) {
        ThrowIfNotRunning();
        LoadRom(Paths.GetAssetPath(rom));
    }

    /// <summary>
    /// advances a frame on the emulator
    /// </summary>
    public void FrameAdvance() {
        ThrowIfNotRunning();
        _apiCommandBuffer.CallMethod(ApiCommands.FrameAdvance, null);
    }

    ///// RAM read/write
    // For all methods, domain defaults to main memory if not specified

    // ReadXXX methods have thread-safety issues so disabled for now, use WatchXXX instead
    // public uint? ReadUnsigned(long address, int size, bool isBigEndian, string domain = null) {
    //     string args = $"{address},{size},{isBigEndian}";
    //     if (domain != null) {
    //         args += $",{domain}";
    //     }
    //     string v = _apiCallRpcBuffer.CallMethod("ReadUnsigned", args);
    //     return (v == null) ? null : uint.Parse(v);
    // }
    // public int? ReadSigned(long address, int size, bool isBigEndian, string domain = null) {
    //     string args = $"{address},{size},{isBigEndian}";
    //     if (domain != null) {
    //         args += $",{domain}";
    //     }
    //     string v = _apiCallRpcBuffer.CallMethod("ReadSigned", args);
    //     return (v == null) ? null : int.Parse(v);
    // }
    // public float? ReadFloat(long address, bool isBigEndian, string domain = null) {
    //     string args = $"{address},{isBigEndian}";
    //     if (domain != null) {
    //         args += $",{domain}";
    //     }
    //     string v = _apiCallRpcBuffer.CallMethod("ReadFloat", args);
    //     return (v == null) ? null : float.Parse(v);
    // }

    // WatchXXX methods allow you to register a callback that will be called whenever the watched value changes
    // These methods return an int id which can be used later with Unwatch(id)

    /// <summary>
    /// Watch an unsigned integer value in emulated memory at a given address
    /// </summary>
    /// <param name="address">The address to watch</param>
    /// <param name="size">The size of the value to watch</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to watch. If null, defaults to main memory.</param>
    /// <param name="onChanged">The callback to call when the value changes</param>
    /// <returns>The id of the watch, can be used with Unwatch(id) to stop watching</returns>
    public int WatchUnsigned(long address, int size, bool isBigEndian, string domain, Action<uint> onChanged) {
        ThrowIfNotRunning();
        return Watch(WatchType.Unsigned, address, size, isBigEndian, domain, value => {
            if (uint.TryParse(value, out uint result)) {
                onChanged(result);
            } else {
                _logger.LogError($"Failed to parse unsigned value from Bizhawk watch: {value}", this);
            }
        });
    }

    /// <summary>
    /// Watch a signed integer value in emulated memory at a given address
    /// </summary>
    /// <param name="address">The address to watch</param>
    /// <param name="size">The size of the value to watch</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to watch. If null, defaults to main memory.</param>
    /// <param name="onChanged">The callback to call when the value changes</param>
    /// <returns>The id of the watch, can be used with Unwatch(id) to stop watching</returns>
    public int WatchSigned(long address, int size, bool isBigEndian, string domain, Action<int> onChanged) {
        ThrowIfNotRunning();
        return Watch(WatchType.Signed, address, size, isBigEndian, domain, value => {
            if (int.TryParse(value, out int result)) {
                onChanged(result);
            } else {
                _logger.LogError($"Failed to parse signed value from Bizhawk watch: {value}", this);
            }
        });
    }

    /// <summary>
    /// Watch a float value in emulated memory at a given address
    /// </summary>
    /// <param name="address">The address to watch</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to watch. If null, defaults to main memory.</param>
    /// <param name="onChanged">The callback to call when the value changes</param>
    /// <returns>The id of the watch, can be used with Unwatch(id) to stop watching</returns>
    public int WatchFloat(long address, bool isBigEndian, string domain, Action<float> onChanged) {
        ThrowIfNotRunning();
        return Watch(WatchType.Float, address, 4, isBigEndian, domain, value => {
            if (float.TryParse(value, out float result)) {
                onChanged(result);
            } else {
                _logger.LogError($"Failed to parse float value from Bizhawk watch: {value}", this);
            }
        });
    }

    private int Watch(WatchType type, long address, int size, bool isBigEndian, string domain, Action<string> onChanged) {
        ThrowIfNotRunning();
        string args = $"{address},{size},{isBigEndian},{type}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Watch", args);
        var key = (address, size, isBigEndian, type, domain);
        var hashCode = key.GetHashCode();
        if (_watchCallbacks.ContainsKey(hashCode)) {
            _logger.LogWarning($"Overwriting existing watch for key {key}", this);
        }
        _watchCallbacks[hashCode] = (key, onChanged);
        return hashCode;
    }

    /// <summary>
    /// Unwatch a value that was previously watched
    /// </summary>
    /// <param name="id">The id of the watch to stop</param>
    public void Unwatch(int id) {
        ThrowIfNotRunning();
        // Unregister the callback first
        if (_watchCallbacks.ContainsKey(id)) {
            var (key, _) = _watchCallbacks[id];
            var (address, size, isBigEndian, type, domain) = key;
            string args = $"{address},{size},{isBigEndian},{type}";
            if (domain != null) {
                args += $",{domain}";
            }
            _apiCommandBuffer.CallMethod("Unwatch", args);
            _watchCallbacks.Remove(id);
        } else {
            _logger.LogWarning($"Unwatch called for id {id} that was not being watched.", this);
        }
    }

    /// <summary>
    /// Sets an unsigned integer value at a given address
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    /// <param name="size">The size of the value to write</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to write to. If null, defaults to main memory.</param>
    /// <remarks>
    /// Only sets the value for a single frame - to freeze the address, use Freeze
    /// </remarks>
    public void WriteUnsigned(long address, uint value, int size, bool isBigEndian, string domain = null) {
        ThrowIfNotRunning();
        string args = $"{address},{value},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteUnsigned", args);
    }

    /// <summary>
    /// Sets a signed integer value at a given address
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    /// <param name="size">The size of the value to write</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to write to. If null, defaults to main memory.</param>
    /// <remarks>
    /// Only sets the value for a single frame - to freeze the address, use Freeze
    /// </remarks>
    public void WriteSigned(long address, int value, int size, bool isBigEndian, string domain = null) {
        ThrowIfNotRunning();
        string args = $"{address},{value},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteSigned", args);
    }

    /// <summary>
    /// Sets a float value at a given address
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    /// <param name="isBigEndian">Whether the value is big endian</param>
    /// <param name="domain">The domain to write to. If null, defaults to main memory.</param>
    /// <remarks>
    /// Only sets the value for a single frame - to freeze the address, use Freeze
    /// </remarks>
    public void WriteFloat(long address, float value, bool isBigEndian, string domain = null) {
        ThrowIfNotRunning();
        string args = $"{address},{value},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteFloat", args);
    }

    /// <summary>
    /// Freezes an address in emulated memory
    /// </summary>
    /// <param name="address">The address to freeze</param>
    /// <param name="size">The size of the value to freeze (1-4 bytes)</param>
    /// <param name="domain">The memory domain. If null, defaults to main memory.</param>
    public void Freeze(long address, int size, string domain = null) {
        ThrowIfNotRunning();
        string args = $"{address},{size}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Freeze", args);
    }

    /// <summary>
    /// Unfreezes an address that was previously frozen
    /// </summary>
    /// <param name="address">The address to unfreeze</param>
    /// <param name="size">The size of the value to unfreeze (1-4 bytes)</param>
    /// <param name="domain">The memory domain. If null, defaults to main memory.</param>
    public void Unfreeze(long address, int size, string domain = null) {
        ThrowIfNotRunning();
        string args = $"{address},{size}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Unfreeze", args);
    }

    ///// events
    /// (These don't throw exception when emulator not running,
    ///  cause it's still valid to set these fields in the inspector)

    /// when the volume changes
    void OnSetVolume() {
        if (!IsRunning) return;
        _apiCommandBuffer.CallMethod(ApiCommands.SetVolume, $"{volume}");
    }

    /// when the sound is muted or unmuted
    void OnSetIsMuted() {
        if (!IsRunning) return;
        _apiCommandBuffer.CallMethod(ApiCommands.SetSoundOn, $"{!isMuted}");
    }

    /// when the speed percent changes
    void OnSetSpeedPercent() {
        if (!IsRunning) return;
        _apiCommandBuffer.CallMethod(ApiCommands.SetSpeedPercent, $"{speedPercent}");
    }

    /// when emulator is paused or unpaused
    void OnSetIsPaused() {
        if (!IsRunning) return;
        string command = IsPaused ? ApiCommands.Pause : ApiCommands.Unpause;
        _apiCommandBuffer.CallMethod(command, null);
    }

    /// when the log level changes
    void OnSetLogLevel() {
        _logger.MinLogLevel = logLevel;
    }

    ///// error handling
    void ThrowIfNotRunning() {
        if (!IsRunning) {
            throw new InvalidOperationException("Emulator is not running");
        }
    }
}
}