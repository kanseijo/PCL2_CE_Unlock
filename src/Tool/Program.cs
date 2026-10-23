using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PclCeUnlock.Shared;

namespace PclCeUnlock.Tool;

public static class Program
{
    private static string? _launcherPath;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("================================================================");
        Console.WriteLine("  " + ToolInfo.Title);
        Console.WriteLine("  工具目录：" + DirectoryPolicy.ToolDirectory);
        Console.WriteLine("  启动器目录（上级）：" + DirectoryPolicy.LauncherDirectory);
        Console.WriteLine("  解锁目标：" + UnlockTargets.Patches.Count + " 个方法（正版劝导相关方法不在其中）");
        Console.WriteLine("================================================================");
        Console.WriteLine();

        // 1) 先尝试用上次保存的设置
        var loaded = Settings.Load();
        if (loaded.Tampered)
            Warn(loaded.Note ?? "设置文件校验失败。");
        else if (loaded.Note is not null)
            Info(loaded.Note);

        if (loaded.LauncherFileName is not null)
        {
            var verdict = DirectoryPolicy.ValidateSelection(
                DirectoryPolicy.CombineLauncherFile(loaded.LauncherFileName));
            if (verdict.Ok)
            {
                _launcherPath = verdict.FullPath;
                Console.WriteLine("已载入上次的启动器：" + Describe(_launcherPath!));
                Save(_launcherPath!);
            }
        }

        // 2) 没有可用设置 → 自动检测
        if (_launcherPath is null) AutoSelect();

