// Public methods for the Emulator component
// Including methods for interfacing with the BizHawk API (loading/saving states, etc)

using System;
using UnityEngine;

using NaughtyAttributes;
using BizHawkConfig = BizHawk.Client.Common.Config;

namespace UnityHawk {

public partial class Emulator {
	[Header("api")]
	[OnValueChanged(nameof(OnSetVolume))]
	[Range(0, 100)]
	[Tooltip("the the volume of the emulator, 0-100")]
	[SerializeField] int volume = 100;

	/// the volume of the emulator, 0-100
	public int Volume {
		get => volume;
		set {
			volume = value;
			OnSetVolume();
		}
	}

	[OnValueChanged(nameof(OnSetIsMuted))]
	[Tooltip("if the emulator is muted")]
	[SerializeField] bool isMuted;

	/// if the emulator is muted
	public bool IsMuted {
		get => isMuted;
		set {
			isMuted = value;
			OnSetIsMuted();
		}
	}

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

	/// the internal render texture
	public RenderTexture Texture => renderTexture;

	/// is the emuhawk.exe process started? (best guess, might be wrong)
    public bool IsStarted => Status == EmulatorStatus.Started;

	/// is the emuhawk.exe process running a game? (best guess, might be wrong)
    public bool IsRunning => Status == EmulatorStatus.Running;

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

    /// sets the config default values
    void SetConfigDefaults(ref BizHawkConfig bizConfig) {
	    bizConfig.SoundVolume = volume;
	    bizConfig.StartPaused = IsPaused;
	    bizConfig.SoundEnabled = !isMuted;
    }

    /// .
    public int CurrentFrame => _currentFrame;

    /// delegate for registering lua callbacks
    // TODO: string-to-string only rn but some automatic de/serialization for different types would be nice
    public delegate string LuaCallback(string arg);

    /// Register a callback that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    public void RegisterLuaCallback(string methodName, LuaCallback luaCallback) {
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
	    var method = IsPaused ? "Pause" : "Unpause";
		_apiCallBuffer.CallMethod(method, null);
    }

    /// pauses the emulator
    public void Pause() {
		IsPaused = true;
    }

    /// unpauses the emulator
    public void Unpause() {
        _apiCallBuffer.CallMethod("Unpause", null);
    }

    [Obsolete("use Volume setter instead")]
    public void SetVolume(int volume) {
	    Volume = volume;
    }

    /// calls the emulator api to set volume
    void OnSetVolume() {
		_apiCallBuffer.CallMethod("SetVolume", $"{volume}");
    }

    /// calls the emulator api to set sound on/off
    void OnSetIsMuted() {
        _apiCallBuffer.CallMethod("SetSoundOn", $"{!isMuted}");
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
        _apiCallBuffer.CallMethod("SaveState", path);

        // TODO: create savestate asset here, async?
        return path;
    }

    /// loads a state from a given path
    /// <param name="path"></param>
    public void LoadState(string path) {
        // TODO: set emulator savestateFile?
        path = Paths.GetFullPath(path);

        if (_status == EmulatorStatus.Inactive) return;
        _apiCallBuffer.CallMethod("LoadState", path);
    }

    /// loads a state from a Savestate asset
    /// <param name="sample"></param>
    public void LoadState(Savestate sample) {
        LoadState(Paths.GetAssetPath(sample));
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
        _apiCallBuffer.CallMethod("LoadRom", path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();
        _status = EmulatorStatus.Started; // Not ready until new texture buffer is set up
    }

    /// loads a rom from a Rom asset
    /// <param name="rom"></param>
    public void LoadRom(Rom rom) {
        LoadRom(Paths.GetAssetPath(rom));
    }

    /// advances a frame on the emulator
    public void FrameAdvance() {
        _apiCallBuffer.CallMethod("FrameAdvance");
    }

	/// initializes the emulator
	public void Initialize() {
		Debug.Log("initialiazing!", this);
		if (_initialized) {
			Debug.LogWarning("attempting to initialize already initialized emulator", this);
			return;
		}

		_Initialize();
	}
}
}