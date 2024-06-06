// Public methods for the Emulator component
// Including methods for interfacing with the BizHawk API (loading/saving states, etc)

using UnityEngine;
using Plunderludics;
using System.Collections.Generic;
using System.Linq;

namespace UnityHawk {

public partial class Emulator : MonoBehaviour
{
    ///// Public methods
    // Register a method that can be called via `unityhawk.callmethod('MethodName')` in BizHawk lua
    public void RegisterMethod(string methodName, Method method)
    {
        if (_registeredMethods == null) {
            _registeredMethods = new Dictionary<string, Method>();
            // This will never get cleared when running in edit mode but maybe that's fine
        }
        _registeredMethods[methodName] = method;
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

    ///// Bizhawk API methods
    ///// [should maybe move these into a Emulator.BizhawkApi subobject or similar]
    // For LoadState/SaveState/LoadRom, path should be relative to StreamingAssets (same as for rom/savestate/lua params in the inspector)
    // can also pass absolute path (but this will most likely break in build!)
    // TODO: should there be a version of these that uses DefaultAssets instead of paths? idk
    
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
    /// unpauses the emulator
    /// </summary>
    public void SetVolume(float volume) {
        _apiCallBuffer.CallMethod("SetVolume", $"{volume}");
    }
    
    /// <summary>
    /// loads a state from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadState(string path) {
        path = Paths.GetAssetPath(path);
        saveStateFileName = path;
        if (_status == EmulatorStatus.Inactive) return;
        _apiCallBuffer.CallMethod("LoadState", path);
    }
    
    /// <summary>
    /// saves a state to a given path
    /// </summary>
    /// <param name="path"></param>
    public void SaveState(string path) {
        path = Paths.GetAssetPath(path);
        if (!path.Contains(".State"))
        {
            path += ".State";
        }
        _apiCallBuffer.CallMethod("SaveState", path);
    }
    
    /// <summary>
    /// loads a rom from a given path
    /// </summary>
    /// <param name="path"></param>
    public void LoadRom(string path) {
        path = Paths.GetAssetPath(path);
        romFileName = path;
        if (_status == EmulatorStatus.Inactive) return;
        
        _apiCallBuffer.CallMethod("LoadRom", path);
        // Need to update texture buffer size in case platform has changed:
        _sharedTextureBuffer.UpdateSize();
        _status = EmulatorStatus.Started; // Not ready until new texture buffer is set up
    }

    public void LoadSample(string path)
    {
        Sample s = Sample.LoadFromDir(path);
        LoadRom(s.romPath);
        LoadState(s.saveStatePath);
        // TODO: lua / config?
    }
    
    public void FrameAdvance() {
        _apiCallBuffer.CallMethod("FrameAdvance", null);
    }
}
}