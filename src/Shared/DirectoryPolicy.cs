using System;
using System.IO;

namespace PclCeUnlock.Shared;

/// <summary>选择校验结果。</summary>
public sealed class PathVerdict
{
    private PathVerdict(bool ok, string? fullPath, string message)
    {
        Ok = ok;
        FullPath = fullPath;
        Message = message;
    }

    public bool Ok { get; }

    public string? FullPath { get; }

    public string Message { get; }

    public static PathVerdict Accept(string fullPath) => new(true, fullPath, string.Empty);

    public static PathVerdict Reject(string message) => new(false, null, message);
}

/// <summary>
/// 目录布局策略：<b>启动器必须位于本工具的「上一级目录」</b>。
/// <para>
/// 期望布局：
/// <code>
/// &lt;PCL 目录&gt;\
/// ├─ PCL2_CE_Release_x64.exe     ← 官方启动器（名字随意）
/// ├─ ... PCL 自己的文件 ...
/// └─ 解锁工具\                    ← 本工具与 hook 都放这里，不和 PCL 混在一起
///     ├─ PclCeUnlock.exe
///     └─ hook\PCLUnlockHook.dll
/// </code>
/// </para>
/// <para>
/// 防"改配置绕过"：配置里<b>只存文件名</b>；每次使用前都按"上级目录"重新校验；
/// 解析软/硬链接后目标仍必须在该上级目录内；配置只当记忆，从不作为可信路径来源。
/// </para>
/// </summary>
public static class DirectoryPolicy
{
    /// <summary>工具自身所在目录（绝不使用当前工作目录，避免被 cd 绕过）。</summary>
    public static string ToolDirectory
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory();
            return Normalize(dir);
        }
    }

    /// <summary>允许放置启动器的目录 = 工具的上一级目录。</summary>
    public static string LauncherDirectory
    {
        get
        {
            var parent = Path.GetDirectoryName(ToolDirectory);
            return string.IsNullOrWhiteSpace(parent) ? ToolDirectory : Normalize(parent);
        }
    }

    private static string Normalize(string dir) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    /// <summary>校验一个"文件名"是否合法：必须是裸文件名、.exe、无目录成分。</summary>
    public static bool IsBareExeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name!.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>把"上级目录"与文件名拼成完整路径（不做存在性校验）。</summary>
    public static string CombineLauncherFile(string fileName) => Path.Combine(LauncherDirectory, fileName);

    /// <summary>布局提示：工具被直接放进启动器目录时应给出的说明。</summary>
    public static string LayoutHint =>
        "期望的目录布局：" + Environment.NewLine +
        $"  {LauncherDirectory}{Path.DirectorySeparatorChar}" + Environment.NewLine +
        "    ├─ PCL2_CE_Release_x64.exe   ← 启动器（名字随意）" + Environment.NewLine +
        "    └─ 解锁工具" + Path.DirectorySeparatorChar + "          ← 本工具放在这个子文件夹里" + Environment.NewLine +
        $"  本工具当前位于：{ToolDirectory}";

    /// <summary>
    /// 校验用户选择（手动或自动）的启动器路径：只接受"工具上一级目录"下的 .exe。
    /// </summary>
    public static PathVerdict ValidateSelection(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return PathVerdict.Reject("没有选择任何文件。");

        string full;
        try
        {
            full = Normalize(selectedPath!);
        }
        catch (Exception ex)
        {
            return PathVerdict.Reject($"路径无效：{ex.Message}");
        }

        if (!File.Exists(full))
            return PathVerdict.Reject($"文件不存在：{full}");

        if (!string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
            return PathVerdict.Reject("只能选择 .exe 文件。");

        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        if (!string.Equals(Normalize(dir), LauncherDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // 常见误用：把工具直接放在了启动器目录里
            var sameAsTool = string.Equals(Normalize(dir), ToolDirectory, StringComparison.OrdinalIgnoreCase);
            var extra = sameAsTool
                ? Environment.NewLine +
                  "看起来你把工具**直接放进了启动器目录**。请新建一个子文件夹（例如 " +
                  Path.GetFileName(LauncherDirectory) + Path.DirectorySeparatorChar + "解锁工具" +
                  "）并把本工具与 hook 一起移动进去，启动器保持在上一级。"
                : Environment.NewLine + LayoutHint;

            return PathVerdict.Reject(
                "启动器必须位于本工具的**上一级目录**（这样工具才不会和 PCL 的文件混在一起）。" +
                Environment.NewLine +
                $"  期望目录：{LauncherDirectory}" + Environment.NewLine +
                $"  你选择的文件：{full}" + extra);
        }

        // 解析符号链接 / 目录联接：链接目标也必须落在上级目录内
        var resolved = ResolveLink(full);
        var resolvedDir = Path.GetDirectoryName(resolved) ?? string.Empty;
        if (!string.Equals(Normalize(resolvedDir), LauncherDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return PathVerdict.Reject(
                "该文件是指向允许目录之外的链接，已拒绝。" + Environment.NewLine +
                $"  链接目标：{resolved}");
        }

        if (!LauncherProbe.IsLauncher(resolved))
            return PathVerdict.Reject("该文件不像 PCL-CE 启动器（未能识别到包内程序集）。请确认选择的是官方启动器本体。");

        return PathVerdict.Accept(resolved);
    }

    /// <summary>解析最末级软/硬链接（失败时返回原路径）。</summary>
    public static string ResolveLink(string path)
    {
        try
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return target is null ? Normalize(path) : Normalize(target.FullName);
        }
        catch
        {
            return Normalize(path);
        }
    }

    /// <summary>校验 hook 目录（工具的组成部分）是否就绪。</summary>
    public static PathVerdict ValidateHook(string hookFileName)
    {
        var hookPath = Path.Combine(ToolDirectory, "hook", hookFileName);
        if (!File.Exists(hookPath))
            return PathVerdict.Reject(
                "找不到注入组件：hook" + Path.DirectorySeparatorChar + hookFileName + Environment.NewLine +
                "  hook 文件夹应位于本工具所在目录下（随包提供，勿单独移动）。");
        return PathVerdict.Accept(Path.GetFullPath(hookPath));
    }
}
