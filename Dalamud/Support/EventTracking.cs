using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Internal;
using Dalamud.Utility;
using Dalamud.Networking.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dalamud.Support;

/// <summary>
/// Class responsible for sending feedback.
/// </summary>
internal static class EventTracking
{

    private const string AnalyticsUrl = "https://aonyx.ffxiv.wang/Dalamud/Analytics/Start";

    public static async Task SendMeasurement(ulong contentId, uint actorId, uint homeWorldId)
    {
        var httpClient = Service<HappyHttpClient>.Get().SharedHttpClient;
        var bilibiliIP = await httpClient.GetStringAsync("https://api.bilibili.com/x/web-interface/zone");
        var json = JObject.Parse(bilibiliIP);
        var ip = json["data"]?["addr"]?.ToString() ?? "IP获取失败";
        var clientId = $"{Hash.GetStringSha256Hash(ip)}";
        var userId = Hash.GetStringSha256Hash($"{contentId:x16}+{actorId:X8}");
        var cheatBannedHash = CheatBannedHash();
        var os = Util.IsWine() ? "Wine" : "Windows";
        var version = $"{Util.AssemblyVersion}-{Util.GetGitHash()}";
        var pluginManager = Service<PluginManager>.GetNullable();
        var count = pluginManager is null ? -1 : pluginManager.InstalledPlugins.Count(x => x.Manifest.InstalledFromUrl is not "OFFICIAL");
        var installedPlugins = pluginManager is null
                                   ? []
                                   : pluginManager.InstalledPlugins.Select(x => x.InternalName).ToList();

        var data = new Analytics()
        {
            CheatBannedHash = cheatBannedHash,
            ClientId = clientId,
            ServerId = homeWorldId.ToString(),
            UserId = userId,
            OS = os,
            DalamudVersion = version,
            PluginCount = count.ToString(),
            PluginList = installedPlugins,
        };

        var postContent = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(AnalyticsUrl, postContent);
        response.EnsureSuccessStatusCode();
    }

    private static string CheatBannedHash()
    {
        var cheatPluginsJson = File.ReadAllText(Path.Combine(Service<Dalamud>.Get().AssetDirectory!.FullName, "UIRes", "cheatplugin.json"));
        var cheatPluginsHash = Hash.GetStringSha256Hash(cheatPluginsJson);
        return cheatPluginsHash;
    }

    private class Analytics
    {
        [JsonProperty("client_id")]
        public string? ClientId { get; set; }

        [JsonProperty("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("server_id")]
        public string? ServerId { get; set; }

        [JsonProperty("cheat_banned_hash")]
        public string? CheatBannedHash { get; set; }

        [JsonProperty("os")]
        public string? OS { get; set; }

        [JsonProperty("dalamud_version")]
        public string? DalamudVersion { get; set; }

        [JsonProperty("plugin_count")]
        public string? PluginCount { get; set; }

        [JsonProperty("plugin_list")]
        public List<string>? PluginList { get; set; }
    }
}
