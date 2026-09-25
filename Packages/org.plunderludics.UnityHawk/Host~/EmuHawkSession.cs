using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Plunderludics.UnityHawk.Shared;

namespace UnityHawk.Host {

public sealed class EmuHawkLaunch {
    public string ExePath;
    public bool UseMono;
    public string MonoPath;
    public string DllDir;
    public string RomPath;
    public string ConfigPath;
    public string SaveStatePath;
    public string RamWatchPath;
    public string LuaScriptPath;
    public string FirmwarePath;
    public string SavestatesDir;
    public string RamWatchDir;
    public string ExternalToolsDir;
    public string SavestateExtension = "savestate";
    public int Volume = 100;
    public bool StartPaused;
    public bool SoundEnabled = true;
    public int SpeedPercent = 100;
    public bool ShowGui;
    public bool ShareAudio;
    public bool ApplicationIsPlaying;
    public bool PassInputFromHost;
    public bool RunInEditMode;
    public bool AcceptBackgroundInput;
    public bool MuteInEditMode;
    public bool SuppressPopups = true;
    public CallMethodRpcBuffer.Callback RpcCallback;
}

public sealed class EmuHawkSession {
    public Process Process { get; private set; }
    public SharedTextureBuffer Texture { get; private set; }
    public SharedAudioBuffer Audio { get; private set; }
    public SharedInputBuffer Input { get; private set; }
    public CallMethodRpcBuffer CallMethod { get; private set; }
    public ApiCommandBuffer Api { get; private set; }
    public int SessionId { get; private set; }

    EmuHawkSession() {}

    public static EmuHawkSession Create(EmuHawkLaunch launch, IHostLog log) {
        if (launch == null) throw new ArgumentNullException(nameof(launch));
        if (string.IsNullOrEmpty(launch.RomPath)) throw new ArgumentException("RomPath is required", nameof(launch));
        log = log ?? NullHostLog.Instance;

        var session = new EmuHawkSession();
        session.SessionId = new Random().Next();
        int guid = session.SessionId;

        var process = new Process();
        process.StartInfo.UseShellExecute = false;
        var args = process.StartInfo.ArgumentList;
        if (launch.UseMono) {
            process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = launch.DllDir;
            process.StartInfo.EnvironmentVariables["MONO_PATH"] = launch.DllDir;
            process.StartInfo.FileName = launch.MonoPath;
            if (launch.ShowGui) {
                log.LogWarning("'Show Bizhawk Gui' is not supported on Mac'");
            }
            args.Add(launch.ExePath);
        } else {
            process.StartInfo.FileName = launch.ExePath;
            process.StartInfo.UseShellExecute = false;
        }

        args.Add(launch.RomPath);

        string tempConfigPath = Path.GetFullPath($"{Path.GetTempPath()}/unityhawk-config-{guid}.ini");
        ConfigService.WriteRuntimeConfig(
            launch.ConfigPath,
            tempConfigPath,
            launch.Volume,
            launch.StartPaused,
            launch.SoundEnabled,
            launch.SpeedPercent
        );
        args.Add($"--config={tempConfigPath}");

        if (launch.SaveStatePath != null) {
            args.Add($"--load-state={launch.SaveStatePath}");
        }
        if (launch.RamWatchPath != null) {
            args.Add($"--ram-watch-file={launch.RamWatchPath}");
        }
        if (launch.LuaScriptPath != null) {
            args.Add($"--lua={launch.LuaScriptPath}");
        }

        args.Add($"--savestate-extension={launch.SavestateExtension}");
        args.Add($"--savestates={launch.SavestatesDir}");
        args.Add($"--firmware={launch.FirmwarePath}");
        args.Add($"--save-ram-watch={launch.RamWatchDir}");

        if (!launch.ShowGui) {
            args.Add("--headless");
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        Dictionary<string, string> userData = new Dictionary<string, string> {
            [Args.TextureBuffer] = "",
            [Args.CallMethodRpc] = "",
            [Args.ApiCommandBuffer] = "",
            [Args.AudioRpc] = "",
            [Args.InputBuffer] = ""
        };

        var textureName = $"texture-{guid}";
        userData[Args.TextureBuffer] = textureName;
        session.Texture = new SharedTextureBuffer(textureName, log);

        var callName = $"call-method-{guid}";
        userData[Args.CallMethodRpc] = callName;
        session.CallMethod = new CallMethodRpcBuffer(callName, launch.RpcCallback, log);

        var apiName = $"api-command-{guid}";
        userData[Args.ApiCommandBuffer] = apiName;
        session.Api = new ApiCommandBuffer(apiName, log);

        if (launch.ShareAudio) {
            var audioName = $"audio-{guid}";
            userData[Args.AudioRpc] = audioName;
            session.Audio = new SharedAudioBuffer(audioName, log);
        }

        if (launch.MuteInEditMode && !launch.ApplicationIsPlaying) {
            args.Add("--mute=true");
        }

        if (launch.ApplicationIsPlaying) {
            if (launch.PassInputFromHost) {
                var inputName = $"input-{guid}";
                userData[Args.InputBuffer] = inputName;
                session.Input = new SharedInputBuffer(inputName, log);
                args.Add("--accept-background-input=false");
            } else {
                args.Add("--accept-background-input=true");
            }
        } else if (launch.RunInEditMode) {
            args.Add($"--accept-background-input={(launch.AcceptBackgroundInput ? "true" : "false")}");
        }

        if (launch.SuppressPopups) {
            args.Add("--suppress-popups");
        }

        string userDataArgs = string.Join(";", userData.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        args.Add($"--userdata={userDataArgs}");
        args.Add("--open-ext-tool-dll=UnityHawk");
        args.Add($"--ext-tools-dir={launch.ExternalToolsDir}");

        log.Log("Starting EmuHawk process");
        log.Log($"{launch.ExePath} {string.Join(" ", args)}");

        session.Process = process;
        return session;
    }

    public void Start() {
        Process.Start();
    }
}

}
