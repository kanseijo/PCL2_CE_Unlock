using System;
using System.Collections.Generic;

namespace PclCeUnlock.Shared;

/// <summary>目标方法的签名形状（注入版据此选择匹配的 detour 方法）。</summary>
public enum DetourShape
{
    /// <summary>bool 无参（属性取值器）。</summary>
    BoolNoArgs,

    /// <summary>bool (string, bool) —— 即 RestrictedFeaturePolicy.Check。</summary>
    BoolStringBool,

    /// <summary>void (string) —— 即 RestrictedFeaturePolicy.ThrowIfRestricted。</summary>
    VoidString
}

/// <summary>补丁动作。</summary>
public enum UnlockAction
{
    /// <summary>返回 true（ldc.i4.1; ret）。</summary>
    ReturnTrue,

    /// <summary>返回 false（ldc.i4.0; ret）。</summary>
    ReturnFalse,

    /// <summary>直接返回，不做任何事（ret）。</summary>
    Skip
}

/// <summary>
/// 一个解锁目标。
/// <paramref name="Assembly"/> 是目标程序集名（不是文件名），<paramref name="TypeName"/> 是全名。
/// </summary>
public sealed class UnlockTarget
{
    public UnlockTarget(string id, string assembly, string typeName, string member, bool isProperty,
        DetourShape shape, UnlockAction action, string purpose)
    {
        Shape = shape;
        Id = id;
        Assembly = assembly;
        TypeName = typeName;
        Member = member;
        IsProperty = isProperty;
        Action = action;
        Purpose = purpose;
    }

    /// <summary>稳定的短标识（用于日志与配置，不含版本）。</summary>
    public string Id { get; }

    public string Assembly { get; }

    public string TypeName { get; }

    public string Member { get; }

    /// <summary>true 表示目标是属性取值器（get_Xxx）。</summary>
    public bool IsProperty { get; }

    /// <summary>方法签名形状。</summary>
    public DetourShape Shape { get; }

    public UnlockAction Action { get; }

    /// <summary>人类可读的用途说明。</summary>
    public string Purpose { get; }

    /// <summary>用于原地改写的方法体 IL：动作指令 + 其后用 nop 填充到原长度。</summary>
    public byte[] BuildIl() => Action switch
    {
        UnlockAction.ReturnTrue => new byte[] { 0x17, 0x2A }, // ldc.i4.1; ret
        UnlockAction.ReturnFalse => new byte[] { 0x16, 0x2A }, // ldc.i4.0; ret
        UnlockAction.Skip => new byte[] { 0x2A }, // ret
        _ => throw new ArgumentOutOfRangeException()
    };

    public override string ToString() => $"{TypeName}.{Member} -> {Action}";
}

/// <summary>
/// 解锁目标的唯一定义（补丁版与注入版都由这里生成，避免两边漂移）。
/// 规格：只解除 ①地区限制 ②大陆环境判定 ③首次建档限制；<b>不动正版劝导</b>。
/// </summary>
public static class UnlockTargets
{
    /// <summary>规格版本；改动目标表时必须同步递增。</summary>
    public const string SpecVersion = "1.0";

    private const string LauncherAsm = "Plain Craft Launcher 2";
    private const string CoreAsm = "PCL.Core";

    /// <summary>需要解开的 5 个方法。</summary>
    public static IReadOnlyList<UnlockTarget> Patches { get; } = new[]
    {
        // ① 解除地区限制：下载 / 补全 / 修复 / 重置 / 整合包安装
        new UnlockTarget("region.isRestricted", LauncherAsm, "PCL.RestrictedFeaturePolicy",
            "IsRestricted", true, DetourShape.BoolNoArgs, UnlockAction.ReturnFalse, "地区限制判定恒为“不受限”"),
        new UnlockTarget("region.check", LauncherAsm, "PCL.RestrictedFeaturePolicy",
            "Check", false, DetourShape.BoolStringBool, UnlockAction.ReturnTrue, "功能检查恒放行（解锁 MC 下载等 14 处调用点）"),
        new UnlockTarget("region.throw", LauncherAsm, "PCL.RestrictedFeaturePolicy",
            "ThrowIfRestricted", false, DetourShape.VoidString, UnlockAction.Skip, "不再抛出取消异常"),

        // ② 解除“大陆环境”判定：任何环境都走可跳过的正版劝导，而不是硬拦截
        new UnlockTarget("locale.unrestricted", CoreAsm, "PCL.Core.App.Localization.Lang",
            "IsFeaturesUnrestricted", true, DetourShape.BoolNoArgs, UnlockAction.ReturnTrue, "解除大陆环境判定（劝导改为可跳过）"),

        // ③ 首次启动即可选择第三方 / 离线登录
        new UnlockTarget("profile.createOther", LauncherAsm, "PCL.ProfileUi",
            "CanCreateOtherProfile", false, DetourShape.BoolNoArgs, UnlockAction.ReturnTrue, "首次启动即可创建离线 / 第三方档案"),
    };

    /// <summary>
    /// 明确<b>不允许</b>改动的成员（正版劝导相关）。补丁/注入完成后必须校验它们逐字节未变。
    /// </summary>
    public static IReadOnlyList<(string Assembly, string TypeName, string Member)> MustNotChange { get; } =
        new[]
        {
            (LauncherAsm, "PCL.ModLaunch", "McLaunchPrecheck"),
            (CoreAsm, "PCL.Core.Minecraft.Profile.ProfileService", "get_HasMicrosoftProfile"),
        };

    /// <summary>补丁版会涉及的内部文件（单文件包内的程序集）。</summary>
    public static IReadOnlyList<string> RequiredAssemblies { get; } =
        new[] { LauncherAsm + ".dll", CoreAsm + ".dll" };
}
