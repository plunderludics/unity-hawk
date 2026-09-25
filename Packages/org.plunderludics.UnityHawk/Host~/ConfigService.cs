using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using BizHawkConfig = BizHawk.Client.Common.Config;
using BizHawkConfigService = BizHawk.Client.Common.ConfigService;

namespace UnityHawk.Host {

public static class ConfigService {
    public static BizHawkConfig Load(string path) {
        JsonSerializerSettings settings = new JsonSerializerSettings {
            Error = (sender, error) => error.ErrorContext.Handled = true,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            ConstructorHandling = ConstructorHandling.Default,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new DefaultContractResolver {
#pragma warning disable CS0618
                DefaultMembersSearchFlags = (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
#pragma warning restore CS0618
            }
        };

        var serializer = JsonSerializer.Create(settings);
        BizHawkConfigService.SetSerializer(serializer);
        return BizHawkConfigService.Load<BizHawkConfig>(path);
    }

    public static void Save(string path, BizHawkConfig config) {
        BizHawkConfigService.Save(path, config);
    }

    public static void WriteRuntimeConfig(string sourcePath, string destPath, int volume, bool startPaused, bool soundEnabled, int speedPercent) {
        var bizConfig = Load(sourcePath);
        bizConfig.SoundVolume = volume;
        bizConfig.StartPaused = startPaused;
        bizConfig.SoundEnabled = soundEnabled;
        bizConfig.SpeedPercent = speedPercent;
        Save(destPath, bizConfig);
    }
}

}
