using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.WinApp.Base.Contracts;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.Plugin.ScrapeControl.Models;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl.BgTasks;

/// <summary>
/// 按插件配置的类别批量刮削游戏。
///
/// 与宿主自带刮削的区别：
/// 1. 类别可精确组合，而不是一律「全部」；
/// 2. 制作人抓取由 <see cref="HostBridge"/> 单独控制，默认不参与刮削；
/// 3. 支持中途取消——宿主自身的刮削任务并没有实现取消。
/// </summary>
public class ScrapeByCategoryTask : BgTaskBase, IDisposable
{
    public override string Title => "分类刮削";

    /// <summary>支持取消：任务会在下一个游戏开始前停下。</summary>
    public override bool CanCancel => true;

    private readonly IPotatoVnApi _hostApi;
    private readonly PluginData _data;
    private readonly List<Galgame> _games;
    private readonly GameParseType _baseType;

    public int Succeeded { get; private set; }
    public int Skipped { get; private set; }
    public int Failed { get; private set; }

    /// <summary>失败明细，任务结束后上报。</summary>
    private readonly List<string> _failures = [];

    public ScrapeByCategoryTask(IPotatoVnApi hostApi, PluginData data, List<Galgame> games)
    {
        _hostApi = hostApi;
        _data = data;
        _games = games;
        _baseType = ScrapeProfile.Build(data);
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

                GameParseType type = BuildTypeFor(game);
                if (type == GameParseType.None)
                {
                    Skipped++;
                }
                else
                {
                    bool ok = await ScrapeOneAsync(game, type);
                    if (ok) Succeeded++;
                    else
                    {
                        Failed++;
                        _failures.Add($"{game.Name.Value}: {HostBridge.ErrorMessage}");
                    }
                }

                done++;
                ChangeProgress(done, total, $"{done}/{total}  {game.Name.Value}",
                    notifyWhenSuccess: false);

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
    /// 对单个游戏执行刮削。
    ///
    /// 刮削内部会改动绑定到界面的对象并下载图片，因此必须在 UI 线程发起；
    /// 这里只在 UI 线程「启动」它（拿到 Task 就返回），随后在后台 await，
    /// 这样既不会跨线程改 UI，也不会卡住界面。
    /// 插件 API 只提供了同步的 InvokeOnMainThread，所以要用这种方式绕开。
    /// </summary>
    private async Task<bool> ScrapeOneAsync(Galgame game, GameParseType type)
    {
        Task? pending = null;
        Exception? launchError = null;

        _hostApi.InvokeOnMainThread(() =>
        {
            try
            {
                pending = HostBridge.ScrapeAsync(game, type);
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

    /// <summary>
    /// 算出这个游戏真正需要刮哪些类别。
    /// 开了"跳过已刮过"时，把已经拿到的类别从标志位里剔除，省掉多余请求。
    /// </summary>
    private GameParseType BuildTypeFor(Galgame game)
    {
        GameParseType type = _baseType;
        if (!_data.SkipAlreadyFetched) return type;

        if (type.HasFlag(GameParseType.GameInfo) && HasGameInfo(game))
            type &= ~GameParseType.GameInfo;

        if (type.HasFlag(GameParseType.Image) && HasImage(game))
            type &= ~GameParseType.Image;

        if (type.HasFlag(GameParseType.HeaderImage) && game.AutoFetchStatus.HeaderImage)
            type &= ~GameParseType.HeaderImage;

        if (type.HasFlag(GameParseType.Character) && game.Characters.Count > 0)
            type &= ~GameParseType.Character;

        return type;
    }

    /// <summary>是否已有基本游戏信息。以会社字段为准，它是最核心的元数据。</summary>
    private static bool HasGameInfo(Galgame game)
    {
        string? developer = game.Developer.Value;
        return !string.IsNullOrWhiteSpace(developer) && developer != Galgame.DefaultString;
    }

    /// <summary>是否已有封面。默认图路径代表没刮到。</summary>
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

    /// <summary>
    /// 任务被持久化后恢复时调用。刮削类任务没必要断点续跑，直接标记结束。
    /// </summary>
    protected override Task RecoverFromJsonInternal()
    {
        ChangeProgress(1, 1, "任务已结束，未执行恢复", notifyWhenSuccess: false);
        return Task.CompletedTask;
    }

    public void Dispose() => CancellationTokenSource?.Dispose();
}
