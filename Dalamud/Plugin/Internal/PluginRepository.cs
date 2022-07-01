using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;

using Dalamud.Logging.Internal;
using Dalamud.Plugin.Internal.Types;
using Dalamud.Utility;
using Microsoft.Extensions.Caching.Abstractions;
using Microsoft.Extensions.Caching.InMemory;
using Newtonsoft.Json;

namespace Dalamud.Plugin.Internal
{
    /// <summary>
    /// This class represents a single plugin repository.
    /// </summary>
    internal class PluginRepository
    {
        private const string DalamudPluginsMasterUrl = "https://bypass.ffxiv.wang/dalamudplugins/cn-api5/pluginmaster.json";

        private static readonly ModuleLog Log = new("PLUGINR");

        private static InMemoryCacheHandler handler = InitHandler();

        private static HttpClient httpClient = new(handler);

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginRepository"/> class.
        /// </summary>
        /// <param name="pluginMasterUrl">The plugin master URL.</param>
        /// <param name="isEnabled">Whether the plugin repo is enabled.</param>
        public PluginRepository(string pluginMasterUrl, bool isEnabled)
        {
            this.PluginMasterUrl = pluginMasterUrl;
            this.IsThirdParty = Utility.Util.FuckGFW(pluginMasterUrl) != Utility.Util.FuckGFW(DalamudPluginsMasterUrl);
            this.IsEnabled = isEnabled;
        }

        private static InMemoryCacheHandler InitHandler()
        {
            var httpClientHandler = new HttpClientHandler();
            var cacheExpirationPerHttpResponseCode = CacheExpirationProvider.CreateSimple(TimeSpan.FromHours(3), TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0));
            return new InMemoryCacheHandler(httpClientHandler, cacheExpirationPerHttpResponseCode);
        }

        /// <summary>
        /// Gets a new instance of the <see cref="PluginRepository"/> class for the main repo.
        /// </summary>
        public static PluginRepository MainRepo => new(Utility.Util.FuckGFW(DalamudPluginsMasterUrl), true);

        /// <summary>
        /// Gets the pluginmaster.json URL.
        /// </summary>
        public string PluginMasterUrl { get; }

        /// <summary>
        /// Gets a value indicating whether this plugin repository is from a third party.
        /// </summary>
        public bool IsThirdParty { get; }

        /// <summary>
        /// Gets a value indicating whether this repo is enabled.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        /// Gets the plugin master list of available plugins.
        /// </summary>
        public ReadOnlyCollection<RemotePluginManifest>? PluginMaster { get; private set; }

        /// <summary>
        /// Gets the initialization state of the plugin repository.
        /// </summary>
        public PluginRepositoryState State { get; private set; }

        /// <summary>
        /// Reload the plugin master asynchronously in a task.
        /// </summary>
        /// <param name="skipCache">Skip MemoryCache.</param>
        /// <returns>The new state.</returns>
        public async Task ReloadPluginMasterAsync(bool skipCache = true)
        {
            this.State = PluginRepositoryState.InProgress;
            this.PluginMaster = new List<RemotePluginManifest>().AsReadOnly();

            try
            {
                Log.Information($"Fetching repo: {this.PluginMasterUrl}");

                if (skipCache)
                {
                    handler.InvalidateCache(new Uri(this.PluginMasterUrl));
                }

                using var response = await httpClient.GetAsync(this.PluginMasterUrl);
                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadAsStringAsync();
                var pluginMaster = JsonConvert.DeserializeObject<List<RemotePluginManifest>>(data);

                if (pluginMaster == null)
                {
                    throw new Exception("Deserialized PluginMaster was null.");
                }

                pluginMaster.Sort((pm1, pm2) => pm1.Name.CompareTo(pm2.Name));

                // Set the source for each remote manifest. Allows for checking if is 3rd party.
                foreach (var manifest in pluginMaster)
                {
                    manifest.SourceRepo = this;
                }

                this.PluginMaster = pluginMaster.AsReadOnly();

                Log.Debug($"Successfully fetched repo: {this.PluginMasterUrl}");

                var stats = handler.StatsProvider.GetStatistics().Total;
                Log.Information($"Cache: {stats.TotalRequests}/{stats.CacheHit}/{stats.CacheMiss}");

                this.State = PluginRepositoryState.Success;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"PluginMaster failed: {this.PluginMasterUrl}");
                this.State = PluginRepositoryState.Fail;
            }
        }
    }
}
