using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
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
        var newdll = Path.Combine(path, "UIRes", "SharpCompress.dll");
        var olddll = Path.Combine(path, "..", "..", "..", "app-6.2.45-beta2", "SharpCompress.dll");
        var newconf = Path.Combine(path, "UIRes", "XIVLauncherCN.exe.config");
        var oldconf = Path.Combine(path, "..", "..", "..", "app-6.2.45-beta2", "XIVLauncherCN.exe.config");
        var newcommon = Path.Combine(path, "UIRes", "XIVLauncher.Common.dll");
        var oldcommon = Path.Combine(path, "..", "..", "..", "app-6.2.45-beta2", "XIVLauncher.Common.dll");
        if (!File.Exists(newdll) || !File.Exists(olddll) || !File.Exists(newconf) || !File.Exists(newcommon))
        {
            Log.Error($"Cant Find Files:{File.Exists(newdll)} || {File.Exists(olddll)} || {File.Exists(newconf)} || {File.Exists(newcommon)}");
            return;
        }
        
        if (FileVersionInfo.GetVersionInfo(olddll).FileVersion != FileVersionInfo.GetVersionInfo(newdll).FileVersion)
        {
            try
            {
                var process = Process.GetProcessesByName("XIVLauncherCN").FirstOrDefault();
                if (process != null)
                {
                    process.Kill();
                    if (process.WaitForExit(3000))
                    {
                        File.Copy(newdll, olddll, true);
                        File.Copy(newconf, oldconf, true);
                        File.Copy(newcommon, oldcommon, true);
                        Log.Error($"Files Changed.");
                    }
                }
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
