using PotatoVN.App.Plugin.ScrapeControl.Models;
using PotatoVN.App.Plugin.ScrapeControl.Services;

namespace PotatoVN.App.Plugin.ScrapeControl.Services;

/// <summary>
/// 刮削类别的位掩码组合与预设。
///
/// GameParseType 枚举位于插件引用不到的宿主主程序里，因此这里一律用整数位掩码表示，
/// 由 <see cref="HostBridge"/> 在运行时翻译成真正的枚举对象。
/// </summary>
public static class ScrapeProfile
{
    /// <summary>把插件的五个开关拼成位掩码。</summary>
    /// <remarks>
    /// 制作人不在刮削枚举里——它是宿主挂在事件上的独立流程，
    /// 由 HostBridge 单独控制，因此这里不包含它。
    /// </remarks>
    public static long Build(PluginData data)
    {
        long mask = 0;

        if (data.GameInfo)    mask |= HostBridge.Flag(nameof(GameParseType.GameInfo));
        if (data.Image)       mask |= HostBridge.Flag("Image");
        if (data.HeaderImage) mask |= HostBridge.Flag("HeaderImage");
        if (data.Character)   mask |= HostBridge.Flag("Character");
        if (data.PlayStatus)  mask |= HostBridge.Flag("PlayStatus");

        return mask;
    }

    /// <summary>一个类别都没勾。</summary>
    public static bool IsEmpty(PluginData data) => CountEnabled(data) == 0;

    /// <summary>勾选了几个类别（不含制作人）。</summary>
    public static int CountEnabled(PluginData data)
    {
        int count = 0;
        if (data.GameInfo)    count++;
        if (data.Image)       count++;
        if (data.HeaderImage) count++;
        if (data.Character)   count++;
        if (data.PlayStatus)  count++;
        return count;
    }

    public static void Apply(Preset preset, PluginData data)
    {
        switch (preset)
        {
            case Preset.Fastest:
                data.GameInfo = true;
                data.Image = false;
                data.HeaderImage = false;
                data.Character = false;
                data.PlayStatus = false;
                data.Staff = false;
                break;

            case Preset.Standard:
                data.GameInfo = true;
                data.Image = true;
                data.HeaderImage = false;
                data.Character = false;
                data.PlayStatus = false;
                data.Staff = false;
                break;

            case Preset.Rich:
                data.GameInfo = true;
                data.Image = true;
                data.HeaderImage = true;
                data.Character = true;
                data.PlayStatus = false;
                data.Staff = false;
                break;

            case Preset.Everything:
                data.GameInfo = true;
                data.Image = true;
                data.HeaderImage = true;
                data.Character = true;
                data.PlayStatus = true;
                data.Staff = true;
                break;
        }
    }

    public static string Describe(PluginData data)
    {
        int count = CountEnabled(data);
        if (count == 0 && !data.Staff) return "当前未勾选任何内容，刮削不会执行";

        string text = count == 0 ? "仅制作人" : $"已选 {count} 类";
        if (data.Staff) text += " + 制作人（很慢）";
        return text;
    }

    /// <summary>仅供按名字取枚举值使用，避免拼错字符串。</summary>
    private static class GameParseType
    {
        public const string GameInfo = "GameInfo";
    }
}

public enum Preset
{
    Fastest,
    Standard,
    Rich,
    Everything,
}
