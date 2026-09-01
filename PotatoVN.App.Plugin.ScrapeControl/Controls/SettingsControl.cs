using System;
using System.Collections.Generic;
using System.Linq;
using GalgameManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using PotatoVN.App.Plugin.ScrapeControl.BgTasks;
// 脚手架的预设控件里有一个自定义的 Panel，与 Microsoft.UI.Xaml.Controls.Panel 重名。
// 这里用别名引用，避免每次 new Panel 都要写全限定名。
using Prefabs = PotatoVN.App.Plugin.ScrapeControl.Controls.Prefabs;
using PotatoVN.App.Plugin.ScrapeControl.Models;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl.Controls;

/// <summary>
/// 插件设置界面。
///
/// 用代码构建而非 XAML：脚手架会对命名空间做随机改写（namespace stamping），
/// XAML 相关断点难以命中；本界面控件不多，代码构建反而更容易排查。
/// 外观一律复用官方预设控件（Panel / Setting / StdStackPanel），保证与宿主风格一致。
/// </summary>
public sealed class SettingsControl : UserControl
{
    private readonly PluginData _data;
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summaryText = new() { TextWrapping = TextWrapping.Wrap };

    public SettingsControl(PluginData data)
    {
        _data = data;

        Content = new ScrollViewer
        {
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            Content = BuildRoot(),
        };

        RefreshStatus();
    }

private Prefabs.StdStackPanel BuildRoot()
    {
        Prefabs.StdStackPanel root = new();
        root.Children.Add(BuildStatusPanel());
        root.Children.Add(BuildPresetPanel());
        root.Children.Add(BuildCategoryPanel());
        root.Children.Add(BuildOptionPanel());
        root.Children.Add(BuildActionPanel());
        return root;
    }

    /// <summary>
    /// 给内容套一层内边距。
    /// 用 Grid 而非直接设 Padding——FrameworkElement 基类没有 Padding，
    /// 而 WinUI 的 StackPanel 同样没有，只有 Grid / Border / Control 等才有。
    /// </summary>
    private static FrameworkElement Wrap(FrameworkElement content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new Grid
        {
            Padding = new Thickness(18, 12, 18, 12),
            Children = { content },
        };
    }

    private static TextBlock Description(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = TryStyle("DescriptionTextStyle"),
    };

    private static Style? TryStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Style style
            ? style
            : null;

