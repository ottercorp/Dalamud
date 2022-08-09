using System;
using System.IO;
using System.Numerics;

using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;
using ImGuiScene;
using Serilog;

namespace Dalamud.Interface.Internal.Windows
{
    /// <summary>
    /// For major updates, an in-game Changelog window.
    /// </summary>
    internal sealed class ChangelogWindow : Window, IDisposable
    {
        /// <summary>
        /// Whether the latest update warrants a changelog window.
        /// </summary>
        public const string WarrantsChangelogForMajorMinor = "6.4.";

        private const string ChangeLog =
            @"• Updated Dalamud for compatibility with Patch 6.1.

If you note any issues or need help, please check the FAQ, and reach out on our Discord if you need help
Thanks and have fun!";

        private const string UpdatePluginsInfo =
            @"• All of your plugins were disabled automatically, due to this update. This is normal.
• Open the plugin installer, then click 'update plugins'. Updated plugins should update and then re-enable themselves.
   => Please keep in mind that not all of your plugins may already be updated for the new version.
   => If some plugins are displayed with a red cross in the 'Installed Plugins' tab, they may not yet be available.";

        private readonly string assemblyVersion = Util.AssemblyVersion;

        private readonly TextureWrap logoTexture;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangelogWindow"/> class.
        /// </summary>
        public ChangelogWindow()
            : base("有啥新功能？？", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize)
        {
            this.Namespace = "DalamudChangelogWindow";

            this.Size = new Vector2(885, 463);
            this.SizeCondition = ImGuiCond.Appearing;

            var interfaceManager = Service<InterfaceManager>.Get();
            var dalamud = Service<Dalamud>.Get();

            this.logoTexture =
                interfaceManager.LoadImage(Path.Combine(dalamud.AssetDirectory.FullName, "UIRes", "logo.png"))!;
        }

        /// <inheritdoc/>
        public override void Draw()
        {
            ImGui.Text($"卫月框架更新到了版本 D{this.assemblyVersion}。");

            ImGuiHelpers.ScaledDummy(10);

            ImGui.Text("包含了以下更新:");

            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(0);
            var imgCursor = ImGui.GetCursorPos();

            ImGui.TextWrapped(ChangeLog);

            ImGuiHelpers.ScaledDummy(5);

            ImGui.TextColored(ImGuiColors.DalamudRed, " !!! 注意 !!!");

            ImGui.TextWrapped(UpdatePluginsInfo);

            ImGuiHelpers.ScaledDummy(10);

            // ImGui.Text("感谢使用我们的工具！");

            // ImGuiHelpers.ScaledDummy(10);

            ImGui.PushFont(UiBuilder.IconFont);

            if (ImGui.Button(FontAwesomeIcon.Download.ToIconString()))
            {
                Service<DalamudInterface>.Get().OpenPluginInstaller();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("打开插件安装器");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.LaughBeam.ToIconString()))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://discord.gg/3NMcUV5",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not open discord url");
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("加入我们的 Discord 服务器");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.LaughSquint.ToIconString()))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://qun.qq.com/qqweb/qunpro/share?_wv=3&_wwv=128&appChannel=share&inviteCode=CZtWN&businessType=9&from=181074&biz=ka&shareSource=5",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not open QQ url");
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("加入我们的 QQ 频道");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.Globe.ToIconString()))
            {
                Util.OpenLink("https://ottercorp.github.io/faq/");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("查看 FAQ");
                ImGui.PushFont(UiBuilder.IconFont);
            }

            ImGui.SameLine();

            if (ImGui.Button(FontAwesomeIcon.Heart.ToIconString()))
            {
                Util.OpenLink("https://ottercorp.github.io/faq/support");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.PopFont();
                ImGui.SetTooltip("支持我们");
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

            imgCursor.X += 750;
            imgCursor.Y -= 30;
            ImGui.SetCursorPos(imgCursor);

            ImGui.Image(this.logoTexture.ImGuiHandle, new Vector2(100));
        }

        /// <summary>
        /// Dispose this window.
        /// </summary>
        public void Dispose()
        {
            this.logoTexture.Dispose();
        }
    }
}
