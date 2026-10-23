using System;
using System.Runtime.InteropServices;

namespace PclCeUnlock.Tool;

/// <summary>
/// 启动器选择对话框。
/// Windows 下使用系统"打开文件"对话框；非 Windows 或对话框不可用时，回退为
/// "把文件拖进控制台 / 粘贴完整路径"。
/// </summary>
public static class FilePicker
{
    /// <summary>返回用户选择的路径；取消或失败返回 null。</summary>
    public static string? PickExe(string initialDirectory, string title)
    {
        if (OperatingSystem.IsWindows())
        {
            var picked = WindowsDialog.Pick(initialDirectory, title);
            if (picked is not null) return picked;
        }

        Console.WriteLine();
        Console.WriteLine("请把启动器 exe **拖拽到本窗口**（或粘贴完整路径）后回车；直接回车=取消：");
        Console.Write("> ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim().Trim('"');
        return input.Length == 0 ? null : input;
    }

    private static class WindowsDialog
    {
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_EXPLORER = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string? lpstrFilter;
            public string? lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string? lpstrFile;
            public int nMaxFile;
            public string? lpstrFileTitle;
            public int nMaxFileTitle;
            public string? lpstrInitialDir;
            public string? lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string? lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string? lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        public static string? Pick(string initialDirectory, string title)
        {
            const int bufferChars = 4096;
            var buffer = new string('\0', bufferChars);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                lpstrFilter = "启动器程序 (*.exe)\0*.exe\0所有文件 (*.*)\0*.*\0\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufferChars,
                lpstrInitialDir = initialDirectory,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER
            };

            if (!GetOpenFileNameW(ref ofn)) return null;
            var result = ofn.lpstrFile;
            if (string.IsNullOrWhiteSpace(result)) return null;
            var end = result.IndexOf('\0');
            return (end >= 0 ? result.Substring(0, end) : result).Trim();
        }
    }
}
