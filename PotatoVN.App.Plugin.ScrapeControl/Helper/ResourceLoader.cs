using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace PotatoVN.App.Plugin.ScrapeControl.Helper;

/// <summary>
/// 插件ResourceDictionary加载器，负责加载插件的XAML资源字典，并在插件卸载时清理它们。
/// 如果你希望新增ResourceDictionary的xaml，请务必在Initialize中调用Add方法加载它
/// </summary>
internal static class ResourceLoader
{
    private static readonly List<ResourceDictionary> Dictionaries = [];
    private static bool _loaded;

    public static void Initialize()
    {
        if (_loaded) return;
        Plugin.HostApi.InvokeOnMainThread(() =>
        {
            Add("Controls/Styles/FontSizes.xaml");
            Add("Controls/Styles/TextBlock.xaml");
            Add("Controls/Styles/Thickness.xaml");
        });
        _loaded = true;
    }

    private static void Add(string relativePath)
    {
        try
        {
            var dictionary = new ResourceDictionary
            {
                Source = XamlResourceLocatorFactory.CreateFromRelativePath(relativePath)
            };
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
            Dictionaries.Add(dictionary);
        }
        catch (Exception e)
        {
            // 样式字典加载失败不该让整个插件挂掉。
            // 插件 UI 全部用代码构建并自带回退样式，缺这几个字典只是不好看，不影响功能。
            Plugin.HostApi?.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                $"[ScrapeControl] 样式 {relativePath} 加载失败，已跳过：{e.Message}");
        }
    }

    public static void Unload()
    {
        Plugin.HostApi.InvokeOnMainThread(() =>
        {
            foreach (var dictionary in Dictionaries)
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        });
        Dictionaries.Clear();
        _loaded = false;
    }
}