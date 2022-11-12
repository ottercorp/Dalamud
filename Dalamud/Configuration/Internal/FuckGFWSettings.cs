namespace Dalamud.Configuration
{
    /// <summary>
    /// Third party repository for dalamud plugins.
    /// </summary>
    public sealed class FuckGFWSettings
    {
        /// <summary>
        /// Gets or sets the fuck GFW url.
        /// </summary>
        public string UrlRegex { get; set; }

        /// <summary>
        /// Gets or sets a value the url to be replaced to.
        /// </summary>
        public string ReplaceTo { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the fuck GFW is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Clone this object.
        /// </summary>
        /// <returns>A shallow copy of this object.</returns>
        public FuckGFWSettings Clone() => this.MemberwiseClone() as FuckGFWSettings;
    }
}
