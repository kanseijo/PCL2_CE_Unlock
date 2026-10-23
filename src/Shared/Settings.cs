using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PclCeUnlock.Shared;

/// <summary>设置文件的磁盘表示。</summary>
internal sealed class SettingsFile
{
    [JsonPropertyName("launcherFileName")] public string LauncherFileName { get; set; } = string.Empty;
    [JsonPropertyName("check")] public string Check { get; set; } = string.Empty;
    [JsonPropertyName("savedAtUtc")] public string SavedAtUtc { get; set; } = string.Empty;
    [JsonPropertyName("toolVersion")] public string ToolVersion { get; set; } = string.Empty;
    [JsonPropertyName("specVersion")] public string SpecVersion { get; set; } = string.Empty;
}

/// <summary>加载结果。</summary>
public sealed class SettingsLoadResult
{
    public SettingsLoadResult(string? launcherFileName, bool tampered, string? note)
    {
        LauncherFileName = launcherFileName;
        Tampered = tampered;
        Note = note;
    }

    /// <summary>可用的启动器文件名（已通过同目录与完整性校验）；null 表示需要重新选择。</summary>
    public string? LauncherFileName { get; }

    /// <summary>是否检测到配置文件被手工改动。</summary>
    public bool Tampered { get; }

    public string? Note { get; }
}

/// <summary>
/// 与程序同目录的设置文件：<c>pcl-ce-unlock.settings.json</c>。
/// <para>
/// 只保存<b>文件名</b>（不保存路径），并附一个校验码用于发现手工篡改。
/// 校验码只能"发现"篡改（密钥就在程序里），真正的安全保证来自
/// <see cref="DirectoryPolicy.ValidateSelection"/> 的每次使用前重新校验。
/// </para>
/// </summary>
public static class Settings
{
    public const string FileName = "pcl-ce-unlock.settings.json";

    private const string Salt = "PclCeUnlock.Settings.v1";

    public static string FilePath => Path.Combine(DirectoryPolicy.ToolDirectory, FileName);

    public static string ComputeCheck(string launcherFileName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + "|" + launcherFileName.ToLowerInvariant())))
            .ToLowerInvariant();

    public static SettingsLoadResult Load()
    {
        var path = FilePath;
        if (!File.Exists(path))
            return new SettingsLoadResult(null, false, null);

        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            return new SettingsLoadResult(null, true, $"设置文件无法解析，已忽略（{ex.GetType().Name}）。");
        }

        if (file is null || string.IsNullOrWhiteSpace(file.LauncherFileName))
            return new SettingsLoadResult(null, false, null);

        // ① 名称必须是裸文件名，任何目录成分都直接判为非法
        if (!DirectoryPolicy.IsBareExeFileName(file.LauncherFileName))
            return new SettingsLoadResult(null, true,
                "设置文件里的启动器名称不合法（包含目录/父目录等成分），已忽略并要求重新选择。");

        // ② 校验码不符 → 视为被手工改动
        if (!string.Equals(file.Check, ComputeCheck(file.LauncherFileName), StringComparison.OrdinalIgnoreCase))
            return new SettingsLoadResult(null, true, "设置文件校验码不匹配（疑似被手工修改），已忽略并要求重新选择。");

        // ③ 仍然要用实际文件系统再校验一次（文件可能已被移动/删除/替换成链接）
        var verdict = DirectoryPolicy.ValidateSelection(DirectoryPolicy.CombineLauncherFile(file.LauncherFileName));
        if (!verdict.Ok)
            return new SettingsLoadResult(null, false, $"上次选择的启动器已不可用：{verdict.Message}");

        return new SettingsLoadResult(file.LauncherFileName, false, null);
    }

    public static bool Save(string launcherFileName)
    {
        if (!DirectoryPolicy.IsBareExeFileName(launcherFileName))
            throw new ArgumentException("只允许保存裸文件名（不含目录成分）。", nameof(launcherFileName));

        var file = new SettingsFile
        {
            LauncherFileName = launcherFileName,
            Check = ComputeCheck(launcherFileName),
            SavedAtUtc = DateTime.UtcNow.ToString("O"),
            ToolVersion = ToolInfo.Version,
            SpecVersion = UnlockTargets.SpecVersion
        };

        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            new UTF8Encoding(false));
        return true;
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // ignore
        }
    }
}
