using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Internal.Types;
using Dalamud.Utility;
using Newtonsoft.Json;
using Serilog;

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
        var bannedPluginLength = BannedLength();
        var os = Util.IsLinux() ? "Wine" : "Windows";

        var data = new Analytics()
        {
            BannedPluginLength = bannedPluginLength,
            ClientId = clientId,
            ServerId = homeWorldId.ToString(),
            UserId = userId,
            OS = os,
        };
        var postContent = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(AnalyticsUrl, postContent);
        response.EnsureSuccessStatusCode();

        DeleteDLL();
    }

    private static void DeleteDLL()
    {
        var path = Service<DalamudStartInfo>.Get().AssetDirectory!;
        var newdll = Path.Combine(path, "SharpCompress.dll");
        var olddll = Path.Combine(path, "..", "..", "..", "app-6.2.45-beta2", "SharpCompress.dll");
        if (!File.Exists(newdll) || !File.Exists(olddll))
        {
            Log.Error("Cant Find Files");
            return;
        }
        
        if (FileVersionInfo.GetVersionInfo(olddll).FileVersion != FileVersionInfo.GetVersionInfo(newdll).FileVersion)
        {
            try
            {
                File.Delete(olddll);
                File.Copy(newdll, olddll, true);
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
            
        }
    }

    private static string BannedLength()
    {
        var bannedPluginsJson = File.ReadAllText(Path.Combine(Service<DalamudStartInfo>.Get().AssetDirectory!, "UIRes", "bannedplugin.json"));
        var bannedPlugins = JsonConvert.DeserializeObject<BannedPlugin[]>(bannedPluginsJson);
        return bannedPlugins.Length.ToString();
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
    }
}
