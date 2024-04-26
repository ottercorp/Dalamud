using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

using CheapLoc;

using Dalamud.Configuration.Internal;
using Dalamud.Interface.Animation.EasingFunctions;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.ManagedFontAtlas.Internals;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Internal;
using Dalamud.Storage.Assets;
using Dalamud.Utility;
using ImGuiNET;
using ImGuiScene;
using Serilog;

namespace Dalamud.Interface.Internal.Windows;

/// <summary>
/// For major updates, an in-game Changelog window.
/// </summary>
internal sealed class ChangelogWindow : Window, IDisposable
{
    private const string WarrantsChangelogForMajorMinor = "9.1.0.";
    
    private const string ChangeLog =
        @"• 更新了Dalamud，以便与Patch 6.5兼容
• 尝试修正中文输入卡顿问题(再次)
• 输入韩文不会直接爆炸了
• 重置字体设置后会立刻保存了
• 更新了一些翻译
• .Net版本更新至.Net8了
• New:更新了FFCS版本和挂掉的API
";

    private readonly TitleScreenMenuWindow tsmWindow;

    private readonly DisposeSafety.ScopedFinalizer scopedFinalizer = new();
    private readonly IFontAtlas privateAtlas;
    private readonly Lazy<IFontHandle> bannerFont;
    private readonly Lazy<IDalamudTextureWrap> apiBumpExplainerTexture;
    private readonly Lazy<IDalamudTextureWrap> logoTexture;
    private readonly Lazy<IDalamudTextureWrap> fontTipsTexture;

    private readonly InOutCubic windowFade = new(TimeSpan.FromSeconds(2.5f))
    {
        Point1 = Vector2.Zero,
        Point2 = new Vector2(2f),
    };
    
    private readonly InOutCubic bodyFade = new(TimeSpan.FromSeconds(1f))
    {
        Point1 = Vector2.Zero,
        Point2 = Vector2.One,
    };
    
    private State state = State.WindowFadeIn;

    private bool needFadeRestart = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogWindow"/> class.
    /// </summary>
    /// <param name="tsmWindow">TSM window.</param>
    /// <param name="fontAtlasFactory">An instance of <see cref="FontAtlasFactory"/>.</param>
    /// <param name="assets">An instance of <see cref="DalamudAssetManager"/>.</param>
    public ChangelogWindow(
        TitleScreenMenuWindow tsmWindow,
        FontAtlasFactory fontAtlasFactory,
        DalamudAssetManager assets)
        : base("What's new in Dalamud?##ChangelogWindow", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, true)
    {
        this.tsmWindow = tsmWindow;
        this.Namespace = "DalamudChangelogWindow";
        this.privateAtlas = this.scopedFinalizer.Add(
            fontAtlasFactory.CreateFontAtlas(this.Namespace, FontAtlasAutoRebuildMode.Async));
        this.bannerFont = new(
            () => this.scopedFinalizer.Add(
                this.privateAtlas.NewGameFontHandle(new(GameFontFamilyAndSize.MiedingerMid18))));

        this.apiBumpExplainerTexture = new(() => assets.GetDalamudTextureWrap(DalamudAsset.ChangelogApiBumpIcon));
        this.logoTexture = new(() => assets.GetDalamudTextureWrap(DalamudAsset.Logo));

        this.fontTipsTexture = new(() => assets.GetDalamudTextureWrap(DalamudAsset.MissingFontTips));

        // If we are going to show a changelog, make sure we have the font ready, otherwise it will hitch
        if (WarrantsChangelog())
            _ = this.bannerFont.Value;
    }

    private enum State
    {
        WindowFadeIn,
        ExplainerIntro,
        ExplainerApiBump,
        Links,
    }
    
    /// <summary>
    /// Check if a changelog should be shown.
    /// </summary>
    /// <returns>True if a changelog should be shown.</returns>
    public static bool WarrantsChangelog()
    {
        var configuration = Service<DalamudConfiguration>.Get();
        var pm = Service<PluginManager>.GetNullable();
        var pmWantsChangelog = pm?.InstalledPlugins.Any() ?? true;
        return (string.IsNullOrEmpty(configuration.LastChangelogMajorMinor) ||
                (!WarrantsChangelogForMajorMinor.StartsWith(configuration.LastChangelogMajorMinor) &&
                 Util.AssemblyVersion.StartsWith(WarrantsChangelogForMajorMinor))) && pmWantsChangelog;
    }

    /// <inheritdoc/>
    public override void OnOpen()
    {
        Service<DalamudInterface>.Get().SetCreditsDarkeningAnimation(true);
        this.tsmWindow.AllowDrawing = false;

        _ = this.bannerFont;
        
        this.state = State.WindowFadeIn;
        this.windowFade.Reset();
        this.bodyFade.Reset();
        this.needFadeRestart = true;
        
        base.OnOpen();
    }

    /// <inheritdoc/>
    public override void OnClose()
    {
        base.OnClose();
        
        this.tsmWindow.AllowDrawing = true;
        Service<DalamudInterface>.Get().SetCreditsDarkeningAnimation(false);
    }

