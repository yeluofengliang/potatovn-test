using GalgameManager.Enums;
using PotatoVN.App.Plugin.ScrapeControl.Models;

namespace PotatoVN.App.Plugin.ScrapeControl.Services;

/// <summary>
/// 刮削类别的组合与预设。
///
/// 宿主的 <see cref="GameParseType"/> 是标志位枚举，本类负责在"插件设置的六个开关"
/// 与"宿主理解的标志位"之间做翻译，并提供几套常用预设。
/// </summary>
public static class ScrapeProfile
{
    /// <summary>把插件的六个开关翻译成宿主的刮削标志位。</summary>
    /// <remarks>
    /// 制作人不在 <see cref="GameParseType"/> 里——它是宿主挂在事件上的独立流程，
    /// 由 <see cref="HostBridge"/> 单独控制，因此这里不包含它。
    /// </remarks>
    public static GameParseType Build(PluginData data)
    {
        GameParseType result = GameParseType.None;

        if (data.GameInfo)   result |= GameParseType.GameInfo;
        if (data.Image)      result |= GameParseType.Image;
        if (data.HeaderImage) result |= GameParseType.HeaderImage;
        if (data.Character)  result |= GameParseType.Character;
        if (data.PlayStatus) result |= GameParseType.PlayStatus;

        return result;
    }

    /// <summary>是否一个类别都没勾。</summary>
    public static bool IsEmpty(GameParseType type) => type == GameParseType.None;

    /// <summary>当前勾选了几个类别（不含制作人）。</summary>
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

    // ------------------------------------------------------------------ 预设

    /// <summary>套用预设。只改刮削类别，不动"包含子库"等执行选项。</summary>
    public static void Apply(Preset preset, PluginData data)
    {
        switch (preset)
        {
            case Preset.Fastest:
                // 只要文字信息，一张图都不下
                data.GameInfo    = true;
                data.Image       = false;
                data.HeaderImage = false;
                data.Character   = false;
                data.PlayStatus  = false;
                data.Staff       = false;
                break;

            case Preset.Standard:
                // 信息 + 封面，够用且快
                data.GameInfo    = true;
                data.Image       = true;
                data.HeaderImage = false;
                data.Character   = false;
                data.PlayStatus  = false;
                data.Staff       = false;
                break;

            case Preset.Rich:
                // 加上大图与角色
                data.GameInfo    = true;
                data.Image       = true;
                data.HeaderImage = true;
                data.Character   = true;
                data.PlayStatus  = false;
                data.Staff       = false;
                break;

            case Preset.Everything:
                // 全开，等价于宿主默认的"全部"
                data.GameInfo    = true;
                data.Image       = true;
                data.HeaderImage = true;
                data.Character   = true;
                data.PlayStatus  = true;
                data.Staff       = true;
                break;
        }
    }

    /// <summary>给当前配置一句人话描述，用于设置页回显。</summary>
    public static string Describe(PluginData data)
    {
        int count = CountEnabled(data);
        if (count == 0 && !data.Staff) return "当前未勾选任何内容，刮削不会执行";

        string text = count == 0 ? "仅制作人" : $"已选 {count} 类";
        if (data.Staff) text += " + 制作人（很慢）";
        return text;
    }
}

/// <summary>常用预设。</summary>
public enum Preset
{
    /// <summary>极速：只要文字信息，不下载任何图片</summary>
    Fastest,
    /// <summary>标准：文字信息 + 封面图</summary>
    Standard,
    /// <summary>丰富：文字信息 + 封面 + 标题图 + 角色</summary>
    Rich,
    /// <summary>全部：包含制作人与游玩状态</summary>
    Everything,
}
