using System.Diagnostics;

using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;

namespace Dalamud.Interface.Internal.Windows
{
    /// <summary>
    /// For major updates, an in-game Changelog window.
    /// </summary>
    internal sealed class ChangelogWindow : Window
    {
        /// <summary>
        /// Whether the latest update warrants a changelog window.
        /// </summary>
        public const bool WarrantsChangelog = true;

        private const string ChangeLog =
            @"* Various behind-the-scenes changes to improve stability and provide more functionality to plugin developers

ATTENTION: YOU WILL HAVE TO UPDATE/REINSTALL ALL OF YOUR PLUGINS!!!!
If you note any issues or need help, please make sure to ask on our discord server.

Thank you for participating in the Dalamud collaborative testing programme.";

        private readonly string assemblyVersion = Util.AssemblyVersion;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangelogWindow"/> class.
        /// </summary>
        public ChangelogWindow()
            : base("What's new in XIVLauncher?", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize)
        {
            this.Namespace = "DalamudChangelogWindow";

            this.IsOpen = WarrantsChangelog;
        }

        /// <inheritdoc/>
        public override void Draw()
        {
            ImGui.Text($"卫月框架更新到了版本 D{this.assemblyVersion}。");

            ImGuiHelpers.ScaledDummy(10);

            ImGui.Text("包含以下更新内容：");
            ImGui.Text(ChangeLog);

            ImGuiHelpers.ScaledDummy(10);

            ImGui.Text("感谢使用我们的工具！");

            ImGuiHelpers.ScaledDummy(10);

            ImGui.PushFont(UiBuilder.IconFont);

            if (ImGui.Button(FontAwesomeIcon.Download.ToIconString()))
            {
                Service<DalamudInterface>.Get().OpenPluginInstaller();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("Open Plugin Installer");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.LaughBeam.ToIconString()))
            {
                Process.Start("https://discord.gg/3NMcUV5");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("Join our Discord server");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.Globe.ToIconString()))
            {
                Process.Start("https://github.com/goatcorp/FFXIVQuickLauncher");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("See our GitHub repository");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.PopFont();

            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 0);
            ImGui.SameLine();

            if (ImGui.Button("关闭"))
            {
                this.IsOpen = false;
            }
        }
    }
}
