// Public methods for the Emulator component
// Including methods for interfacing with the BizHawk API (loading/saving states, etc)

using System;
using UnityEngine;

using NaughtyAttributes;
using System.Linq;

namespace UnityHawk {
public partial class Emulator {
    ///// props
    [Header("api")]
    [OnValueChanged(nameof(OnSetVolume))]
    [Range(0, 100)]
    [Tooltip("the the volume of the emulator, 0-100")]
    [SerializeField] int volume = 100;

    [OnValueChanged(nameof(OnSetIsMuted))]
    [Tooltip("if the emulator is muted")]
    [SerializeField] bool isMuted;

    [OnValueChanged(nameof(OnSetIsPaused))]
    [Tooltip("if the emulator is paused")]
    [SerializeField] bool isPaused;

    /// if the emulator is paused
    public bool IsPaused {
        get => isPaused;
        set {
            isPaused = value;
            OnSetIsPaused();
        }
    }

    /// the emulator current volume
    public int Volume => volume;

    /// the emulator current volume
    public bool IsMuted => isMuted;

    /// Currently displayed emulator texture.
    /// (If emulator is not running but savestate is set, show savestate texture)
    public Texture Texture => IsRunning ? renderTexture : saveStateFile?.Screenshot;

    /// is the emulator process started
    public bool IsStarted => Status >= EmulatorStatus.Started;

    /// is the emulator process running a game? (best guess, might be wrong)
    public bool IsRunning => Status >= EmulatorStatus.Running; // is the emuhawk.exe process running? (best guess, might be wrong)

    /// ID of the current emulator platform (e.g. "N64", "PSX", etc.)
    /// Returns null if emulator is not running.
    public string SystemId => _systemId;

    /// the current status of the emulator
    public enum EmulatorStatus {
        /// BizHawk hasn't started yet
        Inactive,

        /// BizHawk has been started, but not rendering yet
        Started,

        /// Bizhawk is running and sending textures [technically gets set when shared texture channel is open]
        Running
    }

    /// the current status of the emulator
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

    /// .
    public int CurrentFrame => _currentFrame;

    /// delegate for registering lua callbacks
    // TODO: string-to-string only rn but some automatic de/serialization for different types would be nice
    public delegate string LuaCallback(string arg);

    /// Register a callback that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    public void RegisterLuaCallback(string methodName, LuaCallback luaCallback) {
        if (SpecialCommands.All.Contains(methodName)) {
            Debug.LogWarning($"Tried to register a Lua callback for reserved method name '{methodName}', this will not work!");
            return;
        }
        _registeredLuaCallbacks[methodName] = luaCallback;
    }

    /// Register a method that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    [Obsolete("use RegisterLuaCallback instead")]
    public void RegisterMethod(string methodName, LuaCallback luaCallback) {
        RegisterLuaCallback(methodName, luaCallback);
    }

    ///// Bizhawk API methods
    // For LoadState/SaveState/LoadRom, path should be relative to StreamingAssets (same as for rom/savestate/lua params in the inspector)
    // can also pass absolute path (but this will most likely break in build!)

    /// calls the emulator api to pause/unpause
    void OnSetIsPaused() {
        string command = isPaused ? ApiCommands.Pause : ApiCommands.Unpause;
        _apiCommandBuffer.CallMethod(command, null);
    }

    /// pauses the emulator
    public void Pause() {
        IsPaused = true;
    }

    /// unpauses the emulator
    public void Unpause() {
        IsPaused = false;
    }

    /// saves a state to a given path
    /// <param name="path"></param>
    /// TODO: how can this return a savestate object?
    public string SaveState(string path) {
        path = Paths.GetFullPath(path);
        if (!path.Contains(".savestate"))
        {
            path += ".savestate";
        }

        _apiCommandBuffer.CallMethod(ApiCommands.SaveState, path);

        // TODO: create savestate asset here, async?
        return path;
    }

    /// loads a state from a given path
    /// <param name="path"></param>
    public void LoadState(string path) {
        // TODO: set emulator savestateFile?
        path = Paths.GetFullPath(path);
        _apiCommandBuffer.CallMethod(ApiCommands.LoadState, path);
    }

    /// loads a state from a Savestate asset
    /// <param name="sample"></param>
    public void LoadState(Savestate sample) {
        string path = Paths.GetAssetPath(sample);
        if (path == null) {
            Debug.LogError($"Savestate {sample} not found");
            return;
        }

        LoadState(path);
    }

    /// reloads the current state
    /// <param name="path"></param>
    public void ReloadState() {
        LoadState(saveStateFile);
    }

