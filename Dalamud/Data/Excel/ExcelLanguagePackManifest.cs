using System.Collections.Generic;

using Newtonsoft.Json;

#pragma warning disable SA1600 // Internal asset manifest model.

namespace Dalamud.Data.Excel;

internal sealed class ExcelLanguagePackManifest
{
    [JsonProperty("formatVersion", Required = Required.Always)]
    public int FormatVersion { get; set; }

    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("gameVersion", Required = Required.Always)]
    public string GameVersion { get; set; } = string.Empty;

    [JsonProperty("source", Required = Required.Always)]
    public string Source { get; set; } = string.Empty;

    [JsonProperty("path", Required = Required.Always)]
    public string Path { get; set; } = string.Empty;

    [JsonProperty("languages", Required = Required.Always)]
    public List<string> Languages { get; set; } = [];
}
