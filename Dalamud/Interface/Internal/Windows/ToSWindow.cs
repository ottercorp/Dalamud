using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Configuration.Internal;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Networking.Http;
using ImGuiNET;
using ImGuiScene;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using Serilog;

namespace Dalamud.Interface.Internal.Windows;

/// <summary>
/// For major updates, an in-game Changelog window.
/// </summary>
internal sealed class ToSWindow : Window, IDisposable
{
    private static SemaphoreSlim requestSemaphore = new SemaphoreSlim(1, 1);
    private string? TOSContent;
    private bool tosRead = false;
    private bool tosRequested = false;
    private DalamudConfiguration configuration;
    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogWindow"/> class.
    /// </summary>
    public ToSWindow()
        : base("Dalamud Terms of Service", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse)
    {
        this.Namespace = "DalamudTosWindow";

        this.Size = new Vector2(885, 463);
        this.SizeCondition = ImGuiCond.Appearing;
        this.ShowCloseButton = false;
        this.configuration = Service<DalamudConfiguration>.Get();
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        var isOpen = this.IsOpen;
        if (!isOpen)
        {
            return;
        }

        if (!this.tosRequested)
        {
            this.GetRemoteTOS();
        }

        this.BringToFront();

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - (this.Size.Value.X / 2.0f), center.Y - (this.Size.Value.Y / 2.0f)));

        var tosContent = this.TOSContent == null ? "请等待获取用户协议..." : this.TOSContent;
        ImGui.PushTextWrapPos(850);
        ImGui.TextUnformatted(tosContent);
        ImGui.PopTextWrapPos();

        ImGui.Spacing();

        if (this.TOSContent == null)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Checkbox("我已阅读完毕以上内容并同意", ref this.tosRead);

        if (this.TOSContent == null)
        {
            ImGui.EndDisabled();
        }

        if (!this.tosRead)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("我同意"))
        {
            Log.Debug(tosContent);
            var readHash = Hash.GetStringSha256Hash(tosContent);
            Log.Information($"TOS Read Hash: {readHash}");
            this.configuration.AcceptedTOSHash = readHash;
            this.configuration.QueueSave();
            this.IsOpen = false;
        }

        if (!this.tosRead)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        var disagreeText = "我不同意";
        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(disagreeText).X);

        if (ImGui.Button(disagreeText))
        {
            Log.Information($"TOS failed to get accepted.");
            this.configuration.AcceptedTOSHash = string.Empty;
            this.configuration.QueueSave();
            this.IsOpen = false;
            Task.Run(() =>
            {
                Thread.Sleep(1000);
                Service<Dalamud>.Get().Unload();
            });
        }
    }

    /// <summary>
    /// Dispose this window.
    /// </summary>
    public void Dispose()
    {
    }

    internal async void GetRemoteTOS()
    {
        if (this.tosRequested && this.TOSContent != null)
        {
            return;
        }

        this.tosRequested = true;
        await requestSemaphore.WaitAsync();
        try
        {
            Log.Information("Requesting TOS...");
            var httpClient = Service<HappyHttpClient>.Get().SharedHttpClient;
            var response = await httpClient.GetStringAsync($"{Util.TOSRemoteUrl}");
            this.TOSContent = response;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Remote TOS request failed");
        }
        finally
        {
            requestSemaphore.Release();
        }
    }
}
