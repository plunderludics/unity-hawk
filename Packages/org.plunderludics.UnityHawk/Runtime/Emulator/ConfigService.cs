using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using BizHawkConfig = BizHawk.Client.Common.Config;
using BizHawkConfigService = BizHawk.Client.Common.ConfigService;

namespace UnityHawk {

public static class ConfigService {
    public static BizHawkConfig Load(string path) {
        JsonSerializerSettings settings = new () {
            Error = (sender, error) => error.ErrorContext.Handled = true,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            ConstructorHandling = ConstructorHandling.Default,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new DefaultContractResolver {
                DefaultMembersSearchFlags = (BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            }
        };

        var serializer = JsonSerializer.Create(settings);
        BizHawkConfigService.SetSerializer(serializer);

        return BizHawkConfigService.Load<BizHawkConfig>(path);
    }

    public static void Save(string path, BizHawkConfig config) {
        BizHawkConfigService.Save(path, config);
    }
}

}