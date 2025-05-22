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
        _apiCommandBuffer.CallMethod("Pause", null);
    }

    /// <summary>
    /// unpauses the emulator
    /// </summary>
    public void Unpause() {
        _apiCommandBuffer.CallMethod("Unpause", null);
    }

    /// <summary>
    /// unpauses the emulator
    /// </summary>
    public void SetVolume(float volume) {
        _apiCommandBuffer.CallMethod("SetVolume", $"{volume}");
    }

    /// <summary>
    /// saves a state to a given path
    /// </summary>
    /// <param name="path"></param>
    /// TODO: how can this return a savestate object?
    public void SaveState(string path) {
        path = Paths.GetFullPath(path);
        _apiCommandBuffer.CallMethod("SaveState", path);
    }

    /// <summary>
    /// loads a state from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadState(string path) {
        // TODO: set emulator savestateFile?
        path = Paths.GetFullPath(path);

        if (_status == EmulatorStatus.Inactive) return;
        _apiCommandBuffer.CallMethod("LoadState", path);
    }

    /// <summary>
    /// loads a state from a Savestate asset
    /// </summary>
    /// <param name="sample"></param>
    public void LoadState(Savestate sample) {
        LoadState(Paths.GetAssetPath(sample));
        // TODO would be nice if there was some calllback or way to know when state is loaded
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
        _apiCommandBuffer.CallMethod("LoadRom", path);
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
        _apiCommandBuffer.CallMethod("FrameAdvance", null);
    }

    /// get platform of currently loaded rom, or "NULL"
    /// Warning: Can block for a long time is rom is not open
    /// TODO: async version..?
    public string GetSystemId() {
        return _apiCallRpcBuffer.CallMethod("GetSystemId");
    }

    ///// RAM read/write
    /// For all methods, domain defaults to main memory if not specified
    
    public uint? ReadUnsigned(long address, int size, bool isBigEndian, string domain = null) {
        string args = $"{address},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        string v = _apiCallRpcBuffer.CallMethod("ReadUnsigned", args);
        return (v == null) ? null : uint.Parse(v);
    }
    public int? ReadSigned(long address, int size, bool isBigEndian, string domain = null) {
        string args = $"{address},{size},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        string v = _apiCallRpcBuffer.CallMethod("ReadSigned", args);
        return (v == null) ? null : int.Parse(v);
    }
    public float? ReadFloat(long address, bool isBigEndian, string domain = null) {
        string args = $"{address},{isBigEndian}";
        if (domain != null) {
            args += $",{domain}";
        }
        string v = _apiCallRpcBuffer.CallMethod("ReadFloat", args);
        return (v == null) ? null : float.Parse(v);
    }

    // // Sets a memory address to a given value (for a single frame - to freeze the address, use FreezeBytes)
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

    /// Freezes a memory address for a given size (1, 2, or 4 bytes)
    public void Freeze(long address, int size, string domain = null) {
        if (size != 1 && size != 2 && size != 4)
            throw new ArgumentException("Size must be 1, 2, or 4 bytes.", nameof(size));
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

    // public void UnfreezeBytes(long address, int size, string domain = null);
}
}