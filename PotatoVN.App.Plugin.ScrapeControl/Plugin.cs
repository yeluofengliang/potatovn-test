using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PotatoVN.App.Plugin.ScrapeControl.Controls;
using PotatoVN.App.Plugin.ScrapeControl.Helper;
using PotatoVN.App.Plugin.ScrapeControl.Models;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl;

public partial class Plugin : IPlugin, IPluginSetting
{
    public static IPotatoVnApi HostApi { get; private set; } = null!;

    private IPotatoVnApi _hostApi = null!;
    private PluginData _data = new();
    private SettingsControl? _settingsUi;

    public PluginInfo Info { get; } = new()
    {
        Id = new Guid("b4197869-6f10-4f5d-9460-9bbda98f622d"),
        Name = "刮削控制",
        Description = "按类别控制刮削内容，并关掉最耗时的制作人抓取。\n"
                      + "支持极速/标准/丰富等预设，可随时取消，制作人可单独补抓。",
    };

    public async Task InitializeAsync(IPotatoVnApi hostApi)
    {
        _hostApi = hostApi;
        HostApi = hostApi;
        XamlResourceLocatorFactory.PackagePath = hostApi.GetPluginPath();
        ResourceLoader.Initialize();

        // 接管刮削与制作人控制必须尽早完成，否则插件只剩一个空壳界面
        HostBridge.Initialize(hostApi);

        string? dataJson = await hostApi.GetDataAsync();
        if (!string.IsNullOrWhiteSpace(dataJson))
        {
            try
            {
                _data = JsonSerializer.Deserialize<PluginData>(dataJson) ?? new PluginData();
            }
            catch (Exception e)
            {
                hostApi.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                    $"[ScrapeControl] 插件数据读取失败，已重置：{e.Message}");
                _data = new PluginData();
            }
        }

        MigrateData();

        // ObservableProperty 变化时自动落盘；制作人开关需要额外联动宿主监听
        _data.PropertyChanged += (_, args) =>
        {
            SaveData();
            if (args.PropertyName == nameof(PluginData.Staff)) ApplyStaffSwitch();
            _settingsUi?.RefreshStatus();
        };

        ApplyStaffSwitch();
        InitUi();

        if (!HostBridge.IsAvailable)
            hostApi.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                $"[ScrapeControl] 未能接管宿主刮削服务：{HostBridge.ErrorMessage}");
        else if (!HostBridge.CanControlStaff)
            hostApi.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                "[ScrapeControl] 未能定位制作人监听，制作人开关不可用");
    }

    /// <summary>数据校验与迁移。当前只有 v1，保留结构以便日后平滑升级。</summary>
    private void MigrateData()
    {
        bool changed = false;

        if (_data.Version < 1)
        {
            _data.Version = 1;
            changed = true;
        }

        // 下拉框索引越界会让 ComboBox 绑定回写 -1，这里统一夹回合法范围
        if (_data.DelayIndex is < 0 or > 3)
        {
            _data.DelayIndex = 1;
            changed = true;
        }

        if (changed) SaveData();
    }

    /// <summary>
    /// 让宿主的"自动抓制作人"与插件开关保持一致。
    /// 关闭时摘掉监听（任何入口都不再触发），开启时挂回去。
    /// </summary>
    private void ApplyStaffSwitch()
    {
        _hostApi.InvokeOnMainThread(() =>
        {
            try
            {
                if (_data.Staff) HostBridge.AttachStaff();
                else HostBridge.DetachStaff();
            }
            catch (Exception e)
            {
                _hostApi.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                    $"[ScrapeControl] 制作人开关联动失败：{e.Message}");
            }
        });
    }

    /// <summary>
    /// 卸载时务必把监听还回去。
    /// 否则宿主会一直处于"没人抓制作人"的状态，用户卸载插件后也恢复不了。
    /// </summary>
    public Task OnUninstallAsync(bool deleteData, Action<TimeSpan> extendWaitHandler, CancellationToken cts)
    {
        if (cts.IsCancellationRequested) return Task.FromCanceled(cts);

        try
        {
            HostBridge.AttachStaff();
        }
        catch
        {
            // 卸载流程不应因恢复失败而中断
        }

        ResourceLoader.Unload();
        return Task.CompletedTask;
    }

    private void SaveData()
    {
        try
        {
            string json = JsonSerializer.Serialize(_data);
            _ = _hostApi.SaveDataAsync(json);
        }
        catch (Exception e)
        {
            _hostApi.Log(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                $"[ScrapeControl] 插件数据保存失败：{e.Message}");
        }
    }

    public FrameworkElement CreateSettingUi()
    {
        try
        {
            _settingsUi = new SettingsControl(_data);
            return _settingsUi;
        }
        catch (Exception e)
        {
            // 界面构建失败时绝不能把异常抛给宿主——那会让整个软件崩溃。
            // 改成把错误直接渲染在设置面板里，用户既能继续用，也能把原因报给我们。
            return BuildErrorUi("设置界面创建失败", e);
        }
    }

    /// <summary>
    /// 把异常渲染成可滚动、可选中的文本块。
    /// 附带诊断信息，便于在不装调试器的情况下定位问题。
    /// </summary>
    private FrameworkElement BuildErrorUi(string title, Exception e)
    {
        string detail =
            $"{title}\n\n" +
            $"{e.GetType().Name}: {e.Message}\n\n" +
            $"内部异常：{e.InnerException?.Message}\n\n" +
            $"{e.StackTrace}\n\n" +
            "--- 诊断信息 ---\n" +
            $"刮削服务可用: {HostBridge.IsAvailable}\n" +
            $"制作人可控: {HostBridge.CanControlStaff}\n" +
            $"类别枚举: {HostBridge.HasParseTypeEnum}\n" +
            $"宿主错误: {HostBridge.ErrorMessage}\n" +
            $"插件路径: {XamlResourceLocatorFactory.PackagePath}";

        return new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
                FontSize = 12,
                IsTextSelectionEnabled = true,
            },
        };
    }

    protected Guid Id => Info.Id;
}