    private static void Bind(FrameworkElement target, DependencyProperty property, object source,
        string path, BindingMode mode = BindingMode.TwoWay)
    {
        target.SetBinding(property, new Binding
        {
            Source = source,
            Path = new PropertyPath(path),
            Mode = mode,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
    }

    private static UIElement ToggleSetting(string title, string description, object source, string path)
    {
        ToggleSwitch toggle = new();
        Bind(toggle, ToggleSwitch.IsOnProperty, source, path);
        return new Prefabs.Setting { Title = title, Description = description, Content = toggle };
    }

    // ------------------------------------------------------------------ 状态

    private UIElement BuildStatusPanel()
    {
        StackPanel stack = new() { Spacing = 6 };
        stack.Children.Add(_statusText);
        stack.Children.Add(_summaryText);
        return new Prefabs.Panel { Content = Wrap(stack) };
    }

    /// <summary>刷新接管状态与当前配置的说明。</summary>
    public void RefreshStatus()
    {
        if (!HostBridge.IsAvailable)
        {
            _statusText.Text = $"⚠ 未能接管宿主刮削服务（{HostBridge.ErrorMessage}）。"
                               + "本插件的刮削与制作人开关都不会生效，但界面仍可正常打开。";
        }
        else if (!HostBridge.CanControlStaff)
        {
            _statusText.Text = "⚠ 未能定位制作人监听，制作人开关不可用；其余类别可正常控制。";
        }
        else
        {
            _statusText.Text = _data.Staff
                ? "✓ 已接管。制作人抓取处于开启状态，刮削会连带抓取制作人（很慢）。"
                : "✓ 已接管。制作人抓取已关闭，任何入口的刮削都不会再触发它。";
        }

        _summaryText.Text = ScrapeProfile.Describe(_data);
    }

    // ------------------------------------------------------------------ 预设

    private UIElement BuildPresetPanel()
    {
        StackPanel stack = new() { Spacing = 10 };

        stack.Children.Add(Description(
            "预设会一次性改好下面的六个开关，选完仍可单独微调。"));

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(PresetButton("极速", "只要文字信息，一张图都不下", Preset.Fastest));
        buttons.Children.Add(PresetButton("标准", "信息 + 封面图，推荐", Preset.Standard));
        buttons.Children.Add(PresetButton("丰富", "再加标题图与角色", Preset.Rich));
        buttons.Children.Add(PresetButton("全部", "含制作人与游玩状态", Preset.Everything));
        stack.Children.Add(buttons);

        return new Prefabs.Panel { Content = Wrap(stack) };
    }

    private Button PresetButton(string label, string tooltip, Preset preset)
    {
        Button button = new()
        {
            Content = label,
            Padding = new Thickness(12, 5, 12, 5),
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) =>
        {
            ScrapeProfile.Apply(preset, _data);
            RefreshStatus();
        };
        return button;
    }

    // ------------------------------------------------------------------ 六个分类

    private UIElement BuildCategoryPanel()
    {
        StackPanel stack = new() { Spacing = 10 };

        stack.Children.Add(Description("决定刮削时实际去信息源取哪些内容。不勾的内容不会产生任何网络请求。"));

        stack.Children.Add(ToggleSetting("游戏信息",
            "游戏名、简介、会社、发售日期、标签、评分、预计时长等文字元数据。",
            _data, nameof(PluginData.GameInfo)));

        stack.Children.Add(ToggleSetting("封面图",
            "游戏列表里显示的小图。",
            _data, nameof(PluginData.Image)));

        stack.Children.Add(ToggleSetting("标题图",
            "详情页顶部的横向大图，体积较大。",
            _data, nameof(PluginData.HeaderImage)));

        stack.Children.Add(ToggleSetting("角色信息",
            "角色列表。角色多时很慢——每人要单独查一次并下载两张图片。",
            _data, nameof(PluginData.Character)));

        stack.Children.Add(ToggleSetting("游玩状态",
            "把 Bangumi / VNDB 上的评论、评分、游玩状态同步回来。需要已登录对应账号。",
            _data, nameof(PluginData.PlayStatus)));

        stack.Children.Add(ToggleSetting("制作人员",
            "原画、编剧、音乐等。这是刮削最慢的一环：每位制作人要单独发一次请求并下载头像，"
            + "上百个游戏累计可达数千次请求。关闭后可用下方的按钮按需补抓。",
            _data, nameof(PluginData.Staff)));

        return new Prefabs.Panel { Content = Wrap(stack) };
    }

    // ------------------------------------------------------------------ 执行选项

    private UIElement BuildOptionPanel()
    {
        StackPanel stack = new() { Spacing = 10 };

        ComboBox delayCombo = new() { MinWidth = 160 };
        delayCombo.ItemsSource = new[] { "不等待", "200 毫秒", "500 毫秒", "1 秒" };
        Bind(delayCombo, Selector.SelectedIndexProperty, _data, nameof(PluginData.DelayIndex));

        stack.Children.Add(new Setting
        {
            Title = "游戏之间间隔",
            Description = "批量刮削时每个游戏之间的等待时间，用于缓解信息源限流。被限流时可调大。",
            Content = delayCombo,
        });

        stack.Children.Add(ToggleSetting("跳过已刮过的内容",
            "已拿到对应信息的类别不再重复请求。首次入库或想强制刷新时可关掉。",
            _data, nameof(PluginData.SkipAlreadyFetched)));

        stack.Children.Add(ToggleSetting("包含子游戏库",
            "连带处理当前库下的子库。",
            _data, nameof(PluginData.IncludeSubSources)));

        return new Prefabs.Panel { Content = Wrap(stack) };
    }

    // ------------------------------------------------------------------ 操作

    private UIElement BuildActionPanel()
    {
        StackPanel stack = new() { Spacing = 10 };

        Button scrapeButton = new()
        {
            Content = "按以上配置刮削全部游戏",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = TryStyle("AccentButtonStyle"),
        };
        scrapeButton.Click += (_, _) => StartScrape();

        Button staffButton = new()
        {
            Content = "为全部游戏补抓制作人员",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        staffButton.Click += (_, _) => StartStaffFetch();

        stack.Children.Add(Description(
            "刮削在后台任务中执行，可随时取消。建议先用「极速」跑一遍把元数据补齐，"
            + "再按需补图片与角色。"));
        stack.Children.Add(scrapeButton);
        stack.Children.Add(Description(
            "制作人员建议单独补抓：它耗时极长，混在日常刮削里会严重拖慢进度。"));
        stack.Children.Add(staffButton);

        return new Prefabs.Panel { Content = Wrap(stack) };
    }

    private void StartScrape()
    {
        if (!HostBridge.IsAvailable)
        {
            Plugin.HostApi?.Info(InfoBarSeverity.Warning, "无法刮削",
                msg: $"未能接管宿主刮削服务：{HostBridge.ErrorMessage}", displayTimeMs: 5000);
            return;
        }

        if (ScrapeProfile.IsEmpty(_data) && !_data.Staff)
        {
            Plugin.HostApi?.Info(InfoBarSeverity.Warning, "没有勾选任何内容",
                "请至少勾选一个刮削类别。", displayTimeMs: 4000);
            return;
        }

        List<Galgame>? games = Plugin.HostApi?.GetAllGames();
        if (games is null || games.Count == 0)
        {
            Plugin.HostApi?.Info(InfoBarSeverity.Warning, "游戏库是空的", "没有找到任何游戏。");
            return;
        }

        ScrapeByCategoryTask task = new(Plugin.HostApi!, _data, games);
        _ = Plugin.HostApi!.AddBgTask(task);

        Plugin.HostApi?.Info(InfoBarSeverity.Informational, "已开始刮削",
            msg: $"共 {games.Count} 个游戏，可在后台任务中查看进度或取消。", displayTimeMs: 3000);
    }

    private void StartStaffFetch()
    {
        List<Galgame>? games = Plugin.HostApi?.GetAllGames();
        if (games is null || games.Count == 0) return;

        FetchStaffTask task = new(Plugin.HostApi!, games);
        _ = Plugin.HostApi!.AddBgTask(task);
    }
}