        // 3) 命令行开关（便于快捷方式直接"注入启动"或"打补丁"）
        var cli = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : null;
        if (cli is "--inject" or "-i")
        {
            if (_launcherPath is null)
            {
                Console.WriteLine("命令行模式：当前没有可用的启动器选择，转入交互模式。");
            }
            else
            {
                DoInject(interactive: false);
                return 0;
            }
        }
        else if (cli is "--patch" or "-p")
        {
            if (_launcherPath is null)
            {
                Console.WriteLine("命令行模式：当前没有可用的启动器选择，转入交互模式。");
            }
            else
            {
                DoPatch(interactive: false);
                return 0;
            }
        }
        else if (cli is "--restore" or "-r")
        {
            if (_launcherPath is null) Console.WriteLine("命令行模式：当前没有可用的启动器选择，转入交互模式。");
            else { DoRestore(); return 0; }
        }
        else if (cli is "--shortcut")
        {
            DoShortcut();
            return 0;
        }
        else if (cli is "--status" or "-s")
        {
            ShowStatus();
            return 0;
        }
        else if (cli is "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }
        else if (cli is not null)
        {
            Console.WriteLine("未知参数：" + args[0]);
            PrintHelp();
            return 2;
        }

        // 4) 主循环
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("当前启动器：" + (_launcherPath is null ? "（未选择）" : Describe(_launcherPath)));
            Console.WriteLine();
            Console.WriteLine("  1) 重新自动检测");
            Console.WriteLine("  2) 手动选择启动器（打开文件对话框 / 拖入路径）");
            Console.WriteLine("  3) 打补丁（直接替换原启动器，自动备份）");
            Console.WriteLine("  4) 以注入方式启动（官方 exe 保持原样）");
            Console.WriteLine("  5) 从备份恢复原版启动器");
            Console.WriteLine("  6) 创建桌面快捷方式（" + Shortcut.Name + "）");
            Console.WriteLine("  7) 查看配置与状态");
            Console.WriteLine("  0) 退出");
            Console.Write("请选择：");

            var choice = Console.ReadLine()?.Trim();
            Console.WriteLine();
            switch (choice)
            {
                case "1":
                    AutoSelect(force: true);
                    break;
                case "2":
                    ManualSelect();
                    break;
                case "3":
                    DoPatch();
                    break;
                case "4":
                    DoInject();
                    break;
                case "5":
                    DoRestore();
                    break;
                case "6":
                    DoShortcut();
                    break;
                case "7":
                    ShowStatus();
                    break;
                case "0":
                case null:
                    return 0;
                default:
                    Warn("无效的选项。");
                    break;
            }
        }
    }

    // ---------------- 选择 ----------------

    private static void AutoSelect(bool force = false)
    {
        var toolExe = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
        var candidates = LauncherProbe.DetectIn(DirectoryPolicy.LauncherDirectory, toolExe);

        if (candidates.Count == 0)
        {
            // 常见误用：工具直接被放进了启动器目录（上级目录里没有启动器）
            var misplaced = LauncherProbe.DetectIn(DirectoryPolicy.ToolDirectory, toolExe).Count > 0;
            if (misplaced)
            {
                Warn("检测到启动器和工具在同一个目录里，这不符合本工具的布局要求。");
                Console.WriteLine("  请在本目录下新建一个子文件夹（如 解锁工具），把工具与 hook 一起移进去；");
                Console.WriteLine("  启动器留在上一级目录。");
            }
            else
            {
                Warn("在上级目录里没有自动检测到 PCL-CE 启动器。");
            }

            Console.WriteLine();
            Console.WriteLine(DirectoryPolicy.LayoutHint);
            Console.WriteLine("  然后把启动器放进上面的“启动器目录”，或选择 2) 手动选择。");
            return;
        }

        Console.WriteLine($"自动检测到 {candidates.Count} 个候选：");
        for (var i = 0; i < candidates.Count; i++)
            Console.WriteLine($"  [{i + 1}] {candidates[i]}");

        LauncherInfo picked;
        if (candidates.Count == 1 && !force)
        {
            picked = candidates[0];
            Console.WriteLine();
            Console.WriteLine("只找到一个候选，已自动选用。");
        }
        else
        {
            Console.Write("请选择序号（回车取消）：");
            var input = Console.ReadLine()?.Trim();
            if (!int.TryParse(input, out var index) || index < 1 || index > candidates.Count)
            {
                Info("已取消。");
                return;
            }

            picked = candidates[index - 1];
        }

        Accept(picked.Path);
    }

    private static void ManualSelect()
    {
        var picked = FilePicker.PickExe(DirectoryPolicy.LauncherDirectory,
            "选择 PCL-CE 启动器（必须位于本工具的上一级目录）");
        if (picked is null)
        {
            Info("已取消。");
            return;
        }

        Accept(picked);
    }

    /// <summary>统一的接受入口：必过同目录校验。</summary>
    private static void Accept(string candidate)
    {
        // 手动输入可能只给了文件名
        if (!Path.IsPathRooted(candidate))
            candidate = Path.Combine(DirectoryPolicy.LauncherDirectory, candidate);

        var verdict = DirectoryPolicy.ValidateSelection(candidate);
        if (!verdict.Ok)
        {
            Console.WriteLine();
            Console.WriteLine("【已拒绝】" + verdict.Message);
            Console.WriteLine();
            return;
        }

        _launcherPath = verdict.FullPath;
        Save(_launcherPath!);
        Console.WriteLine();
        Console.WriteLine("已选用：" + Describe(_launcherPath!));
        var info = LauncherProbe.Inspect(_launcherPath!);
        if (!info.OfficialHash)
            Warn("这个文件不是已知的 PCL-CE v2.15.1 官方发布包（哈希不匹配）。本工具只保证对 v2.15.1 的识别，" +
                 "其它版本可能因方法改名而失效。");
        if (info.LooksPatched)
            Warn("这个文件看起来已经打过本工具的补丁，再次打补丁不会重复生效。");
    }

    private static void Save(string path)
    {
        try
        {
            Settings.Save(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Warn("保存设置失败：" + ex.Message);
        }
    }

    // ---------------- 动作 ----------------

    private static void DoPatch(bool interactive = true)
    {
        if (!RequireLauncher()) return;
        var source = _launcherPath!;

        var (applied, total) = PatchEngine.CountApplied(source);
        if (applied == total)
        {
            Console.WriteLine($"当前文件已经打过补丁（{applied}/{total} 个目标均已改写），无需重复打。");
            Console.WriteLine("如需重打：先用菜单 5) 从原版备份还原，再执行本操作。");
            if (interactive) WaitKey();
            return;
        }

        if (applied > 0)
        {
            Warn($"检测到该文件已被改动（{applied}/{total} 个目标已是补丁状态）。");
            Console.WriteLine("  建议先用菜单 5) 从原版备份还原，再重新打补丁，避免状态混杂。");
            if (interactive) WaitKey();
            return;
        }

        Console.WriteLine($"目标启动器：{Path.GetFileName(source)}");
        Console.WriteLine("将直接替换该文件（文件名不变，桌面快捷方式照常可用），并预留一份备份。");
        Console.WriteLine();

        try
        {
            var backup = PatchInstaller.Apply(source, Console.Out);
            Console.WriteLine();
            Console.WriteLine("已完成：原文件已被补丁版替换。");
            Console.WriteLine("  原版备份：" + Path.GetFileName(backup) + "   ← 需要还原时用它");
            Console.WriteLine("  还原方法：本工具菜单 5)（或把上面这个「"
                              + PatchInstaller.OriginalBackupSuffix.TrimStart('.') + "」文件改名回原文件名）");
            Console.WriteLine("  提示：建议关闭启动器自动更新，否则上游新版会把它覆盖。");
        }
        catch (Exception ex)
        {
            Warn("打补丁失败：" + ex.Message);
        }

        if (interactive) WaitKey();
    }

    private static void DoRestore()
    {
        if (!RequireLauncher()) return;
        var target = _launcherPath!;

        try
        {
            var (originals, patched) = PatchInstaller.ListBackups(target);
            var backup = PatchInstaller.FindLatestOriginalBackup(target);
            if (backup is null)
            {
                Warn("没有找到该启动器的**原版**备份（查找 " +
                     Path.GetFileName(target) + "*" + PatchInstaller.OriginalBackupSuffix + "）。");
                if (patched.Length > 0)
                    Console.WriteLine("  目录里只有补丁版备份（" + patched.Length + " 个），补丁版备份不能用于还原。");
                return;
            }

            if (originals.Length > 1)
                Console.WriteLine($"目录里有 {originals.Length} 份原版备份，将使用最新的这一份。");
            Console.WriteLine("找到原版备份：" + Path.GetFileName(backup));
            Console.Write("确认用它替换回当前文件？(y/N)：");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is not ("y" or "yes"))
            {
                Info("已取消。");
                return;
            }

            PatchInstaller.Restore(target, Console.Out);
        }
        catch (Exception ex)
        {
            Warn("恢复失败：" + ex.Message);
        }

        WaitKey();
    }

    private static void DoShortcut()
    {
        if (!OperatingSystem.IsWindows())
        {
            Warn("桌面快捷方式功能仅在 Windows 上可用。");
            return;
        }

        try
        {
            var toolExe = Environment.ProcessPath ?? Path.Combine(DirectoryPolicy.ToolDirectory, "PclCeUnlock.exe");
            var lnk = Shortcut.CreateDesktopShortcut(toolExe, DirectoryPolicy.ToolDirectory, _launcherPath);
            Console.WriteLine("已创建桌面快捷方式：" + lnk);
            Console.WriteLine("  名称：" + Shortcut.Name + "（参数 --inject，图标取启动器图标）");
            Console.WriteLine("  以后双击它即可以注入方式启动，官方 exe 保持原样。");
        }
        catch (Exception ex)
        {
            Warn("创建快捷方式失败：" + ex.Message);
        }

        WaitKey();
    }

    private static void DoInject(bool interactive = true)
    {
        if (!RequireLauncher()) return;

        var hook = DirectoryPolicy.ValidateHook(ToolInfo.HookFileName);
        if (!hook.Ok)
        {
            Warn(hook.Message);
            return;
        }

        var (injected, injectedTotal) = PatchEngine.CountApplied(_launcherPath!);
        if (injected == injectedTotal)
            Warn("当前启动器已经是补丁版，注入模式不再需要（如需原版请用菜单 5 恢复；"
                 + "注入模式应配合官方原版使用）。");

        try
        {
            var psi = new ProcessStartInfo(_launcherPath!)
            {
                UseShellExecute = false,
                WorkingDirectory = DirectoryPolicy.ToolDirectory
            };
            psi.Environment["DOTNET_STARTUP_HOOKS"] = hook.FullPath!;

            var process = Process.Start(psi);
            Console.WriteLine("已以注入方式启动，进程 ID：" + (process?.Id.ToString() ?? "?"));
            Console.WriteLine("注入日志：同目录下的 PCL解锁注入日志.txt");
            if (interactive) WaitKey();
        }
        catch (Exception ex)
        {
            Warn("启动失败：" + ex.Message);
        }
    }

    private static void ShowStatus()
    {
        Console.WriteLine("工具目录：" + DirectoryPolicy.ToolDirectory);
        Console.WriteLine("启动器目录（上级）：" + DirectoryPolicy.LauncherDirectory);
        Console.WriteLine("配置文件：" + Settings.FilePath + (File.Exists(Settings.FilePath) ? "（存在）" : "（尚未创建）"));
        Console.WriteLine("启动器  ：" + (_launcherPath ?? "未选择"));
        Console.WriteLine();
        Console.WriteLine("解锁目标（补丁版与注入版共用同一份定义）：");
        foreach (var t in UnlockTargets.Patches)
            Console.WriteLine($"  · {t.TypeName}.{t.Member} → {t.Action}    {t.Purpose}");
        Console.WriteLine();
        Console.WriteLine("明确不改动（正版劝导相关）：");
        foreach (var (asm, type, member) in UnlockTargets.MustNotChange)
            Console.WriteLine($"  × {asm} :: {type}.{member}");

        if (_launcherPath is not null)
        {
            var (originals, patched) = PatchInstaller.ListBackups(_launcherPath);
            Console.WriteLine();
            Console.WriteLine("备份情况：");
            Console.WriteLine("  原版备份（可用于还原）：" + originals.Length + " 个");
            foreach (var f in originals.Take(3)) Console.WriteLine("    · " + Path.GetFileName(f));
            Console.WriteLine("  补丁版备份（仅留档）：" + patched.Length + " 个");
            foreach (var f in patched.Take(3)) Console.WriteLine("    · " + Path.GetFileName(f));
        }

        var hook = DirectoryPolicy.ValidateHook(ToolInfo.HookFileName);
        Console.WriteLine();
        Console.WriteLine("注入组件：" + (hook.Ok ? "就绪（" + hook.FullPath + "）" : "缺失"));
        if (!hook.Ok) Console.WriteLine("  " + hook.Message);
    }

    private static void WaitKey()
    {
        Console.WriteLine();
        Console.WriteLine("按任意键继续...");
        Console.ReadKey(true);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("用法：");
        Console.WriteLine("  PclCeUnlock              交互模式（推荐，可自动检测 / 手动选择 / 打补丁 / 注入启动）");
        Console.WriteLine("  PclCeUnlock --inject     用已记住的启动器以注入方式启动（官方 exe 保持原样）");
        Console.WriteLine("  PclCeUnlock --patch      打补丁：替换原启动器（自动备份，文件名不变）");
        Console.WriteLine("  PclCeUnlock --restore    从备份还原原版启动器");
        Console.WriteLine("  PclCeUnlock --shortcut   创建桌面快捷方式（" + Shortcut.Name + "）");
        Console.WriteLine("  PclCeUnlock --status     打印当前配置与解锁目标");
        Console.WriteLine();
        Console.WriteLine("说明：--inject / --patch 使用与交互模式完全相同的选择结果与同目录校验；");
        Console.WriteLine("      若还没有选择或选择已失效，会转入交互模式让你重新选择。");
    }

    private static bool RequireLauncher()
    {
        if (_launcherPath is null)
        {
            Warn("还没有选择启动器。请先用 1) 自动检测 或 2) 手动选择。");
            return false;
        }

        // 每次使用前重新校验（防止选择后被移动/替换/改成链接）
        var verdict = DirectoryPolicy.ValidateSelection(_launcherPath);
        if (!verdict.Ok)
        {
            Warn("当前启动器已不可用：" + verdict.Message);
            _launcherPath = null;
            Settings.Clear();
            return false;
        }

        _launcherPath = verdict.FullPath;
        return true;
    }

    // ---------------- 输出 ----------------

    private static string Describe(string path)
    {
        var info = LauncherProbe.Inspect(path);
        return info.ToString();
    }

    private static void Info(string message) => Console.WriteLine("[信息] " + message);

    private static void Warn(string message) => Console.WriteLine("[注意] " + message);
}
