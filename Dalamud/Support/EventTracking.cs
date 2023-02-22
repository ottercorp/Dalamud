using System.Collections.Generic;
using System.Net;
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
    private const string ApiSecret = "CWTvRIdaTJuLmiZjAZ3L9w";
    private const string MeasurementId = "G-W3HJPGVM1J";
    private const string GA4Endpoint = $"https://www.google-analytics.com/mp/collect?measurement_id={MeasurementId}&api_secret={ApiSecret}";

    public enum MeasurementType
    {
        StartDalamud,
    }

    public static async Task SendMeasurement(MeasurementType type, ulong contentId, uint actorId, uint homeWorldId)
    {
        var httpClient = new HttpClient();
        var taobaoIpJs = await httpClient.GetStringAsync("https://www.taobao.com/help/getip.php");
        var ipSplits = taobaoIpJs.Split('"');
        var ip = ipSplits[1];
        var clientId = $"{Hash.GetStringSha256Hash(ip)}";
        var userId = Hash.GetStringSha256Hash($"{contentId:x16}+{actorId:X8}");
        var model = new MeasurementModel
        {
            ClientId = clientId,
            UserId = userId,
        };
        var homeWorldProperty = new MeasurementModel.UserProperty();
        homeWorldProperty.Value = $"{homeWorldId}";
        model.UserProperties.Add("HomeWorld", homeWorldProperty);

        switch (type)
        {
            case MeasurementType.StartDalamud:
                var userEvent = new MeasurementModel.Event();
                userEvent.Name = "start_dalamud";
                userEvent.Params.Add("server_id", $"{homeWorldId}");
                userEvent.Params.Add("engagement_time_msec", "100");
                userEvent.Params.Add("session_id", userId);
                model.Events.Add(userEvent);
                break;
            default:
                Log.Error($"Unknown MeasurementType:{type}");
                return;
        }

        var userAgent = Util.IsLinux() ? "Wine" : "Windows";
        userAgent = $"Dalamud/{Util.GetGitHash()} ({userAgent})";
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var postContent = new StringContent(JsonConvert.SerializeObject(model), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(GA4Endpoint, postContent);
        response.EnsureSuccessStatusCode();
    }

    private class MeasurementModel
    {
        [JsonProperty("client_id")]
        public string? ClientId { get; set; }

        [JsonProperty("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("user_properties")]
        public Dictionary<string, UserProperty> UserProperties { get; set; } = new();

        [JsonProperty("events")]
        public List<Event> Events { get; set; } = new();

        public class UserProperty
        {
            [JsonProperty("value")]
            public object? Value { get; set; }
        }

        public class Event
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("params")]
            public Dictionary<string, string> Params { get; set; } = new();
        }
    }
}
