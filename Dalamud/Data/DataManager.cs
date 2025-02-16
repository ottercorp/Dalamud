using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.IoC.Internal;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Timing;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Newtonsoft.Json;
using Serilog;

namespace Dalamud.Data;

/// <summary>
/// This class provides data for Dalamud-internal features, but can also be used by plugins if needed.
/// </summary>
[PluginInterface]
[ServiceManager.EarlyLoadedService]
#pragma warning disable SA1015
[ResolveVia<IDataManager>]
#pragma warning restore SA1015
internal sealed class DataManager : IInternalDisposableService, IDataManager
{
    private readonly Thread luminaResourceThread;
    private readonly CancellationTokenSource luminaCancellationTokenSource;
    private readonly RsvResolver rsvResolver;

    [ServiceManager.ServiceConstructor]
    private DataManager(Dalamud dalamud)
    {
        this.Language = (ClientLanguage)dalamud.StartInfo.Language;

        this.rsvResolver = new();

        try
        {
            Log.Verbose("Starting data load...");
            
            using (Timings.Start("Lumina Init"))
            {
                var luminaOptions = new LuminaOptions
                {
                    LoadMultithreaded = true,
                    CacheFileResources = true,
                    PanicOnSheetChecksumMismatch = true,
                    RsvResolver = this.rsvResolver.TryResolve,
                    DefaultExcelLanguage = this.Language.ToLumina(),
                };

                this.GameData = new(
                    Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "sqpack"),
                    luminaOptions)
                {
                    StreamPool = new(),
                };

                Log.Information("Lumina is ready: {0}", this.GameData.DataPath);

                if (!dalamud.StartInfo.TroubleshootingPackData.IsNullOrEmpty())
                {
                    try
                    {
                        var tsInfo =
                            JsonConvert.DeserializeObject<LauncherTroubleshootingInfo>(
                                dalamud.StartInfo.TroubleshootingPackData);
                        this.HasModifiedGameDataFiles =
                            tsInfo?.IndexIntegrity is LauncherTroubleshootingInfo.IndexIntegrityResult.Failed or LauncherTroubleshootingInfo.IndexIntegrityResult.Exception;
                        
                        if (this.HasModifiedGameDataFiles)
                            Log.Verbose("Game data integrity check failed!\n{TsData}", dalamud.StartInfo.TroubleshootingPackData);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            this.IsDataReady = true;

            this.luminaCancellationTokenSource = new();

            var luminaCancellationToken = this.luminaCancellationTokenSource.Token;
            this.luminaResourceThread = new(() =>
            {
                while (!luminaCancellationToken.IsCancellationRequested)
                {
                    if (this.GameData.FileHandleManager.HasPendingFileLoads)
                    {
                        this.GameData.ProcessFileHandleQueue();
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }
            });
            this.luminaResourceThread.Start();
            this.ChangeWorldForCN();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not initialize Lumina");
            throw;
        }
    }

    /// <summary>
    /// 为国服服务器临时修正isPublic数据.
    /// </summary>
    private void ChangeWorldForCN()
    {
        var chineseWorldDCGroups = new[] {
                new
                {
                    Name = "陆行鸟",
                    Id   = 101u,
                    Worlds = new[]
                    {
                        new { Id = 1175u, Name = "晨曦王座" },
                        new { Id = 1174u, Name = "沃仙曦染" },
                        new { Id = 1173u, Name = "宇宙和音" },
                        new { Id = 1167u, Name = "红玉海"   },
                        new { Id = 1060u, Name = "萌芽池"   },
                        new { Id = 1081u, Name = "神意之地" },
                        new { Id = 1044u, Name = "幻影群岛" },
                        new { Id = 1042u, Name = "拉诺西亚" },
                    },
                },
                new
                {
                   Name = "莫古力",
                   Id   = 102u,
                   Worlds = new[]
                   {
                        new { Id = 1121u, Name = "拂晓之间" },
                        new { Id = 1166u, Name = "龙巢神殿" },
                        new { Id = 1113u, Name = "旅人栈桥" },
                        new { Id = 1076u, Name = "白金幻象" },
                        new { Id = 1176u, Name = "梦羽宝境" },
                        new { Id = 1171u, Name = "神拳痕"   },
                        new { Id = 1170u, Name = "潮风亭"   },
                        new { Id = 1172u, Name = "白银乡"   },
                   },
                },
                new
                {
                   Name = "猫小胖",
                   Id   = 103u,
                   Worlds = new[]
                   {
                        new { Id = 1179u, Name = "琥珀原"   },
                        new { Id = 1178u, Name = "柔风海湾" },
                        new { Id = 1177u, Name = "海猫茶屋" },
                        new { Id = 1169u, Name = "延夏"    },
                        new { Id = 1106u, Name = "静语庄园" },
                        new { Id = 1045u, Name = "摩杜纳"   },
                        new { Id = 1043u, Name = "紫水栈桥" },
                   },
                },
                new
                {
                   Name = "豆豆柴",
                   Id   = 104u,
                   Worlds = new[]
                   {
                        new { Id = 1201u, Name = "红茶川"    },
                        new { Id = 1186u, Name = "伊修加德"  },
                        new { Id = 1180u, Name = "太阳海岸"  },
                        new { Id = 1183u, Name = "银泪湖"    },
                        new { Id = 1192u, Name = "水晶塔"    },
                        new { Id = 1202u, Name = "萨雷安"    },
                        new { Id = 1203u, Name = "加雷马"    },
                        new { Id = 1200u, Name = "亚马乌罗提" },
                   },
                },
            };
        //var dcExcel = this.GameData.Excel.GetSheet<WorldDCGroupType>();
        var worldExcel = this.GameData.Excel.GetSheet<World>();
        foreach (var dc in chineseWorldDCGroups)
        {
            // var dcToReplaced = dcExcel.GetRow(dc.Id);
            // dcToReplaced.Name = new SeString(dc.Name);
            // dcToReplaced.Region = 5;

            foreach (var world in dc.Worlds)
            {
                var worldToUpdated = worldExcel.GetRow(world.Id);
                worldToUpdated.IsPublic = true;
                //worldToUpdated.DataCenter = new LazyRow<WorldDCGroupType>(this.GameData, dc.Id, Lumina.Data.Language.ChineseSimplified);
            }
        }

    }

    /// <summary>
    /// Gets the current game client language.
    /// </summary>
    /// <inheritdoc/>
    public ClientLanguage Language { get; private set; }

    /// <inheritdoc/>
    public GameData GameData { get; private set; }

    /// <inheritdoc/>
    public ExcelModule Excel => this.GameData.Excel;

    /// <inheritdoc/>
    public bool HasModifiedGameDataFiles { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Game Data is ready to be read.
    /// </summary>
    internal bool IsDataReady { get; private set; }

    #region Lumina Wrappers

    /// <inheritdoc/>
    public ExcelSheet<T> GetExcelSheet<T>(ClientLanguage? language = null, string? name = null) where T : struct, IExcelRow<T> 
        => this.Excel.GetSheet<T>(ClientLanguage.ChineseSimplified.ToLumina(), name);

    /// <inheritdoc/>
    public SubrowExcelSheet<T> GetSubrowExcelSheet<T>(ClientLanguage? language = null, string? name = null) where T : struct, IExcelSubrow<T>
        => this.Excel.GetSubrowSheet<T>(ClientLanguage.ChineseSimplified.ToLumina(), name);

    /// <inheritdoc/>
    public FileResource? GetFile(string path) 
        => this.GetFile<FileResource>(path);

    /// <inheritdoc/>
    public T? GetFile<T>(string path) where T : FileResource
    {
        var filePath = GameData.ParseFilePath(path);
        if (filePath == null)
            return default;
        return this.GameData.Repositories.TryGetValue(filePath.Repository, out var repository) ? repository.GetFile<T>(filePath.Category, filePath) : default;
    }

    /// <inheritdoc/>
    public Task<T> GetFileAsync<T>(string path, CancellationToken cancellationToken) where T : FileResource =>
        GameData.ParseFilePath(path) is { } filePath &&
        this.GameData.Repositories.TryGetValue(filePath.Repository, out var repository)
            ? Task.Run(
                () => repository.GetFile<T>(filePath.Category, filePath) ?? throw new FileNotFoundException(
                          "Failed to load file, most likely because the file could not be found."),
                cancellationToken)
            : Task.FromException<T>(new FileNotFoundException("The file could not be found."));

    /// <inheritdoc/>
    public bool FileExists(string path) 
        => this.GameData.FileExists(path);

    #endregion

    /// <inheritdoc/>
    void IInternalDisposableService.DisposeService()
    {
        this.luminaCancellationTokenSource.Cancel();
        this.GameData.Dispose();
        this.rsvResolver.Dispose();
    }

    private class LauncherTroubleshootingInfo
    {
        public enum IndexIntegrityResult
        {
            Failed,
            Exception,
            NoGame,
            ReferenceNotFound,
            ReferenceFetchFailure,
            Success,
        }

        public IndexIntegrityResult? IndexIntegrity { get; set; }
    }
}
