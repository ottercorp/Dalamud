namespace Dalamud.Utility;

/// <summary>
/// Central endpoint definitions for the CN distribution.
/// </summary>
public static class ServerAddress
{
    // The server address provided here is intended for distribution by OtterCorp only.
    // Any individuals not affiliated with this organization should modify the server address before distributing it.
    // Unauthorized server usage is prohibited.

    /// <summary>
    /// The CN service API root.
    /// </summary>
    public const string MainAddress = "https://aonyx.ffxiv.wang";

    /// <summary>
    /// The CN plugin image distribution root.
    /// </summary>
    public const string PluginImageAddress = "https://s3test.ffxiv.wang/plugindistd17";
}
