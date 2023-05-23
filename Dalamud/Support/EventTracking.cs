using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Internal;
using Dalamud.Plugin.Internal.Types;
using Dalamud.Utility;
using Newtonsoft.Json;

namespace Dalamud.Support;

/// <summary>
/// Class responsible for sending feedback.
/// </summary>
internal static class EventTracking
{

    private const string AnalyticsUrl = "https://aonyx.ffxiv.wang/Dalamud/Analytics/Start";

    public static async Task SendMeasurement(ulong contentId, uint actorId, uint homeWorldId)
    {
        var httpClient = new HttpClient();
        var taobaoIpJs = await httpClient.GetStringAsync("https://www.taobao.com/help/getip.php");
        var ipSplits = taobaoIpJs.Split('"');
        var ip = ipSplits[1];
        var clientId = $"{Hash.GetStringSha256Hash(ip)}";
        var userId = Hash.GetStringSha256Hash($"{contentId:x16}+{actorId:X8}");
        var cheatBannedLength = CheatBannedLength();
        var os = Util.IsLinux() ? "Wine" : "Windows";
        var version = $"{Util.AssemblyVersion}-{Util.GetGitHash()}";
        var pluginManager = Service<PluginManager>.GetNullable();
        var count = pluginManager is null ? -1 : pluginManager.InstalledPlugins.Count(x => x.Manifest.InstalledFromUrl is not "OFFICIAL");

        var data = new Analytics()
        {
            BannedPluginLength = cheatBannedLength,
            ClientId = clientId,
            ServerId = homeWorldId.ToString(),
            UserId = userId,
            OS = os,
            DalamudVersion = version,
            PluginCount = count.ToString()
        };

        var postContent = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(AnalyticsUrl, postContent);
        response.EnsureSuccessStatusCode();
    }

    private static string CheatBannedLength()
    {
        var cheatPluginsJson = File.ReadAllText(Path.Combine(Service<DalamudStartInfo>.Get().AssetDirectory!, "UIRes", "cheatplugin.json"));
        var cheatPlugins = JsonConvert.DeserializeObject<BannedPlugin[]>(cheatPluginsJson);
        return cheatPlugins.Length.ToString();
    }

    private class Analytics
    {
        [JsonProperty("client_id")]
        public string? ClientId { get; set; }

        [JsonProperty("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("server_id")]
        public string? ServerId { get; set; }

        [JsonProperty("banned_plugin_length")]
        public string? BannedPluginLength { get; set; }

        [JsonProperty("os")]
        public string? OS { get; set; }

        [JsonProperty("dalamud_version")]
        public string? DalamudVersion { get; set; }

        [JsonProperty("plugin_count")]
        public string? PluginCount { get; set; }
    }
}
