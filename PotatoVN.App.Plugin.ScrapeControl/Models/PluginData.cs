using CommunityToolkit.Mvvm.ComponentModel;

namespace PotatoVN.App.Plugin.ScrapeControl.Models;

/// <summary>
/// 插件持久化数据。
///
/// 标记为 <see cref="ObservableProperty"/> 的字段在变化时会触发自动保存（见 Plugin.SaveData），
/// 因此新增设置项请一律使用 ObservableProperty，不要写成普通属性。
/// </summary>
public partial class PluginData : ObservableRecipient
{
    /// <summary>数据版本号，用于日后结构变更时的迁移。</summary>
    public int Version { get; set; } = 1;

    // ---------------- 六大刮削分类 ----------------
    // 前五项对应宿主的 GameParseType 标志位，第六项（Staff）宿主没有开关，
    // 由本插件通过卸载事件监听实现，是本插件存在的主要原因。

    /// <summary>游戏信息：游戏名、简介、会社、发售日期、标签、评分、预计时长等。</summary>
    [ObservableProperty] private bool _gameInfo = true;

    /// <summary>封面图。</summary>
    [ObservableProperty] private bool _image = true;

    /// <summary>标题图（详情页顶部横幅）。</summary>
    [ObservableProperty] private bool _headerImage = true;

    /// <summary>角色信息。角色多时会显著变慢，且每人要下载两张图片。</summary>
    [ObservableProperty] private bool _character = true;

    /// <summary>游玩状态（把 Bangumi / VNDB 上的评论、评分、游玩状态同步回来）。</summary>
    [ObservableProperty] private bool _playStatus = true;

    /// <summary>
    /// 制作人员（原画、编剧、音乐等）。
    ///
    /// 默认关闭：这是刮削最慢的一环——每位 staff 要单独发一次 API 请求并下载一张头像，
    /// 一个游戏动辄几十人，上百个游戏累计几千次请求。
    /// 关闭后插件会直接摘掉宿主的 staff 监听，任何入口的刮削都不再触发它。
    /// </summary>
    [ObservableProperty] private bool _staff;

    // ---------------- 行为选项 ----------------

    /// <summary>
    /// 是否连带处理子游戏库。
    /// 对应宿主对话框里的"包含子文件夹"。
    /// </summary>
    [ObservableProperty] private bool _includeSubSources;

    /// <summary>
    /// 跳过已经刮过对应信息的游戏。
    /// 关掉则每次都全量重刮。
    /// </summary>
    [ObservableProperty] private bool _skipAlreadyFetched = true;

    /// <summary>
    /// 两个游戏之间的间隔档位，用于缓解信息源限流。
    /// 界面用下拉框绑定索引，避免数值控件与 int 字段之间的类型转换麻烦。
    /// </summary>
    [ObservableProperty] private int _delayIndex = 1;

    /// <summary>把档位换算成实际毫秒数。</summary>
    public int DelayBetweenGamesMs => DelayIndex switch
    {
        0 => 0,
        1 => 200,
        2 => 500,
        3 => 1000,
        _ => 200,
    };
}
