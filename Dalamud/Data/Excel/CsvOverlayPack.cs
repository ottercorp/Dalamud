using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

using Newtonsoft.Json;

#pragma warning disable SA1600 // Internal generated-pack model.

namespace Dalamud.Data.Excel;

internal sealed class CsvOverlayPack
{
    private const int CurrentFormatVersion = 1;

    private CsvOverlayPack(
        CsvOverlayPackManifest manifest,
        IReadOnlyDictionary<string, CsvOverlaySheet> sheets)
    {
        this.Manifest = manifest;
        this.Sheets = sheets;
    }

    public CsvOverlayPackManifest Manifest { get; }

    public IReadOnlyDictionary<string, CsvOverlaySheet> Sheets { get; }

    public static CsvOverlayPack Read(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = ReadJson<CsvOverlayPackManifest>(archive, "pack.json");
        if (manifest.FormatVersion != CurrentFormatVersion ||
            manifest.Language != "English" ||
            manifest.Variant != "lite")
        {
            throw new InvalidDataException("The CSV overlay pack has unsupported metadata.");
        }

        var sheets = new Dictionary<string, CsvOverlaySheet>(StringComparer.Ordinal);
        foreach (var definition in manifest.Sheets)
        {
            var content = ReadJson<CsvOverlaySheetContent>(archive, definition.Entry);
            if (content.FormatVersion != CurrentFormatVersion)
                throw new InvalidDataException($"CSV overlay sheet '{definition.Name}' has an unsupported format.");
            sheets.Add(definition.Name, new CsvOverlaySheet(definition, content.Rows));
        }

        return new CsvOverlayPack(manifest, sheets);
    }

    private static T ReadJson<T>(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
                    ?? throw new InvalidDataException($"CSV overlay entry '{entryName}' is missing.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        return JsonSerializer.CreateDefault().Deserialize<T>(jsonReader)
               ?? throw new InvalidDataException($"CSV overlay entry '{entryName}' is empty.");
    }
}

internal sealed class CsvOverlayPackManifest
{
    [JsonProperty("formatVersion", Required = Required.Always)]
    public int FormatVersion { get; set; }

    [JsonProperty("gameVersion", Required = Required.Always)]
    public string GameVersion { get; set; } = string.Empty;

    [JsonProperty("language", Required = Required.Always)]
    public string Language { get; set; } = string.Empty;

    [JsonProperty("variant", Required = Required.Always)]
    public string Variant { get; set; } = string.Empty;

    [JsonProperty("sheets", Required = Required.Always)]
    public List<CsvOverlaySheetDefinition> Sheets { get; set; } = [];
}

internal sealed class CsvOverlaySheetDefinition
{
    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("entry", Required = Required.Always)]
    public string Entry { get; set; } = string.Empty;

    [JsonProperty("columnTypes", Required = Required.Always)]
    public List<string> ColumnTypes { get; set; } = [];

    [JsonProperty("variant", Required = Required.Always)]
    public string Variant { get; set; } = string.Empty;
}

internal sealed class CsvOverlaySheetContent
{
    [JsonProperty("formatVersion", Required = Required.Always)]
    public int FormatVersion { get; set; }

    [JsonProperty("rows", Required = Required.Always)]
    public List<CsvOverlayRow> Rows { get; set; } = [];
}

internal sealed record CsvOverlaySheet(
    CsvOverlaySheetDefinition Definition,
    IReadOnlyList<CsvOverlayRow> Rows);

internal sealed class CsvOverlayRow
{
    [JsonProperty("rowId", Required = Required.Always)]
    public uint RowId { get; set; }

    [JsonProperty("cells", Required = Required.Always)]
    public List<CsvOverlayCell> Cells { get; set; } = [];
}

internal sealed class CsvOverlayCell
{
    [JsonProperty("column", Required = Required.Always)]
    public int Column { get; set; }

    [JsonProperty("value", Required = Required.Always)]
    public string Value { get; set; } = string.Empty;
}
