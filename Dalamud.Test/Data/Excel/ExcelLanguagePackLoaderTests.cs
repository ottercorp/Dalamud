using System;
using System.IO;

using Dalamud.Data;
using Dalamud.Data.Excel;
using Dalamud.Game;

using Lumina.Data;

using Xunit;

namespace Dalamud.Test.Data.Excel;

public sealed class ExcelLanguagePackLoaderTests
{
    private const string ValidManifest = """
        {
          "formatVersion": 2,
          "id": "en-lite",
          "gameVersion": "2026.08.05.0000.0000",
          "source": "csv-overlay",
          "path": "en-lite.xlcsvpack",
          "languages": [ "English" ],
          "variant": "lite",
          "profileVersion": 2,
          "files": [
            {
              "path": "en-lite.xlcsvpack",
              "size": 123,
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            }
          ]
        }
        """;

    [Fact]
    public void ResolveContainedPath_ChildPath_ReturnsFullPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "language-pack");

        var actual = ExcelLanguagePackLoader.ResolveContainedPath(root, "en-lite.xlcsvpack");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "en-lite.xlcsvpack")), actual);
    }

    [Theory]
    [InlineData("../en-lite.xlcsvpack")]
    [InlineData("sub/../../en-lite.xlcsvpack")]
    public void ResolveContainedPath_ParentTraversal_Throws(string relativePath)
    {
        var root = Path.Combine(Path.GetTempPath(), "language-pack");

        Assert.Throws<InvalidDataException>(
            () => ExcelLanguagePackLoader.ResolveContainedPath(root, relativePath));
    }

    [Fact]
    public void ResolveContainedPath_RootedPath_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "language-pack");

        Assert.Throws<InvalidDataException>(
            () => ExcelLanguagePackLoader.ResolveContainedPath(root, Path.GetPathRoot(root)!));
    }

    [Fact]
    public void ValidateLuminaCacheContract_CurrentLuminaShape_IsSupported()
    {
        ExcelLanguagePackLoader.ValidateLuminaCacheContract();
        _ = new LuminaRawSheetFactory();
    }

    [Fact]
    public void ReadManifest_ValidGeneratedManifest_ReturnsCsvOverlay()
    {
        var path = this.WriteManifest(ValidManifest);
        try
        {
            var manifest = ExcelLanguagePackLoader.ReadManifest(path);

            Assert.Equal("en-lite", manifest.Id);
            Assert.Equal("csv-overlay", manifest.Source);
            Assert.Equal("en-lite.xlcsvpack", manifest.Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"formatVersion\": 2", "\"formatVersion\": 3")]
    [InlineData("\"source\": \"csv-overlay\"", "\"source\": \"xlpack\"")]
    [InlineData("\"languages\": [ \"English\" ]", "\"languages\": [ \"Japanese\" ]")]
    public void ReadManifest_UnsupportedContract_Throws(string oldValue, string newValue)
    {
        var path = this.WriteManifest(ValidManifest.Replace(oldValue, newValue));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExcelLanguagePackLoader.ReadManifest(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadManifest_SourceTraversal_Throws()
    {
        var path = this.WriteManifest(
            ValidManifest.Replace(
                "\"path\": \"en-lite.xlcsvpack\"",
                "\"path\": \"../en-lite.xlcsvpack\""));
        try
        {
            Assert.Throws<InvalidDataException>(() => ExcelLanguagePackLoader.ReadManifest(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, false, Language.ChineseSimplified)]
    [InlineData(ClientLanguage.ChineseSimplified, false, Language.ChineseSimplified)]
    [InlineData(ClientLanguage.English, false, Language.ChineseSimplified)]
    [InlineData(ClientLanguage.English, true, Language.English)]
    public void SelectExcelLanguage_UsesInstalledLanguageOrChineseFallback(
        ClientLanguage? requested,
        bool isInstalled,
        Language expected)
    {
        var actual = DataManager.SelectExcelLanguage(requested, _ => isInstalled);

        Assert.Equal(expected, actual);
    }

    private string WriteManifest(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        return path;
    }
}