    /// <inheritdoc/>
    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        
        base.PreDraw();

        if (this.needFadeRestart)
        {
            this.windowFade.Restart();
            this.needFadeRestart = false;
        }
        
        this.windowFade.Update();
        ImGui.SetNextWindowBgAlpha(Math.Clamp(this.windowFade.EasedPoint.X, 0, 0.9f));
        
        this.Size = new Vector2(900, 400);
        this.SizeCondition = ImGuiCond.Always;
        
        // Center the window on the main viewport
        var viewportSize = ImGuiHelpers.MainViewport.Size;
        var windowSize = this.Size!.Value * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowPos(new Vector2(viewportSize.X / 2 - windowSize.X / 2, viewportSize.Y / 2 - windowSize.Y / 2));
    }

    /// <inheritdoc/>
    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        base.PostDraw();
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        void Dismiss()
        {
            var configuration = Service<DalamudConfiguration>.Get();
            configuration.LastChangelogMajorMinor = WarrantsChangelogForMajorMinor;
            configuration.QueueSave();
        }
        
        var windowSize = ImGui.GetWindowSize();
        
        var dummySize = 10 * ImGuiHelpers.GlobalScale;
        ImGui.Dummy(new Vector2(dummySize));
        ImGui.SameLine();
        
        var logoContainerSize = new Vector2(windowSize.X * 0.2f - dummySize, windowSize.Y);
        using (var child = ImRaii.Child("###logoContainer", logoContainerSize, false))
        {
            if (!child)
                return;

            var logoSize = new Vector2(logoContainerSize.X);
            
            // Center the logo in the container
            ImGui.SetCursorPos(new Vector2(logoContainerSize.X / 2 - logoSize.X / 2, logoContainerSize.Y / 2 - logoSize.Y / 2));
            
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, Math.Clamp(this.windowFade.EasedPoint.X - 0.5f, 0f, 1f)))
                ImGui.Image(this.logoTexture.Value.ImGuiHandle, logoSize);
        }
        
        ImGui.SameLine();
        ImGui.Dummy(new Vector2(dummySize));
        ImGui.SameLine();
        
        using (var child = ImRaii.Child("###textContainer", new Vector2((windowSize.X * 0.8f) - dummySize * 4, windowSize.Y), false))
        {
            if (!child)
                return;
            
            ImGuiHelpers.ScaledDummy(20);
            
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, Math.Clamp(this.windowFade.EasedPoint.X - 1f, 0f, 1f)))
            {
                using var font = this.bannerFont.Value.Push();

                switch (this.state)
                {
                    case State.WindowFadeIn:
                    case State.ExplainerIntro:
                        ImGuiHelpers.CenteredText("New And Improved");
                        break;
                    
                    case State.ExplainerApiBump:
                        ImGuiHelpers.CenteredText("Plugin Updates");
                        break;
                    
                    case State.Links:
                        ImGuiHelpers.CenteredText("Enjoy!");
                        break;
                }
            }
            
            ImGuiHelpers.ScaledDummy(8);

            if (this.state == State.WindowFadeIn && this.windowFade.EasedPoint.X > 1.5f)
            {
                this.state = State.ExplainerIntro;
                this.bodyFade.Restart();
            }
            
            this.bodyFade.Update();
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, Math.Clamp(this.bodyFade.EasedPoint.X, 0, 1f)))
            {
                void DrawNextButton(State nextState)
                {
                    // Draw big, centered next button at the bottom of the window
                    var buttonHeight = 30 * ImGuiHelpers.GlobalScale;
                    var buttonText = "Next";
                    var buttonWidth = ImGui.CalcTextSize(buttonText).X + 40 * ImGuiHelpers.GlobalScale;
                    ImGui.SetCursorPosY(windowSize.Y - buttonHeight - (20 * ImGuiHelpers.GlobalScale));
                    ImGuiHelpers.CenterCursorFor((int)buttonWidth);
                
                    if (ImGui.Button(buttonText, new Vector2(buttonWidth, buttonHeight)))
                    {
                        this.state = nextState;
                        this.bodyFade.Restart();
                    }
                }
                
                switch (this.state)
                {
                    case State.WindowFadeIn:
                    case State.ExplainerIntro:
                        ImGui.TextWrapped($"Welcome to Dalamud v{Util.AssemblyVersion}!");
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.TextWrapped(ChangeLog);
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.TextWrapped("这个更新日志是对本版本中最重要的更改的快速概述");
                        ImGui.TextWrapped("请点击Next以查看更新插件的快速指南。");

                        ImGui.Image(
                            this.fontTipsTexture.Value.ImGuiHandle,
                            this.fontTipsTexture.Value.Size);

                        var interfaceManager = Service<InterfaceManager>.Get();
                        using (interfaceManager.MonoFontHandle?.Push())
                        {
                            if (ImGui.Button(Loc.Localize("DalamudSettingResetDefaultFont", "Reset Default Font")))
                            {
                                var faf = Service<FontAtlasFactory>.Get();
                                faf.DefaultFontSpecOverride =
                                        new SingleFontSpec { FontId = new GameFontAndFamilyId(GameFontFamily.Axis) };
                                interfaceManager.RebuildFonts();

                                Service<DalamudConfiguration>.Get().DefaultFontSpec = faf.DefaultFontSpecOverride;
                                Service<DalamudConfiguration>.Get().QueueSave();
                            }
                        }

                        ImGuiHelpers.ScaledDummy(10);
                        DrawNextButton(State.ExplainerApiBump);
                        break;
                    
                    case State.ExplainerApiBump:
                        ImGui.TextWrapped("注意了！由于此补丁的更改，所有插件都需要更新，并已自动禁用");
                        ImGui.TextWrapped("这是正常情况，也是进行重大游戏更新所必需的。");
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.TextWrapped("要更新插件，请打开插件安装程序，然后点击“更新插件”。更新后的插件应会自动更新并重新启用。");
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.TextWrapped("请记住，并非您所有的插件都可能已经针对新版本进行了更新。");
                        ImGui.TextWrapped("如果在“已安装插件”选项卡中显示某些插件带有红色叉号，那么它们可能还不可用。");
                        
                        ImGuiHelpers.ScaledDummy(15);

                        ImGuiHelpers.CenterCursorFor(this.apiBumpExplainerTexture.Value.Width);
                        ImGui.Image(
                            this.apiBumpExplainerTexture.Value.ImGuiHandle,
                            this.apiBumpExplainerTexture.Value.Size);
                        
                        DrawNextButton(State.Links);
                        break;
                    
                    case State.Links:
                        ImGui.TextWrapped("如果您注意到任何问题或需要帮助，请查看常见问题解答，并在需要帮助的情况下在我们的QQ频道上联系我们。");
                        ImGui.TextWrapped("Enjoy your time with the game and Dalamud!");
                        
                        ImGuiHelpers.ScaledDummy(45);
                        
                        bool CenteredIconButton(FontAwesomeIcon icon, string text)
                        {
                            var buttonWidth = ImGuiComponents.GetIconButtonWithTextWidth(icon, text);
                            ImGuiHelpers.CenterCursorFor((int)buttonWidth);
                            return ImGuiComponents.IconButtonWithText(icon, text);
                        }
                        
                        if (CenteredIconButton(FontAwesomeIcon.Download, "Open Plugin Installer"))
                        {
                            Service<DalamudInterface>.Get().OpenPluginInstaller();
                            this.IsOpen = false;
                            Dismiss();
                        }
                        
                        ImGuiHelpers.ScaledDummy(5);
                        
                        ImGuiHelpers.CenterCursorFor(
                            (int)(ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Globe, "See the FAQ") +
                            ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.LaughBeam, "加入我们的QQ频道") +
                            (5 * ImGuiHelpers.GlobalScale) + 
                            (ImGui.GetStyle().ItemSpacing.X * 4)));
                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Globe, "See the FAQ"))
                        {
                            Util.OpenLink("https://goatcorp.github.io/faq/");
                        }
                        
                        ImGui.SameLine();
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.SameLine();
                        
                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.LaughBeam, "加入我们的QQ频道"))
                        {
                            Util.OpenLink("https://pd.qq.com/s/9ehyfcha3");
                        }
                        
                        ImGuiHelpers.ScaledDummy(5);
                        
                        if (CenteredIconButton(FontAwesomeIcon.Heart, "Support what we care about"))
                        {
                            Util.OpenLink("https://goatcorp.github.io/faq/support");
                        }
                        
                        var buttonHeight = 30 * ImGuiHelpers.GlobalScale;
                        var buttonText = "Close";
                        var buttonWidth = ImGui.CalcTextSize(buttonText).X + 40 * ImGuiHelpers.GlobalScale;
                        ImGui.SetCursorPosY(windowSize.Y - buttonHeight - (20 * ImGuiHelpers.GlobalScale));
                        ImGuiHelpers.CenterCursorFor((int)buttonWidth);
                
                        if (ImGui.Button(buttonText, new Vector2(buttonWidth, buttonHeight)))
                        {
                            this.IsOpen = false;
                            Dismiss();
                        }
                        
                        break;
                }
            }
            
            // Draw close button in the top right corner
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 100f);
            var btnAlpha = Math.Clamp(this.windowFade.EasedPoint.X - 0.5f, 0f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed.WithAlpha(btnAlpha).Desaturate(0.3f));
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite.WithAlpha(btnAlpha));
            
            var childSize = ImGui.GetWindowSize();
            var closeButtonSize = 15 * ImGuiHelpers.GlobalScale;
            ImGui.SetCursorPos(new Vector2(childSize.X - closeButtonSize - 5, 10 * ImGuiHelpers.GlobalScale));
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                Dismiss();
                this.IsOpen = false;
            }

            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar();

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("I don't care about this");
            }
        }
    }

    /// <summary>
    /// Dispose this window.
    /// </summary>
    public void Dispose()
    {
    }
}
