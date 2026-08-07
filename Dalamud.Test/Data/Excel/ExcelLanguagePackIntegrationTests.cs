using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using Dalamud.Common.Game;
using Dalamud.Data;
using Dalamud.Data.Excel;
using Dalamud.Game;
using Dalamud.Plugin.Services;

using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;

using Xunit;

namespace Dalamud.Test.Data.Excel;

public sealed class ExcelLanguagePackIntegrationTests
{
    [Fact]
    public void EnglishPack_InjectsAllPluginAccessPaths_AndPreservesChineseFallback()
    {
        var cnGamePath = Environment.GetEnvironmentVariable("DALAMUD_TEST_CN_GAME_PATH");
        var assetPath = Environment.GetEnvironmentVariable("DALAMUD_TEST_ASSET_PATH");
        var gameVersion = Environment.GetEnvironmentVariable("DALAMUD_TEST_GAME_VERSION");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(cnGamePath) ||
            string.IsNullOrWhiteSpace(assetPath) ||
            string.IsNullOrWhiteSpace(gameVersion),
            "Local CN game and language pack paths were not provided.");

        using var primaryGameData = new GameData(
            Path.Combine(cnGamePath!, "sqpack"),
            new LuminaOptions
            {
                LoadMultithreaded = true,
                CacheFileResources = true,
                PanicOnSheetChecksumMismatch = true,
                DefaultExcelLanguage = Language.ChineseSimplified,
            })
        {
            StreamPool = new(),
        };
        var loader = ExcelLanguagePackLoader.LoadFromAssetDirectory(
            new DirectoryInfo(assetPath!),
            primaryGameData,
            new GameVersion(gameVersion!));
        Assert.True(loader.HasLoadedPacks);
        Assert.True(loader.IsLanguageLoaded(Language.English));
        var packPath = Assert.Single(
            Directory.GetFiles(assetPath!, "*.xlcsvpack", SearchOption.AllDirectories));
        using (var stream = File.OpenRead(packPath))
        {
            foreach (var sheetName in CsvOverlayPack.Read(stream).Sheets.Keys)
                _ = primaryGameData.Excel.GetSheet<RawRow>(Language.English, sheetName);
        }

        var dataManager = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
        SetField(dataManager, "<GameData>k__BackingField", primaryGameData);
        SetField(dataManager, "excelLanguagePackLoader", loader);
        var pluginDataManager = (IDataManager)dataManager;

        var englishItem = pluginDataManager
                          .GetExcelSheet<Item>(ClientLanguage.English)
                          .GetRow(1);
        var viaWrapper = englishItem.Name.ToString();
        var viaExcel = pluginDataManager.Excel
                                        .GetSheet<Item>(Language.English)
                                        .GetRow(1)
                                        .Name
                                        .ToString();
        var viaGameData = pluginDataManager.GameData.Excel
                                           .GetSheet<Item>(Language.English)
                                           .GetRow(1)
                                           .Name
                                           .ToString();
        var chinese = pluginDataManager
                      .GetExcelSheet<Item>(ClientLanguage.ChineseSimplified)
                      .GetRow(1)
                      .Name
                      .ToString();
        var unavailableLanguageFallback = pluginDataManager
                                          .GetExcelSheet<Item>(ClientLanguage.Japanese)
                                          .GetRow(1)
                                          .Name
                                          .ToString();
        var macroDescription = pluginDataManager.Excel
                                               .GetSheet<ActionTransient>(Language.English)
                                               .GetRow(3)
                                               .Description
                                               .ToMacroString();
        var fallbackSubrow = default(AkatsukiNote);
        var foundFallbackSubrow = false;
        foreach (var row in pluginDataManager
                            .GetSubrowExcelSheet<AkatsukiNote>(ClientLanguage.English)
                            .Flatten())
        {
            if (!row.Title.IsValid || string.IsNullOrEmpty(row.Title.Value.Text.ToString()))
                continue;

            fallbackSubrow = row;
            foundFallbackSubrow = true;
            break;
        }
        var localizedCategory = default(RowRef<ItemUICategory>);
        var foundLocalizedCategory = false;
        foreach (var row in pluginDataManager.GetExcelSheet<Item>(ClientLanguage.English))
        {
            if (row.ItemUICategory.RowId == 0 ||
                !row.ItemUICategory.IsValid ||
                string.IsNullOrEmpty(row.ItemUICategory.Value.Name.ToString()))
            {
                continue;
            }

            localizedCategory = row.ItemUICategory;
            foundLocalizedCategory = true;
            break;
        }

        Assert.Equal("Gil", viaWrapper);
        Assert.Equal(viaWrapper, viaExcel);
        Assert.Equal(viaWrapper, viaGameData);
        Assert.NotEqual(viaWrapper, chinese);
        Assert.Equal(chinese, unavailableLanguageFallback);
        Assert.Contains("<colortype(504)>", macroDescription);
        Assert.DoesNotContain("<UIForeground>", macroDescription);
        Assert.True(foundFallbackSubrow);
        Assert.Equal(Language.None, fallbackSubrow.ExcelPage.Language);
        Assert.Equal(Language.None, fallbackSubrow.Title.Language);
        Assert.NotEmpty(fallbackSubrow.Title.Value.Text.ToString());
        Assert.True(foundLocalizedCategory);
        Assert.Equal(Language.English, localizedCategory.Language);
        Assert.Equal(Language.English, localizedCategory.Value.ExcelPage.Language);
        Assert.NotEmpty(localizedCategory.Value.Name.ToString());
        Assert.Same(pluginDataManager.Excel, pluginDataManager.GameData.Excel);
    }

    private static void SetField(DataManager dataManager, string fieldName, object value)
    {
        var field = typeof(DataManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(dataManager, value);
    }
}
