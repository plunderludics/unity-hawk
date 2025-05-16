// Public methods for the Emulator component
// Including methods for interfacing with the BizHawk API (loading/saving states, etc)

using System;
using UnityEngine;
using Plunderludics;
using System.IO;
using System.Linq;

namespace UnityHawk {

public partial class Emulator
{
    public RenderTexture Texture => renderTexture;
    public bool IsRunning => Status == EmulatorStatus.Running; // is the emuhawk.exe process running? (best guess, might be wrong)

    public enum EmulatorStatus {
        Inactive,
        Started, // Underlying bizhawk has been started, but not rendering yet
        Running  // Bizhawk is running and sending textures [technically gets set when shared texture channel is open]
    }
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

    /// TODO: string-to-string only rn but some automatic de/serialization for different types would be nice
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

    /// <summary>
    /// pauses the emulator
    /// </summary>
    public void Pause() {
        _apiCallBuffer.CallMethod("Pause", null);
    }

    /// <summary>
    /// unpauses the emulator
    /// </summary>
    public void Unpause() {
        _apiCallBuffer.CallMethod("Unpause", null);
    }

    /// <summary>
    /// set volume
    /// </summary>
    /// <param name="volume">0-100</param>
    public void SetVolume(int volume) {
        _apiCallBuffer.CallMethod("SetVolume", $"{volume}");
    }

    /// <summary>
    /// saves a state to a given path
    /// </summary>
    /// <param name="path"></param>
    /// TODO: how can this return a savestate object?
    public void SaveState(string path) {
        path = Paths.GetFullPath(path);
        if (!path.Contains(".savestate"))
        {
            path += ".savestate";
        }
        _apiCallBuffer.CallMethod("SaveState", path);
    }

    /// <summary>
    /// loads a state from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadState(string path) {
        // TODO: set emulator savestateFile?
        path = Paths.GetFullPath(path);

        if (_status == EmulatorStatus.Inactive) return;
        _apiCallBuffer.CallMethod("LoadState", path);
    }

    /// <summary>
    /// loads a state from a Savestate asset
    /// </summary>
    /// <param name="sample"></param>
    public void LoadState(Savestate sample) {
        LoadState(Paths.GetAssetPath(sample));
    }

    /// <summary>
    /// reloads the current state
    /// </summary>
    /// <param name="path"></param>
    public void ReloadState() {
        LoadState(saveStateFile);
    }

    /// <summary>
    /// loads a rom from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadRom(string path) {
        path = Paths.GetFullPath(path);

        if (_status == EmulatorStatus.Inactive) return;

        // TODO: set emulator romFile?
        _apiCallBuffer.CallMethod("LoadRom", path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();
        _status = EmulatorStatus.Started; // Not ready until new texture buffer is set up
    }

    /// <summary>
    /// loads a rom from a Rom asset
    /// </summary>
    /// <param name="rom"></param>
    public void LoadRom(Rom rom) {
        LoadRom(Paths.GetAssetPath(rom));
    }

    /// <summary>
    /// advances a frame on the emulator
    /// </summary>
    public void FrameAdvance() {
        _apiCallBuffer.CallMethod("FrameAdvance", null);
    }
    
    /// <summary>
    /// Memory writing methods
    /// </summary>
    /// <param name="addr"></param>
    /// <param name="value"></param>
    /// <param name="domain"></param>
    /// 
    void WriteFloat(long addr, double value, string domain = null);
    void WriteS8(long addr, int value, string domain = null);
    void WriteS16(long addr, int value, string domain = null);
    void WriteS24(long addr, int value, string domain = null);
    void WriteS32(long addr, int value, string domain = null);
    void WriteU8(long addr, uint value, string domain = null);
    void WriteU16(long addr, uint value, string domain = null);
    void WriteU24(long addr, uint value, string domain = null);
    void WriteU32(long addr, uint value, string domain = null);
}
}