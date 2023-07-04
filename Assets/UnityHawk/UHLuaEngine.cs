using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;
using System.IO;

using UnityEngine;

using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using BizHawk.Common.CollectionExtensions;

using NLua;

// [mostly adapted from LuaConsole.cs]
class UHLuaEngine {
    UHLuaLibraries _lua;
    IEmulator _emulator;

    static UHLuaEngine() {
        // don't love static constructors but this has to get called somewhere
        LuaSandbox.DefaultLogger = Debug.Log;
    }

    // Restart (or init) all scripts
    public void Restart(
        Config config,
        InputManager inputManager,
        IEmulator emulator,
        IGameInfo game,
        List<string> luaScriptPaths
    )
    {
        _emulator = emulator;
        List<LuaFile> runningScripts = new();

        // Things we need to do with the existing lua instance before we can make a new one
        if (_lua is not null)
        {
            if (_lua.IsRebootingCore)
            {
                // Even if the lua console is self-rebooting from client.reboot_core() we still want to re-inject dependencies
                _lua.Restart(emulator.ServiceProvider, config, emulator, game);
                return;
            }

            runningScripts = _lua.ScriptList.Where(lf => lf.Enabled).ToList();

            // we don't use runningScripts here as the other scripts need to be stopped too
            foreach (var file in _lua.ScriptList)
            {
                DisableScript(file);
            }
        }

        _lua?.Close();
        LuaFileList newScripts = new(luaScriptPaths.Select(
            path => new LuaFile(Path.Combine(Application.dataPath, path))
        ).ToArray(), onChanged: () => {});
        Debug.Log(newScripts);
        LuaFunctionList registeredFuncList = new(onChanged: () => {});
        _lua = new UHLuaLibraries(
            newScripts,
            registeredFuncList,
            emulator.ServiceProvider,
            new UHMainFormApi(),
            null, // DisplayManager
            inputManager,
            config,
            emulator,
            game,
            LogLuaObject);

        // [why does this use runningScripts from before? i don't get it]
        foreach (var file in /*runningScripts*/newScripts)
        {
            try
            {
                Debug.Log("attempting to spawn sandbox thread");
                LuaSandbox.Sandbox(file.Thread, () =>
                {
                    _lua.SpawnAndSetFileThread(file.Path, file);
                    LuaSandbox.CreateSandbox(file.Thread, Path.GetDirectoryName(file.Path));
                    Debug.Log("created sandbox");
                    file.State = LuaFile.RunState.Running;
                }, () =>
                {
                    Debug.LogWarning("Could not spawn thread");
                    file.State = LuaFile.RunState.Disabled;
                });
            }
            catch (Exception ex)
            {
                Debug.Log(ex);
            }
        }
    }

    public void ResumeScripts(bool includeFrameWaiters)
    {
        if (!_lua.ScriptList.Any()
            || _lua.IsUpdateSupressed
           /* || (MainForm.IsTurboing && !Config.RunLuaDuringTurbo) */)
        {
            return;
        }

        foreach (var lf in _lua.ScriptList.Where(static lf => lf.State is LuaFile.RunState.Running && lf.Thread is not null))
        {
            try
            {
                LuaSandbox.Sandbox(lf.Thread, () =>
                {
                    var prohibit = lf.FrameWaiting && !includeFrameWaiters;
                    if (!prohibit)
                    {
                        var (waitForFrame, terminated) = _lua.ResumeScript(lf);
                        if (terminated)
                        {
                            _lua.CallExitEvent(lf);
                            lf.Stop();
                            DetachRegisteredFunctions(lf);
                            // UpdateDialog();
                        }

                        lf.FrameWaiting = waitForFrame;
                    }
                }, () =>
                {
                    lf.Stop();
                    DetachRegisteredFunctions(lf);
                    // LuaListView.Refresh();
                });
            }
            catch (Exception ex)
            {
                Debug.Log(ex);
            }
        }

        // _messageCount = 0;
    }

    public void UpdateBefore() {
        ResumeScripts(false);
        if (!_lua.IsUpdateSupressed)
        {
            _lua.CallFrameBeforeEvent();
        }
    }

    public void UpdateAfter() {
        if (!_lua.IsUpdateSupressed)
        {
            _lua.CallFrameAfterEvent();
            ResumeScripts(true);
        }
    }

    // [copied from ConsoleLuaLibrary.cs]
    // Outputs the given lua object to unity debug log. Note: Can accept a LuaTable
    private void LogLuaObject(/*string separator, string terminator, */params object[] outputs)
    {
        string separator = "\t";
        string terminator = "\n";
        static string SerializeTable(LuaTable lti)
        {
            var keyObjs = lti.Keys;
            var valueObjs = lti.Values;
            if (keyObjs.Count != valueObjs.Count)
            {
                throw new ArgumentException(message: "each value must be paired with one key, they differ in number", paramName: nameof(lti));
            }

            var values = new object[keyObjs.Count];
            var kvpIndex = 0;
            foreach (var valueObj in valueObjs)
            {
                values[kvpIndex++] = valueObj;
            }

            return string.Concat(keyObjs.Cast<object>()
                .Select((kObj, i) => $"\"{kObj}\": \"{values[i]}\"\n")
                .Order());
        }

        var sb = new StringBuilder();

        void SerializeAndWrite(object output)
            => sb.Append(output switch
            {
                null => "nil",
                LuaTable table => SerializeTable(table),
                _ => output.ToString()
            });

        if (outputs == null || outputs.Length == 0 || (outputs.Length == 1 && outputs[0] is null))
        {
            sb.Append($"(no return){terminator}");
            return;
        }

        SerializeAndWrite(outputs[0]);
        for (int outIndex = 1, indexAfterLast = outputs.Length; outIndex != indexAfterLast; outIndex++)
        {
            sb.Append(separator);
            SerializeAndWrite(outputs[outIndex]);
        }

        if (!string.IsNullOrEmpty(terminator))
        {
            sb.Append(terminator);
        }

        Debug.Log(sb.ToString());
    }

    private void DetachRegisteredFunctions(LuaFile lf)
    {
        foreach (var nlf in _lua.RegisteredFunctions
            .Where(f => f.LuaFile == lf))
        {
            nlf.DetachFromScript();
        }
    }

    private void DisableScript(LuaFile file)
    {
        if (file.IsSeparator) return;

        file.State = LuaFile.RunState.Disabled;

        if (file.Thread is not null)
        {
            _lua.CallExitEvent(file);
            _lua.RegisteredFunctions.RemoveForFile(file, _emulator);
            file.Stop();
        }
    }
    
}