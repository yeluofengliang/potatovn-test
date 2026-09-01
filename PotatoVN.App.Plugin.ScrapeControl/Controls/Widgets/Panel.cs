using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PotatoVN.App.Plugin.ScrapeControl.Controls.Widgets;

/// <summary>
/// 卡片式面板。
///
/// 纯 C# 实现，不依赖 XAML —— 脚手架的 XAML 定位靠运行时拼接路径：
/// <c>callerFilePath.LastIndexOf("Stamped") + 8</c> 这种方式在插件打包后极易失效，
/// 而 LoadComponent 失败会直接让宿主崩溃（点开插件设置时才会触发，安装时不加载所以看不出来）。
/// 按脚手架注释的建议改用纯代码构建 UI，从根本上绕开这一整类问题。
/// </summary>
public sealed class Panel : UserControl
{
    private readonly Border _root;

    public Panel()
    {
        _root = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        TryBrush("CardBackgroundFillColorDefaultBrush", b => _root.Background = b);
        TryBrush("CardStrokeColorDefaultBrush", b => _root.BorderBrush = b);

        base.Content = _root;
    }

    /// <summary>面板里承载的内容。</summary>
    public UIElement? Body
    {
        get => _root.Child;
        set => _root.Child = value;
    }

    /// <summary>
    /// 尝试套用宿主主题画刷。
    /// 拿不到就保持透明——样式是锦上添花，不能因为缺主题色就让用户看不到内容。
    /// </summary>
    private static void TryBrush(string key, Action<Brush> apply)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush)
            apply(brush);
    }
}
