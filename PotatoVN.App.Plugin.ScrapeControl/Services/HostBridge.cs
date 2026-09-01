using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GalgameManager.Models;
using GalgameManager.WinApp.Base.Contracts;

namespace PotatoVN.App.Plugin.ScrapeControl.Services;

/// <summary>
/// 与宿主内部服务打交道的桥接层。
///
/// 宿主公开给插件的 <see cref="IPotatoVnApi"/> 里并没有"刮削"和"关闭制作人抓取"这两项能力，
/// 而它们恰恰是刮削耗时的最大来源。本类通过反射拿到宿主内部服务，把这两项能力补上。
///
/// 关于 GameParseType：
/// 该枚举定义在宿主主程序（GalgameManager）里，而插件只能引用 GalgameManager.WinApp.Base，
/// 编译期拿不到这个类型。因此这里一律用「整数位掩码 + 运行时 Enum.ToObject」来回换算，
/// 完全不依赖编译期类型。
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

    private static Type? _parseTypeEnum;
    private static readonly Dictionary<string, long> ParseFlags = new(StringComparer.Ordinal);

    private static bool _initialized;
    private static string? _error;

    public static bool IsAvailable => _initialized && _galgameService is not null && _parseMethod is not null;
    public static bool CanControlStaff => _mutatedEventField is not null && _galgameService is not null;
    public static bool StaffDetached => _detachedStaffHandler is not null;
    public static string? ErrorMessage => _error;

    /// <summary>是否拿到了 GameParseType 枚举。拿不到就无法按类别刮削。</summary>
    public static bool HasParseTypeEnum => _parseTypeEnum is not null;

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
            LocateParseTypeEnum(hostApi);
        }
        catch (Exception e)
        {
            _error = e.Message;
        }
    }

    // ------------------------------------------------------------------ 定位服务

    private static bool LocateGalgameService(IPotatoVnApi hostApi)
    {
        if (TryLocateViaAppServiceLocator(hostApi)) return true;
        return TryScanHostApiFields(hostApi);
    }

    private static bool TryLocateViaAppServiceLocator(IPotatoVnApi hostApi)
    {
        Assembly? assembly = hostApi.GetType().Assembly;
        if (assembly is null) return false;

        Type? appType = assembly.GetType("GalgameManager.App")
                        ?? assembly.GetTypes().FirstOrDefault(t => t.Name == "App" && t.IsClass);
        Type? iface = assembly.GetType("GalgameManager.Contracts.Services.IGalgameCollectionService")
                      ?? assembly.GetTypes().FirstOrDefault(t =>
                          t.IsInterface && t.Name == "IGalgameCollectionService");
        if (appType is null || iface is null) return false;

        MethodInfo? getService = appType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
        if (getService is null) return false;

        object? service = SafeGet(() => getService.MakeGenericMethod(iface).Invoke(null, null));
        return service is not null && TryAdoptGalgameService(service);
    }

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
            type = type.BaseType;
        }
        return false;
    }

    private static bool TryAdoptGalgameService(object candidate)
    {
        MethodInfo? parse = FindParseMethod(candidate.GetType());
        if (parse is null) return false;

        _galgameService = candidate;
        _parseMethod = parse;
        return true;
    }

    /// <summary>
    /// 按签名查找刮削方法：ParseGalInfoAsync(Galgame, RssType, bool, GameParseType)。
    /// 刻意不校验第 1/4 个参数的具体类型——它们位于插件引用不到的主程序里。
    /// </summary>
    private static MethodInfo? FindParseMethod(Type type)
    {
        foreach (MethodInfo method in type.GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "ParseGalInfoAsync", StringComparison.Ordinal)) continue;

            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length < 4) continue;
            if (ps[0].ParameterType.Name != nameof(Galgame)) continue;
            if (ps[2].ParameterType != typeof(bool)) continue;
            if (!ps[3].ParameterType.IsEnum) continue;

            return method;
        }
        return null;
    }

    private static void LocateStaffService(IPotatoVnApi hostApi)
    {
        Assembly? assembly = hostApi.GetType().Assembly;
        if (assembly is null) return;

        Type? appType = assembly.GetType("GalgameManager.App")
                        ?? assembly.GetTypes().FirstOrDefault(t => t.Name == "App" && t.IsClass);
        // 制作人服务的接口在 Server 命名空间下，与其他服务不同
        Type? staffIface = assembly.GetType("GalgameManager.Server.Contracts.IStaffService")
                           ?? assembly.GetTypes().FirstOrDefault(t =>
                               t.IsInterface && t.Name == "IStaffService");
        if (appType is null || staffIface is null) return;

        MethodInfo? getService = appType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Static);
        if (getService is null) return;

        object? service = SafeGet(() => getService.MakeGenericMethod(staffIface).Invoke(null, null));
        if (service is null) return;

        _staffService = service;
        _parseStaffForGame = service.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
                m.Name == "ParseStaffAsync" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.Name == nameof(Galgame));
    }

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

    /// <summary>
    /// 从已加载的程序集里找 GameParseType 枚举，并把每个成员的值缓存成整数。
    /// 只在插件引用不到的主程序里定义，所以必须运行时查找。
    /// </summary>
    private static void LocateParseTypeEnum(IPotatoVnApi hostApi)
    {
        Assembly[] candidates =
        [
            hostApi.GetType().Assembly,
            _galgameService?.GetType().Assembly ?? typeof(object).Assembly,
        ];

        foreach (Assembly assembly in candidates)
        {
            Type? found = SafeGet(() => assembly.GetType("GalgameManager.Enums.GameParseType"))
                          as Type
                          ?? SafeGet(() => assembly.GetTypes().FirstOrDefault(t =>
                              t.IsEnum && t.Name == "GameParseType")) as Type;

            if (found is null) continue;

            _parseTypeEnum = found;
            ParseFlags.Clear();
            foreach (string name in Enum.GetNames(found))
            {
                object? value = SafeGet(() => Enum.Parse(found, name));
                if (value is null) continue;
                ParseFlags[name] = Convert.ToInt64(value);
            }
            return;
        }
    }

    /// <summary>取某个刮削类别的位值，取不到返回 0。</summary>
    public static long Flag(string name) =>
        ParseFlags.TryGetValue(name, out long value) ? value : 0;

    /// <summary>把位掩码还原成宿主认识的枚举对象。</summary>
    public static object? MaskToParseType(long mask)
    {
        if (_parseTypeEnum is null) return null;
        try
        {
            return Enum.ToObject(_parseTypeEnum, unchecked((int)mask));
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ 制作人开关

    public static bool DetachStaff()
    {
        if (!CanControlStaff || _galgameService is null || _mutatedEventField is null) return false;
        if (_detachedStaffHandler is not null) return true;

        try
        {
            if (_mutatedEventField.GetValue(_galgameService) is not Delegate current) return false;

            foreach (Delegate handler in current.GetInvocationList())
            {
                Type? targetType = handler.Target?.GetType();
                if (targetType is null) continue;
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

    public static bool AttachStaff()
    {
        if (_galgameService is null || _mutatedEventField is null) return false;
        if (_detachedStaffHandler is null) return true;

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
    /// 按位掩码刮削单个游戏。
    /// 参数按「目标方法的实际形参类型」逐个构造，避免枚举类型来自不同程序集导致 Invoke 失败。
    /// </summary>
    public static async Task<bool> ScrapeAsync(Galgame game, long mask)
    {
        if (!IsAvailable || _parseMethod is null) return false;

        try
        {
            ParameterInfo[] ps = _parseMethod.GetParameters();
            object?[] args = new object?[ps.Length];

            for (int i = 0; i < ps.Length; i++)
            {
                Type t = ps[i].ParameterType;

                if (t.IsInstanceOfType(game)) args[i] = game;
                else if (t == typeof(bool)) args[i] = false;               // requireConfirm
                else if (t.IsEnum)
                {
                    args[i] = t.Name == "GameParseType"
                        ? MaskToParseType(mask)
                        : Enum.ToObject(t, 0);                             // RssType.None
                }
                else args[i] = null;
            }

            if (_parseMethod.Invoke(_galgameService, args) is Task task) await task;
            return true;
        }
        catch (Exception e)
        {
            _error = e.InnerException?.Message ?? e.Message;
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
