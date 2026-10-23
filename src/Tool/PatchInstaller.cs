using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PclCeUnlock.Shared;

namespace PclCeUnlock.Tool;

/// <summary>
/// 打补丁的"就地替换"安装器：直接替换原启动器文件（保持文件名不变，桌面快捷方式照常可用），
/// 并在同目录预留备份以便随时还原。
/// </summary>
public static class PatchInstaller
{
    public const string TempSuffix = ".pclce-new";

    /// <summary>原版（官方）备份的后缀。</summary>
    public const string OriginalBackupSuffix = ".原版.bak";

    /// <summary>补丁版备份的后缀（还原时把当前补丁版留档用）。</summary>
    public const string PatchedBackupSuffix = ".补丁版.bak";

    /// <summary>最近一次成功替换时产生的备份路径。</summary>
    public static string? LastBackupPath { get; private set; }

    /// <summary>
    /// 打补丁并替换原启动器。返回备份文件路径。
    /// 流程：生成到临时文件 → 自校验 → 原文件改名为备份 → 临时文件改名为原文件名（失败则回滚）。
    /// </summary>
    public static string Apply(string launcherPath, TextWriter log)
    {
        var dir = Path.GetDirectoryName(launcherPath)!;
        var fileName = Path.GetFileName(launcherPath);
        var temp = Path.Combine(dir, fileName + TempSuffix);
        var backup = Path.Combine(dir,
            fileName + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + OriginalBackupSuffix);

        if (File.Exists(temp))
            File.Delete(temp);

        try
        {
            // ① 先写到临时文件（含自校验，失败会抛异常，绝不覆盖原文件）
            PatchEngine.Run(launcherPath, temp, log);

            // ② 原文件 → 备份
            try
            {
                File.Move(launcherPath, backup);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    "无法重命名原启动器（可能正在运行）。请先完全退出 PCL-CE 再试。", ex);
            }

            // ③ 临时文件 → 原文件名
            try
            {
                File.Move(temp, launcherPath);
            }
            catch (Exception ex)
            {
                // 回滚
                try
                {
                    if (File.Exists(backup) && !File.Exists(launcherPath))
                        File.Move(backup, launcherPath);
                }
                catch
                {
                    // ignore
                }

                throw new IOException("替换原启动器失败，已尝试回滚：" + ex.Message, ex);
            }

            LastBackupPath = backup;
            return backup;
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>找到某个启动器最近的一份“原版”备份（补丁版备份不会被选中）。</summary>
    public static string? FindLatestOriginalBackup(string launcherPath)
    {
        var dir = Path.GetDirectoryName(launcherPath)!;
        var fileName = Path.GetFileName(launcherPath);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, fileName + "*" + OriginalBackupSuffix)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
    }

    /// <summary>列出该启动器全部的「原版备份」与「补丁版备份」。</summary>
    public static (string[] Original, string[] Patched) ListBackups(string launcherPath)
    {
        var dir = Path.GetDirectoryName(launcherPath)!;
        var fileName = Path.GetFileName(launcherPath);
        if (!Directory.Exists(dir)) return (Array.Empty<string>(), Array.Empty<string>());
        var byTime = (IEnumerable<string> files) => files
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ToArray();
        return (byTime(Directory.EnumerateFiles(dir, fileName + "*" + OriginalBackupSuffix)),
                byTime(Directory.EnumerateFiles(dir, fileName + "*" + PatchedBackupSuffix)));
    }

    /// <summary>
    /// 从备份还原原启动器。返回所用备份路径。
    /// 当前（已打补丁的）文件会被改名为 <c>*.replaced.bak</c> 保留，可再次恢复。
    /// </summary>
    public static string Restore(string launcherPath, TextWriter log)
    {
        var backup = FindLatestOriginalBackup(launcherPath)
                     ?? throw new InvalidOperationException(
                         "没有找到**原版**备份（" + Path.GetFileName(launcherPath) + "*" + OriginalBackupSuffix +
                         "）。补丁版备份不能用于还原。");
        var dir = Path.GetDirectoryName(launcherPath)!;
        var replaced = Path.Combine(dir,
            Path.GetFileName(launcherPath) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + PatchedBackupSuffix);

        var (applied, total) = PatchEngine.CountApplied(backup);
        if (applied == total)
            throw new InvalidOperationException(
                "选中的备份'" + Path.GetFileName(backup) + "其实是补丁版，不能用来还原。");

        log.WriteLine("将从原版备份还原：" + Path.GetFileName(backup));
        try
        {
            File.Move(launcherPath, replaced);
        }
        catch (IOException ex)
        {
            throw new IOException("无法重命名当前启动器（可能正在运行）。请先完全退出 PCL-CE 再试。", ex);
        }

        try
        {
            File.Move(backup, launcherPath);
        }
        catch (Exception ex)
        {
            try
            {
                File.Move(replaced, launcherPath);
            }
            catch
            {
                // ignore
            }

            throw new IOException("还原失败，已尝试回滚：" + ex.Message, ex);
        }

        log.WriteLine("已还原：" + Path.GetFileName(launcherPath) + "（原版已回到原位）");
        log.WriteLine("  原版备份已消耗：" + Path.GetFileName(backup));
        log.WriteLine("  被换下的补丁版留存为：" + Path.GetFileName(replaced) + "（补丁版，不能用来还原）");
        return backup;
    }
}