    /// loads a rom from a given path
    /// <param name="path"></param>
    public void LoadRom(string path) {
        path = Paths.GetFullPath(path);

        if (string.IsNullOrEmpty(path)) {
            Debug.LogWarning("[emulator] attempting to load rom with invalid path, ignoring...");
            return;
        }

        if (_status == EmulatorStatus.Inactive) {
            return;
        }

        // TODO: set emulator romFile?
        _apiCommandBuffer.CallMethod(ApiCommands.LoadRom, path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();

        Status = EmulatorStatus.Started; // Not running until new texture buffer is set up
    }

    /// loads a rom from a Rom asset
    /// <param name="rom"></param>
    public void LoadRom(Rom rom) {
        LoadRom(Paths.GetAssetPath(rom));
    }

    /// advances a frame on the emulator
    public void FrameAdvance() {
        _apiCommandBuffer.CallMethod(ApiCommands.FrameAdvance, null);
    }

    /// <summary>
    /// sets the emulator volume
    /// </summary>
    public void SetVolume(int value) {
        volume = value;
    }

    /// <summary>
    /// Sets the speed of the emulator as integer percentage
    /// </summary>
    public void SetSpeedPercent(int percent) {
        _apiCommandBuffer.CallMethod(ApiCommands.SetSpeedPercent, $"{percent}");
    }

    ///// RAM read/write
    /// For all methods, domain defaults to main memory if not specified

    // ReadXXX methodshave type-safety issues so disabled for now, use WatchXXX instead
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

    // WatchXXX methods allow you to register a callback that will be called after each bizhawk frame with the value of the memory address
    // These methods return an int id which can be used later with Unwatch(id)
    // (I guess in theory it could be that the callback only gets called when the value changes, but it's just every frame for now)
    public int WatchUnsigned(long address, int size, bool isBigEndian, string domain, Action<uint> callback) {
        return Watch(WatchType.Unsigned, address, size, isBigEndian, domain, value => {
            if (uint.TryParse(value, out uint result)) {
                callback(result);
            } else {
                Debug.LogError($"Failed to parse unsigned value from Bizhawk watch: {value}");
            }
        });
    }

    public int WatchSigned(long address, int size, bool isBigEndian, string domain, Action<int> callback) {
        return Watch(WatchType.Signed, address, size, isBigEndian, domain, value => {
            if (int.TryParse(value, out int result)) {
                callback(result);
            } else {
                Debug.LogError($"Failed to parse signed value from Bizhawk watch: {value}");
            }
        });
    }

    public int WatchFloat(long address, bool isBigEndian, string domain, Action<float> callback) {
        return Watch(WatchType.Float, address, 4, isBigEndian, domain, value => {
            if (float.TryParse(value, out float result)) {
                callback(result);
            } else {
                Debug.LogError($"Failed to parse float value from Bizhawk watch: {value}");
            }
        });
    }

    private int Watch(WatchType type, long address, int size, bool isBigEndian, string domain, Action<string> callback) {
        string args = $"{address},{size},{isBigEndian},{type}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Watch", args);
        var key = (address, size, isBigEndian, type, domain);
        var hashCode = key.GetHashCode();
        if (_watchCallbacks.ContainsKey(hashCode)) {
            Debug.LogWarning($"Overwriting existing watch for key {key}");
        }
        _watchCallbacks[hashCode] = (key, callback);
        return hashCode;
    }

    public void Unwatch(int id) {
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
            Debug.LogWarning($"Unwatch called for id {id} that was not being watched.");
        }
    }

    /// Sets a memory address to a given value (for a single frame - to freeze the address, use FreezeBytes)
    public void WriteUnsigned(long address, uint value, int size, bool isBigEndian, string domain = null) {
        string args = $"{address},{value},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteUnsigned", args);
    }
    public void WriteSigned(long address, int value, int size, bool isBigEndian, string domain = null) {
        string args = $"{address},{value},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteSigned", args);
    }
    public void WriteFloat(long address, float value, bool isBigEndian, string domain = null) {
        string args = $"{address},{value},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("WriteFloat", args);
    }

    /// Freezes a memory address for a given size (1-4 bytes)
    public void Freeze(long address, int size, string domain = null) {
        string args = $"{address},{size}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Freeze", args);
    }

    /// Unfreezes a memory address that was previously frozen
    public void Unfreeze(long address, int size, string domain = null) {
        string args = $"{address},{size}";
        if (domain != null) {
            args += $",{domain}";
        }
        _apiCommandBuffer.CallMethod("Unfreeze", args);
    }

    ///// events
    /// when the volume changes
    void OnSetVolume() {
        _apiCommandBuffer.CallMethod(ApiCommands.SetVolume, $"{volume}");
    }

    /// when the sound is muted
    void OnSetIsMuted() {
        _apiCommandBuffer.CallMethod(ApiCommands.SetSoundOn, $"{!isMuted}");
    }
}
}