using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using PclCeUnlock.Shared;

namespace PclCeUnlock.Injection;

/// <summary>用 MonoMod.RuntimeDetour 在内存中替换目标方法入口（不改任何文件）。</summary>
internal static class Patcher
{
    /// <summary>必须保持引用，否则 Hook 被 GC 会撤销替换。</summary>
    private static readonly List<Hook> Hooks = new();

    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    internal static void Run()
    {
        Logger.Note($"注入器启动（MonoMod {typeof(Hook).Assembly.GetName().Version}，" +
                    $"规格 {UnlockTargets.SpecVersion}），钩子目录 {LocalResolver.BaseDir}");
        Logger.Note($"目标方法 {UnlockTargets.Patches.Count} 个；不挂钩正版劝导相关方法：" +
                    string.Join(", ", UnlockTargets.MustNotChange.Select(x => x.TypeName + "." + x.Member)));

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) TryPatch(asm);
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            TryPatch(e.LoadedAssembly);
            Logger.Flush();
        };
        Logger.Flush();
    }

    private static void TryPatch(Assembly asm)
    {
        string name;
        try
        {
            name = asm.GetName().Name;
        }
        catch
        {
            return;
        }

        foreach (var target in UnlockTargets.Patches.Where(t => t.Assembly == name))
        {
            if (Done.Contains(target.Id)) continue;
            try
            {
                var type = asm.GetType(target.TypeName, false);
                if (type is null) continue;

                const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var method = target.IsProperty
                    ? type.GetProperty(target.Member, Flags)?.GetGetMethod(true)
                    : type.GetMethod(target.Member, Flags);

                if (method is null)
                {
                    Logger.Note($"未找到 {target.TypeName}.{target.Member}（上游可能改名，跳过）");
                    Done.Add(target.Id);
                    continue;
                }

                var detour = DetourMethod(target);
                Hooks.Add(new Hook(method, detour));
                Done.Add(target.Id);
                Logger.Note($"已挂钩 {target.TypeName}.{target.Member} -> {target.Action}（{target.Purpose}）");
            }
            catch (Exception ex)
            {
                Logger.Note($"挂钩 {target.TypeName}.{target.Member} 失败：{ex.GetType().Name}: {ex.Message}");
                Done.Add(target.Id);
            }
        }
    }

    /// <summary>按"动作 + 签名形状"选择匹配的 detour 方法。</summary>
    private static MethodInfo DetourMethod(UnlockTarget target)
    {
        var name = (target.Action, target.Shape) switch
        {
            (UnlockAction.ReturnTrue, DetourShape.BoolNoArgs) => nameof(Detours.TrueNoArgs),
            (UnlockAction.ReturnFalse, DetourShape.BoolNoArgs) => nameof(Detours.FalseNoArgs),
            (UnlockAction.ReturnTrue, DetourShape.BoolStringBool) => nameof(Detours.TrueStringBool),
            (UnlockAction.Skip, DetourShape.VoidString) => nameof(Detours.SkipString),
            _ => throw new NotSupportedException(
                $"没有与 {target.Action}/{target.Shape} 匹配的 detour 方法，请在 Detours 中补充。")
        };

        return typeof(Detours).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
               ?? throw new MissingMethodException("Detours." + name);
    }
}

/// <summary>
/// detour 方法：第一个参数是"原方法"委托，其余参数与原方法签名一致。
/// 新增目标时若签名形状不在 <see cref="DetourShape"/> 中，需要在这里补对应方法。
/// </summary>
public static class Detours
{
    /// <summary>bool 无参 → true</summary>
    public static bool TrueNoArgs(Func<bool> orig) => true;

    /// <summary>bool 无参 → false</summary>
    public static bool FalseNoArgs(Func<bool> orig) => false;

    /// <summary>bool (string, bool) → true</summary>
    public static bool TrueStringBool(Func<string, bool, bool> orig, string feature, bool showHint) => true;

    /// <summary>void (string) → 什么都不做</summary>
    public static void SkipString(Action<string> orig, string feature)
    {
    }
}
