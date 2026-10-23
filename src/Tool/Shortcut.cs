using System;
using System.IO;
using System.Reflection;

namespace PclCeUnlock.Tool;

/// <summary>创建桌面快捷方式（Windows 专用，走 WScript.Shell，无需 P/Invoke）。</summary>
public static class Shortcut
{
    /// <summary>快捷方式显示名称。</summary>
    public const string Name = "注入启动PCL2_CE";

    /// <summary>
    /// 在桌面创建 “注入启动PCL2_CE.lnk”，指向本工具并带 <c>--inject</c> 参数；
    /// 图标优先取启动器 exe 的图标。返回 .lnk 的完整路径。
    /// </summary>
    public static string CreateDesktopShortcut(string toolExePath, string toolDirectory, string? launcherPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("桌面快捷方式功能仅在 Windows 上可用。");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            throw new InvalidOperationException("无法定位桌面目录。");

        var lnkPath = Path.Combine(desktop, Name + ".lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("系统未提供 WScript.Shell，无法创建快捷方式。");
        var shell = Activator.CreateInstance(shellType)!;

        var createShortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
            new object[] { lnkPath })!;
        var shortcutType = createShortcut.GetType();

        void Set(string name, string value) =>
            shortcutType.InvokeMember(name, BindingFlags.SetProperty, null, createShortcut, new object[] { value });

        Set("TargetPath", toolExePath);
        Set("Arguments", "--inject");
        Set("WorkingDirectory", toolDirectory);
        Set("Description", "以内存注入方式启动 PCL-CE（官方文件保持原样）");
        if (!string.IsNullOrWhiteSpace(launcherPath) && File.Exists(launcherPath))
            Set("IconLocation", launcherPath + ",0");
        else
            Set("IconLocation", toolExePath + ",0");

        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, createShortcut, Array.Empty<object>());
        return lnkPath;
    }

    /// <summary>查找桌面上的本工具快捷方式（用于提示/覆盖）。</summary>
    public static string DesktopShortcutPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrWhiteSpace(desktop) ? string.Empty : Path.Combine(desktop, Name + ".lnk");
    }
}
