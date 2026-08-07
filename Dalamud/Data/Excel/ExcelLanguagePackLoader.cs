using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Dalamud.Common.Game;
using Dalamud.Logging.Internal;

using Lumina;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Excel;

using Newtonsoft.Json;

#pragma warning disable SA1600 // Internal loader implementation.

namespace Dalamud.Data.Excel;

internal sealed class ExcelLanguagePackLoader
{
    internal const int CurrentFormatVersion = 2;
    internal const string ManifestFileName = "language-pack.json";

    private static readonly ModuleLog Log = ModuleLog.Create<ExcelLanguagePackLoader>();
    private static readonly Lazy<LuminaCacheAccessor> CacheAccessor = new();

    private bool englishLoaded;

    public bool HasLoadedPacks => this.englishLoaded;

    public static ExcelLanguagePackLoader LoadFromAssetDirectory(
        DirectoryInfo assetDirectory,
        GameData primaryGameData,
        GameVersion? gameVersion)
    {
        var loader = new ExcelLanguagePackLoader();
        var packsDirectory = Path.Combine(assetDirectory.FullName, "UIRes", "ExcelLanguagePacks");
        if (!Directory.Exists(packsDirectory))
        {
            Log.Debug("Excel language pack directory is not present: {Directory}", packsDirectory);
            return loader;
        }

        string[] manifests;
        try
        {
            manifests = Directory.GetFiles(packsDirectory, ManifestFileName, SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not enumerate Excel language packs in {Directory}", packsDirectory);
            return loader;
        }

        foreach (var manifestPath in manifests.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                loader.LoadPack(manifestPath, primaryGameData, gameVersion);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not load Excel language pack manifest {Manifest}", manifestPath);
            }
        }

        return loader;
    }

    public bool IsLanguageLoaded(Language language) =>
        this.englishLoaded && language == Language.English;

    internal static string ResolveContainedPath(string packageDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("The language pack path cannot be empty.");
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("The language pack path must be relative to its manifest.");

        var packageRoot = Path.GetFullPath(packageDirectory);
        var fullPath = Path.GetFullPath(relativePath, packageRoot);
        var relativeToRoot = Path.GetRelativePath(packageRoot, fullPath);
        if (relativeToRoot == ".." ||
            relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToRoot))
        {
            throw new InvalidDataException("The language pack path escapes its package directory.");
        }

