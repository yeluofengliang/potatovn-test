using System.Collections.Generic;
using System.Threading.Tasks;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models.Plugin;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.Plugin.ScrapeControl.BgTasks;
// 自定义 Panel 与 Microsoft.UI.Xaml.Controls.Panel 重名，用别名引用
using Prefabs = PotatoVN.App.Plugin.ScrapeControl.Controls.Prefabs;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl;

/// <summary>
/// 插件的界面部分：侧边栏入口、游戏详情页的快捷面板。
/// </summary>
public partial class Plugin : IGalgamePageRightPanel
{
    private bool _uiInit;

    private void InitUi()
    {
        if (_uiInit) return;

        _hostApi.RegisterSidebarButton(new SidebarButtonInfo
        {
            Id = "scrape-control-run",
            Text = "按配置刮削",
            Placement = SidebarButtonPlacement.Menu,
            FluentGlyph = "&#xE896;",   // Download
        }, () =>
        {
            StartScrapeAll();
            return Task.CompletedTask;
        });

        _uiInit = true;
    }

    private void StartScrapeAll()
    {
        if (!HostBridge.IsAvailable)
        {
            _hostApi.Info(InfoBarSeverity.Warning, "无法刮削",
                msg: $"未能接管宿主刮削服务：{HostBridge.ErrorMessage}", displayTimeMs: 5000);
            return;
        }

        if (ScrapeProfile.IsEmpty(_data))
        {
            _hostApi.Info(InfoBarSeverity.Warning, "没有勾选任何内容",
                "请到插件设置里至少勾选一个刮削类别。", displayTimeMs: 4000);
            return;
        }

        List<Galgame> games = _hostApi.GetAllGames();
        if (games.Count == 0)
        {
            _hostApi.Info(InfoBarSeverity.Warning, "游戏库是空的", "没有找到任何游戏。");
            return;
        }

        ScrapeByCategoryTask task = new(_hostApi, _data, games);
        _ = _hostApi.AddBgTask(task);
    }

    /// <summary>
    /// 游戏详情页右侧面板：对当前这一个游戏刮削或补抓制作人。
    /// </summary>
    public Task<FrameworkElement> CreateRightPanelUiAsync(Galgame game)
    {
        StackPanel stack = new() { Spacing = 8, Padding = new Thickness(16, 12, 16, 12) };

        stack.Children.Add(new TextBlock
        {
            Text = "刮削控制",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        stack.Children.Add(new TextBlock
        {
            Text = ScrapeProfile.Describe(_data),
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources.TryGetValue("DescriptionTextStyle", out object? s)
                    && s is Style st
                ? st
                : null,
        });

        bool canScrape = HostBridge.IsAvailable
                         && !ScrapeProfile.IsEmpty(_data);

        Button scrapeButton = new()
        {
            Content = "按配置刮削本游戏",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = canScrape,
        };
        scrapeButton.Click += (_, _) =>
        {
            ScrapeByCategoryTask task = new(_hostApi, _data, [game]);
            _ = _hostApi.AddBgTask(task);
        };
        stack.Children.Add(scrapeButton);

        Button staffButton = new()
        {
            Content = "补抓本游戏制作人员",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        staffButton.Click += (_, _) =>
        {
            FetchStaffTask task = new(_hostApi, [game]);
            _ = _hostApi.AddBgTask(task);
        };
        stack.Children.Add(staffButton);

        if (!canScrape)
        {
            stack.Children.Add(new TextBlock
            {
                Text = HostBridge.IsAvailable
                    ? "未勾选任何刮削类别，请到插件设置中配置。"
                    : $"未能接管宿主刮削服务：{HostBridge.ErrorMessage}",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return Task.FromResult<FrameworkElement>(new Prefabs.Panel { Content = stack });
    }
}
