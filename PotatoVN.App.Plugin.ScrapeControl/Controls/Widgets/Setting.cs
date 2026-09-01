using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PotatoVN.App.Plugin.ScrapeControl.Controls.Widgets;

/// <summary>
/// 一行设置项：左侧标题与说明，右侧操作控件。
///
/// 纯 C# 实现，理由同 <see cref="Panel"/>。
/// 这里刻意使用普通 CLR 属性而非依赖属性——不使用 XAML 就不需要绑定，
/// 普通属性更直接也更不容易出错。
/// </summary>
public sealed class Setting : UserControl
{
    private readonly TextBlock _title = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _description = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Opacity = 0.75,
        Margin = new Thickness(0, 2, 0, 0),
        Visibility = Visibility.Collapsed,   // 无说明时不占位，避免出现一段空白
    };

    private readonly ContentPresenter _contentArea = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    public Setting()
    {
        StackPanel textStack = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(_title);
        textStack.Children.Add(_description);

        Grid grid = new() { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(textStack, 0);
        grid.Children.Add(textStack);

        Grid.SetColumn(_contentArea, 1);
        grid.Children.Add(_contentArea);

        base.Content = grid;
    }

    /// <summary>标题。</summary>
    public string TitleText
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>标题下方的灰色说明文字。</summary>
    public string DescriptionText
    {
        get => _description.Text;
        set
        {
            _description.Text = value;
            _description.Visibility = string.IsNullOrWhiteSpace(value)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>右侧的操作控件，如开关、下拉框。</summary>
    public object? Body
    {
        get => _contentArea.Content;
        set => _contentArea.Content = value;
    }
}