        return fullPath;
    }

    internal static void ValidateLuminaCacheContract() => _ = CacheAccessor.Value;

    internal static ExcelLanguagePackManifest ReadManifest(string manifestPath)
    {
        var manifest = JsonConvert.DeserializeObject<ExcelLanguagePackManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("The language pack manifest is empty.");
        if (manifest.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported language pack format {manifest.FormatVersion}.");
        if (manifest.Source != "csv-overlay")
            throw new InvalidDataException($"Unsupported language pack source '{manifest.Source}'.");
        if (manifest.Languages.Count != 1 ||
            !string.Equals(manifest.Languages[0], "English", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CSV overlay manifest must provide only English.");
        }

        var packageDirectory = Path.GetDirectoryName(manifestPath)
                               ?? throw new InvalidDataException("The language pack manifest has no parent directory.");
        _ = ResolveContainedPath(packageDirectory, manifest.Path);
        return manifest;
    }

    private void LoadPack(string manifestPath, GameData primaryGameData, GameVersion? gameVersion)
    {
        var manifest = ReadManifest(manifestPath);
        if (gameVersion is null ||
            !string.Equals(manifest.GameVersion, gameVersion.ToString(), StringComparison.Ordinal))
        {
            Log.Warning(
                "Skipping Excel language pack {PackId}: built for {PackVersion}, running {GameVersion}",
                manifest.Id,
                manifest.GameVersion,
                gameVersion?.ToString() ?? "unknown");
            return;
        }

        if (this.englishLoaded)
        {
            Log.Warning(
                "English Excel data is already supplied by an earlier pack; ignoring {PackId}",
                manifest.Id);
            return;
        }

        var packageDirectory = Path.GetDirectoryName(manifestPath)!;
        using var stream = File.OpenRead(ResolveContainedPath(packageDirectory, manifest.Path));
        var pack = CsvOverlayPack.Read(stream);
        if (!string.Equals(pack.Manifest.GameVersion, manifest.GameVersion, StringComparison.Ordinal))
            throw new InvalidDataException("The CSV overlay game version does not match its manifest.");

        var count = CacheAccessor.Value.InjectCsvOverlay(primaryGameData.Excel, pack);
        if (count == 0)
        {
            Log.Warning("Skipping Excel language pack {PackId}: no compatible sheets were found", manifest.Id);
            return;
        }

        var fallbackCount = CacheAccessor.Value.FillEnglishFallback(primaryGameData.Excel);
        this.englishLoaded = true;
        Log.Information(
            "Injected {SheetCount} English Excel sheets from language pack {PackId}; {FallbackCount} sheets use Simplified Chinese fallback",
            count,
            manifest.Id,
            fallbackCount);
    }

    private sealed class LuminaCacheAccessor
    {
        private readonly PropertyInfo definedSheetCacheProperty;
        private readonly FieldInfo headerFileField;
        private readonly FieldInfo languageCacheField;
        private readonly CsvOverlayRawSheetFactory rawSheetFactory = new();

        public LuminaCacheAccessor()
        {
            this.definedSheetCacheProperty = typeof(ExcelModule)
                                             .GetProperties(
                                                 BindingFlags.Instance |
                                                 BindingFlags.Public |
                                                 BindingFlags.NonPublic)
                                             .Single(
                                                 property => property.PropertyType.IsGenericType &&
                                                             property.PropertyType.GetGenericTypeDefinition() ==
                                                             typeof(FrozenDictionary<,>) &&
                                                             property.PropertyType.GenericTypeArguments[0] ==
                                                             typeof(string));

            var sheetDataType = this.definedSheetCacheProperty.PropertyType.GenericTypeArguments[1];
            var instanceFields = sheetDataType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            this.headerFileField = instanceFields.Single(field => field.FieldType == typeof(ExcelHeaderFile));
            this.languageCacheField = instanceFields.Single(
                field => field.FieldType.IsArray &&
                         field.FieldType.GetElementType() is { IsGenericType: true } elementType &&
                         elementType.GetGenericTypeDefinition() == typeof(Lazy<>));
        }

        public int InjectCsvOverlay(ExcelModule module, CsvOverlayPack pack)
        {
            var primarySheets = this.GetDefinedSheets(module);
            var originalLanguageCaches = new Dictionary<object, Array>();
            var injectedCount = 0;
            var schemaMismatchCount = 0;
            try
            {
                foreach (var (sheetName, overlay) in pack.Sheets)
                {
                    if (!primarySheets.TryGetValue(sheetName, out var sheetData))
                        continue;

                    var header = (ExcelHeaderFile)this.headerFileField.GetValue(sheetData)!;
                    if (!CsvOverlayRawSheetFactory.IsCompatible(header, overlay.Definition))
                    {
                        schemaMismatchCount++;
                        continue;
                    }

                    var languageCache = (Array)this.languageCacheField.GetValue(sheetData)!;
                    var chineseIndex = (int)Language.ChineseSimplified;
                    if (chineseIndex >= languageCache.Length ||
                        languageCache.GetValue(chineseIndex) is not Lazy<RawExcelSheet> chineseSheet)
                    {
                        continue;
                    }

                    var englishSheet = new Lazy<RawExcelSheet>(
                        () => this.rawSheetFactory.Create(
                            module,
                            header,
                            Language.English,
                            chineseSheet.Value,
                            overlay));
                    if (this.TrySetEnglishCache(sheetData, englishSheet, originalLanguageCaches))
                        injectedCount++;
                }
            }
            catch
            {
                foreach (var (sheetData, originalCache) in originalLanguageCaches)
                    this.languageCacheField.SetValue(sheetData, originalCache);
                throw;
            }

            if (schemaMismatchCount > 0)
            {
                Log.Warning(
                    "Skipped {SheetCount} CSV overlay sheets because their string schemas differ from the primary client",
                    schemaMismatchCount);
            }

            return injectedCount;
        }

        public int FillEnglishFallback(ExcelModule module)
        {
            var fallbackCount = 0;
            foreach (var sheetData in this.GetDefinedSheets(module).Values)
            {
                lock (sheetData)
                {
                    var languageCache = (Array)this.languageCacheField.GetValue(sheetData)!;
                    var chineseIndex = (int)Language.ChineseSimplified;
                    if (chineseIndex >= languageCache.Length ||
                        languageCache.GetValue(chineseIndex) is not { } chineseSheet)
                    {
                        continue;
                    }

                    var englishIndex = (int)Language.English;
                    if (englishIndex < languageCache.Length &&
                        languageCache.GetValue(englishIndex) is not null)
                    {
                        continue;
                    }

                    languageCache = this.EnsureLanguageCacheSize(
                        sheetData,
                        languageCache,
                        englishIndex + 1);
                    languageCache.SetValue(chineseSheet, englishIndex);
                    fallbackCount++;
                }
            }

            return fallbackCount;
        }

        private Dictionary<string, object> GetDefinedSheets(ExcelModule module)
        {
            var cache = (IEnumerable)this.definedSheetCacheProperty.GetValue(module)!;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var entry in cache)
            {
                var entryType = entry.GetType();
                var key = (string)entryType.GetProperty("Key")!.GetValue(entry)!;
                var value = entryType.GetProperty("Value")!.GetValue(entry)!;
                result.Add(key, value);
            }

            return result;
        }

        private bool TrySetEnglishCache(
            object sheetData,
            Lazy<RawExcelSheet> sheet,
            IDictionary<object, Array> originalLanguageCaches)
        {
            lock (sheetData)
            {
                var englishIndex = (int)Language.English;
                var languageCache = (Array)this.languageCacheField.GetValue(sheetData)!;
                if (englishIndex < languageCache.Length &&
                    languageCache.GetValue(englishIndex) is not null)
                {
                    return false;
                }

                originalLanguageCaches.TryAdd(sheetData, (Array)languageCache.Clone());
                languageCache = this.EnsureLanguageCacheSize(
                    sheetData,
                    languageCache,
                    englishIndex + 1);
                languageCache.SetValue(sheet, englishIndex);
                return true;
            }
        }

        private Array EnsureLanguageCacheSize(object sheetData, Array languageCache, int requiredLength)
        {
            if (languageCache.Length >= requiredLength)
                return languageCache;

            var expandedCache = Array.CreateInstance(
                languageCache.GetType().GetElementType()!,
                requiredLength);
            Array.Copy(languageCache, expandedCache, languageCache.Length);
            this.languageCacheField.SetValue(sheetData, expandedCache);
            return expandedCache;
        }
    }
}
