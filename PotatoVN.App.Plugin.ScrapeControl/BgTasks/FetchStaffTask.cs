using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.WinApp.Base.Contracts;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl.BgTasks;

/// <summary>
/// 为一批游戏补抓制作人员。
///
/// 制作人是刮削里最慢的一环（每人一次 API 请求 + 一次头像下载），
/// 所以把它从日常刮削中剥离出来，做成可单独触发、可取消的任务。
/// </summary>
public class FetchStaffTask : BgTaskBase, IDisposable
{
    public override string Title => "补抓制作人员";

    /// <summary>支持取消。取消后会立刻停下，已抓到的部分保留。</summary>
    public override bool CanCancel => true;

    private readonly IPotatoVnApi _hostApi;
    private readonly List<Galgame> _games;

    public int Succeeded { get; private set; }
    public int Failed { get; private set; }

    public FetchStaffTask(IPotatoVnApi hostApi, List<Galgame> games)
    {
        _hostApi = hostApi;
        _games = games;
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

                bool ok = await FetchOneAsync(game);
                if (ok) Succeeded++;
                else Failed++;

                done++;
                ChangeProgress(done, total, $"{done}/{total}  {game.Name.Value}",
                    notifyWhenSuccess: false);
            }

            string summary = IsCancelled
                ? $"已取消：完成 {Succeeded} 个，失败 {Failed} 个"
                : $"制作人员补抓完成：成功 {Succeeded} 个，失败 {Failed} 个";

            ChangeProgress(1, 1, summary, notifyWhenSuccess: true);
        });
    }

    /// <summary>
    /// 补抓单个游戏。制作人数据会写进绑定到界面的集合，因此要在 UI 线程发起，
    /// 这里沿用"UI 线程启动、后台 await"的方式，避免跨线程改 UI。
    /// </summary>
    private async Task<bool> FetchOneAsync(Galgame game)
    {
        if (!HostBridge.CanControlStaff && !HostBridge.IsAvailable)
            return false;

        Task? pending = null;
        Exception? launchError = null;

        _hostApi.InvokeOnMainThread(() =>
        {
            try
            {
                pending = HostBridge.FetchStaffAsync(game);
            }
            catch (Exception e)
            {
                launchError = e;
            }
        });

        if (launchError is not null || pending is null)
        {
            Plugin.HostApi?.Log(InfoBarSeverity.Warning,
                $"[ScrapeControl] 补抓失败 {game.Name.Value}: {launchError?.Message ?? HostBridge.ErrorMessage}");
            return false;
        }

        try
        {
            await pending;
            return true;
        }
        catch (Exception e)
        {
            Plugin.HostApi?.Log(InfoBarSeverity.Warning,
                $"[ScrapeControl] 补抓失败 {game.Name.Value}: {e.InnerException?.Message ?? e.Message}");
            return false;
        }
    }

    /// <summary>任务被持久化后恢复时调用。补抓任务不做断点续跑，直接标记结束。</summary>
    protected override Task RecoverFromJsonInternal()
    {
        ChangeProgress(1, 1, "任务已结束，未执行恢复", notifyWhenSuccess: false);
        return Task.CompletedTask;
    }

    public void Dispose() => CancellationTokenSource?.Dispose();
}
