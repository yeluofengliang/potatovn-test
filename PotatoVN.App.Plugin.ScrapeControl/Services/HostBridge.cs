using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts;

namespace PotatoVN.App.Plugin.ScrapeControl.Services;

/// <summary>
/// 与宿主内部服务打交道的桥接层。
///
/// 宿主公开给插件的 <see cref="IPotatoVnApi"/> 里并没有"刮削"和"关闭制作人抓取"这两项能力，
/// 而它们恰恰是刮削耗时的最大来源。本类通过反射拿到宿主内部服务，把这两项能力补上。
///
/// 设计原则：
/// 1. 反射只做一次并缓存结果，避免每次刮削都去翻类型；
/// 2. 按"方法签名"找目标，而不是硬编码字段名，宿主小改不易失效；
/// 3. 任何一步失败都只记录原因、安静降级，绝不把异常抛给宿主。
/// </summary>
public static class HostBridge
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static object? _galgameService;
    private static MethodInfo? _parseMethod;
    private static object? _staffService;
    private static MethodInfo? _parseStaffForGame;
    private static FieldInfo? _mutatedEventField;
    private static Delegate? _detachedStaffHandler;

    private static bool _initialized;
    private static string? _error;

    /// <summary>宿主服务是否定位成功。</summary>
    public static bool IsAvailable => _initialized && _galgameService is not null && _parseMethod is not null;

    /// <summary>制作人抓取是否可被本插件控制。</summary>
    public static bool CanControlStaff => _mutatedEventField is not null && _galgameService is not null;

    /// <summary>制作人监听当前是否已摘除。</summary>
    public static bool StaffDetached => _detachedStaffHandler is not null;

    /// <summary>失败原因，用于向用户提示。</summary>
    public static string? ErrorMessage => _error;

    /// <summary>
    /// 在插件初始化时调用一次，探测宿主能力。
    /// </summary>
    public static void Initialize(IPotatoVnApi hostApi)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            if (!LocateGalgameService(hostApi))
            {
                _error = "未能定位宿主的刮削服务";
                return;
            }

            LocateStaffService(hostApi);
            PrepareMutatedEventField();
        }
        catch (Exception e)
        {
            _error = e.Message;
        }
    }

    // ------------------------------------------------------------------ 定位服务

    /// <summary>
    /// 定位游戏集合服务：优先走宿主自己的服务定位器，失败再扫描 hostApi 的字段。
    /// </summary>
    private static bool LocateGalgameService(IPotatoVnApi hostApi)
    {
        if (TryLocateViaAppServiceLocator(hostApi)) return true;
        return TryScanHostApiFields(hostApi);
    }

    /// <summary>通过 App.GetService&lt;T&gt;() 拿服务，最直接也最稳。</summary>
    private static bool TryLocateViaAppServiceLocator(IPotatoVnApi hostApi)
    {
        Assembly? assembly = hostApi.GetType().Assembly;
        if (assembly is null) return false;

        // 命名空间来自宿主实现，若将来调整则这里会失配，交由下面的字段扫描兜底
        Type? appType = assembly.GetType("GalgameManager.App")
                        ?? assembly.GetTypes().FirstOrDefault(t => t.Name == "App" && t.IsClass);
        Type? iface = assembly.GetType("GalgameManager.Contracts.Services.IGalgameCollectionService")
                      ?? assembly.GetTypes().FirstOrDefault(t =>
                          t.IsInterface && t.Name == "IGalgameCollectionService");
        if (appType is null || iface is null) return false;

        MethodInfo? getService = appType.GetMethod("GetService",
            BindingFlags.Public | BindingFlags.Static);
        if (getService is null) return false;

        object? service = getService.MakeGenericMethod(iface).Invoke(null, null);
        return service is not null && TryAdoptGalgameService(service);
    }

    /// <summary>兜底：在 hostApi 实例（及其基类）的成员里找带刮削方法的对象。</summary>
    private static bool TryScanHostApiFields(IPotatoVnApi hostApi)
    {
        Type? type = hostApi.GetType();
        while (type is not null && type != typeof(object))
        {
            foreach (FieldInfo field in type.GetFields(Declared))
            {
                object? value = SafeGet(() => field.GetValue(field.IsStatic ? null : hostApi));
                if (value is not null && TryAdoptGalgameService(value)) return true;
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                object? value = SafeGet(() =>
                    property.GetValue(property.GetMethod?.IsStatic == true ? null : hostApi));
                if (value is not null && TryAdoptGalgameService(value)) return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    /// <summary>检查对象是否为游戏集合服务（即是否具备刮削方法），是则缓存。</summary>
    private static bool TryAdoptGalgameService(object candidate)
    {
        MethodInfo? parse = FindParseMethod(candidate.GetType());
        if (parse is null) return false;

        _galgameService = candidate;
        _parseMethod = parse;
        return true;
    }

    /// <summary>
    /// 按名字与签名查找刮削方法。刻意不要求参数类型完全一致，只要能接收即可。
    /// </summary>
    private static MethodInfo? FindParseMethod(Type type)
    {
        foreach (MethodInfo method in type.GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "ParseGalInfoAsync", StringComparison.Ordinal)) continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 4) continue;

            // (Galgame galgame, RssType rssType, bool requireConfirm, GameParseType type)
            if (!parameters[0].ParameterType.IsAssignableFrom(typeof(Galgame))) continue;
            if (parameters[2].ParameterType != typeof(bool)) continue;

            return method;
        }

        return null;
    }

    /// <summary>定位制作人服务，并缓存用于手工补抓的方法。</summary>
    private static void LocateStaffService(IPotatoVnApi hostApi)
    {
        Assembly? assembly = hostApi.GetType().Assembly;
        if (assembly is null) return;

        Type? appType = assembly.GetType("GalgameManager.App")
                        ?? assembly.GetTypes().FirstOrDefault(t => t.Name == "App" && t.IsClass);

        // 注意：制作人服务的接口在 Server 命名空间下，与其他服务不同
        Type? staffIface = assembly.GetType("GalgameManager.Server.Contracts.IStaffService")
                           ?? assembly.GetTypes().FirstOrDefault(t =>
                               t.IsInterface && t.Name == "IStaffService");
        if (appType is null || staffIface is null) return;

        MethodInfo? getService = appType.GetMethod("GetService",
            BindingFlags.Public | BindingFlags.Static);
        if (getService is null) return;

        object? service = SafeGet(() => getService.MakeGenericMethod(staffIface).Invoke(null, null));
        if (service is null) return;

        _staffService = service;

        // ParseStaffAsync(Galgame) 是公开方法，用于手工补抓
        _parseStaffForGame = service.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                m.Name == "ParseStaffAsync" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(Galgame)));
    }

    /// <summary>准备 GalgameMutated 事件的后备字段，制作人抓取就挂在这个事件上。</summary>
    private static void PrepareMutatedEventField()
    {
        if (_galgameService is null) return;

        Type? type = _galgameService.GetType();
        while (type is not null && type != typeof(object))
        {
            FieldInfo? field = type.GetField("GalgameMutated", Declared);
            if (field is not null && typeof(Delegate).IsAssignableFrom(field.FieldType))
            {
                _mutatedEventField = field;
                return;
            }

            type = type.BaseType;
        }
    }

    // ------------------------------------------------------------------ 制作人开关

    /// <summary>
    /// 摘除宿主的"刮完游戏信息就自动抓制作人"的监听。
    ///
    /// 宿主在 StaffService 构造时挂上：
    /// <c>galgameService.GalgameMutated += OnGalgameMutated;</c>
    /// 只要 ParsedTypes 含 GameInfo 就会触发，且没有任何开关能关掉它。
    /// 摘除后所有入口的刮削都不会再抓制作人，想抓时用 <see cref="FetchStaffAsync"/> 手动补。
    /// </summary>
    public static bool DetachStaff()
    {
        if (!CanControlStaff || _galgameService is null || _mutatedEventField is null) return false;
        if (_detachedStaffHandler is not null) return true;   // 已经摘过了

        try
        {
            if (_mutatedEventField.GetValue(_galgameService) is not Delegate current) return false;

            foreach (Delegate handler in current.GetInvocationList())
            {
                Type? targetType = handler.Target?.GetType();
                if (targetType is null) continue;

                // 匹配制作人服务（含其子类/包装类型）
                if (!targetType.Name.Contains("StaffService", StringComparison.Ordinal)) continue;

                Delegate? reduced = Delegate.Remove(current, handler);
                if (reduced is null) continue;

                _mutatedEventField.SetValue(_galgameService, reduced);
                _detachedStaffHandler = handler;
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            _error = e.Message;
            return false;
        }
    }

    /// <summary>把之前摘掉的监听挂回去，恢复宿主的默认行为。</summary>
    public static bool AttachStaff()
    {
        if (_galgameService is null || _mutatedEventField is null) return false;
        if (_detachedStaffHandler is null) return true;      // 本来就没摘

        try
        {
            Delegate? current = _mutatedEventField.GetValue(_galgameService) as Delegate;
            Delegate combined = current is null
                ? _detachedStaffHandler
                : Delegate.Combine(current, _detachedStaffHandler);

            _mutatedEventField.SetValue(_galgameService, combined);
            _detachedStaffHandler = null;
            return true;
        }
        catch (Exception e)
        {
            _error = e.Message;
            return false;
        }
    }

    /// <summary>手工补抓某个游戏的制作人。</summary>
    public static async Task<bool> FetchStaffAsync(Galgame game)
    {
        if (_staffService is null || _parseStaffForGame is null) return false;

        try
        {
            if (_parseStaffForGame.Invoke(_staffService, [game]) is Task task) await task;
            return true;
        }
        catch (Exception e)
        {
            _error = e.Message;
            return false;
        }
    }

    // ------------------------------------------------------------------ 执行刮削

    /// <summary>
    /// 按指定的信息类别组合刮削单个游戏。
    /// 与宿主"更新游戏信息"走的是同一个方法，但类别由调用方精确控制。
    /// </summary>
    public static async Task<bool> ScrapeAsync(Galgame game, GameParseType parseType)
    {
        if (!IsAvailable || _parseMethod is null) return false;

        try
        {
            object?[] args = [game, RssType.None, false, parseType];
            if (_parseMethod.Invoke(_galgameService, args) is Task task) await task;
            return true;
        }
        catch (Exception e)
        {
            // 反射调用会把真实异常包在 TargetInvocationException 里，取内层更可读
            string msg = e.InnerException?.Message ?? e.Message;
            _error = msg;
            return false;
        }
    }

    private static object? SafeGet(Func<object?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }
}
