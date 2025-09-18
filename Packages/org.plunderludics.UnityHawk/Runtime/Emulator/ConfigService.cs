using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using BizHawkConfig = BizHawk.Client.Common.Config;
using BizHawkConfigService = BizHawk.Client.Common.ConfigService;

namespace UnityHawk {

internal static class ConfigService {
    public static BizHawkConfig Load(string path) {
        JsonSerializerSettings settings = new () {
            Error = (sender, error) => error.ErrorContext.Handled = true,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            ConstructorHandling = ConstructorHandling.Default,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new DefaultContractResolver {
#pragma warning disable CS0618 // DefaultMembersSearchFlags is obsolete, but this code is copied from BizHawk and we want to keep it identical
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
}

}