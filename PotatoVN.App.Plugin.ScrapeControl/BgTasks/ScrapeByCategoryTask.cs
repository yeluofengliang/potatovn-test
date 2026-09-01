using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.WinApp.Base.Contracts;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.Plugin.ScrapeControl.Models;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl.BgTasks;

public class ScrapeByCategoryTask : BgTaskBase, IDisposable
{
    public override string Title => "分类刮削";
    public override bool CanCancel => true;

    private readonly IPotatoVnApi _hostApi;
    private readonly PluginData _data;
    private readonly List<Galgame> _games;
    private readonly long _baseMask;

    public int Succeeded { get; private set; }
    public int Skipped { get; private set; }
    public int Failed { get; private set; }

    private readonly List<string> _failures = [];

    public ScrapeByCategoryTask(IPotatoVnApi hostApi, PluginData data, List<Galgame> games)
    {
        _hostApi = hostApi;
        _data = data;
        _games = games;
        _baseMask = ScrapeProfile.Build(data);
    }

    protected override Task RunInternal()
    {
        CancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = CancellationTokenSource.Token;

        return Task.Run(async () =>
        {
            int total = _games.Count;
            int done = 0;

            foreach (Galgame game in _games)
            {
                if (token.IsCancellationRequested) break;

                long mask = BuildMaskFor(game);
                if (mask == 0)
                {
                    Skipped++;
                }
                else
                {
                    bool ok = await ScrapeOneAsync(game, mask);
                    if (ok) Succeeded++;
                    else
                    {
                        Failed++;
                        _failures.Add($"{game.Name.Value}: {HostBridge.ErrorMessage}");
                    }
                }

                done++;
                ChangeProgress(done, total, $"{done}/{total}  {game.Name.Value}", notifyWhenSuccess: false);

                if (_data.DelayBetweenGamesMs > 0 && !token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_data.DelayBetweenGamesMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            ReportResult();
        });
    }

    /// <summary>
    /// 刮削内部会改动绑定到界面的对象并下载图片，因此必须在 UI 线程发起；
    /// 插件 API 只提供同步的 InvokeOnMainThread，所以这里只在 UI 线程「启动」它再回后台 await。
    /// </summary>
    private async Task<bool> ScrapeOneAsync(Galgame game, long mask)
    {
        Task? pending = null;
        Exception? launchError = null;

        _hostApi.InvokeOnMainThread(() =>
        {
            try
            {
                pending = HostBridge.ScrapeAsync(game, mask);
            }
            catch (Exception e)
            {
                launchError = e;
            }
        });

        if (launchError is not null || pending is null) return false;

        try
        {
            await pending;
            return true;
        }
        catch (Exception e)
        {
            _failures.Add($"{game.Name.Value}: {e.InnerException?.Message ?? e.Message}");
            return false;
        }
    }

    /// <summary>开了"跳过已刮过"时，把已拿到的类别位清掉。</summary>
    private long BuildMaskFor(Galgame game)
    {
        long mask = _baseMask;
        if (!_data.SkipAlreadyFetched) return mask;

        if (HasGameInfo(game)) mask &= ~HostBridge.Flag("GameInfo");
        if (HasImage(game)) mask &= ~HostBridge.Flag("Image");
        if (game.AutoFetchStatus.HeaderImage) mask &= ~HostBridge.Flag("HeaderImage");
        if (game.Characters.Count > 0) mask &= ~HostBridge.Flag("Character");

        return mask;
    }

    private static bool HasGameInfo(Galgame game)
    {
        string? developer = game.Developer.Value;
        return !string.IsNullOrWhiteSpace(developer) && developer != Galgame.DefaultString;
    }

    private static bool HasImage(Galgame game)
    {
        string? path = game.ImagePath.Value;
        return !string.IsNullOrWhiteSpace(path) && path != Galgame.DefaultImagePath;
    }

    private void ReportResult()
    {
        string summary = IsCancelled
            ? $"已取消：完成 {Succeeded} 个，跳过 {Skipped} 个，失败 {Failed} 个"
            : $"刮削完成：成功 {Succeeded} 个，跳过 {Skipped} 个，失败 {Failed} 个";

        ChangeProgress(1, 1, summary, notifyWhenSuccess: true);

        foreach (string failure in _failures)
            Plugin.HostApi?.Log(InfoBarSeverity.Warning, $"[ScrapeControl] {failure}");
    }

    protected override Task RecoverFromJsonInternal()
    {
        ChangeProgress(1, 1, "任务已结束，未执行恢复", notifyWhenSuccess: false);
        return Task.CompletedTask;
    }

    public void Dispose() => CancellationTokenSource?.Dispose();
}
