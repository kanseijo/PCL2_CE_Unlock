using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PclCeUnlock.Shared;

/// <summary>启动器探测结果。</summary>
public sealed class LauncherInfo
{
    public LauncherInfo(string path, long size, string sha256, string? version, bool officialHash, bool looksPatched)
    {
        Path = path;
        Size = size;
        Sha256 = sha256;
        Version = version;
        OfficialHash = officialHash;
        LooksPatched = looksPatched;
    }

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public long Size { get; }
    public string Sha256 { get; }

    /// <summary>从包内 metadata.json 读到的版本号（读不到为 null）。</summary>
    public string? Version { get; }

    /// <summary>是否命中已知官方发布包哈希（v2.15.1）。</summary>
    public bool OfficialHash { get; }

    /// <summary>是否已经是本工具的补丁版（关键方法 IL 已被改写）。</summary>
    public bool LooksPatched { get; }

    public override string ToString() =>
        $"{FileName}  ({(Version is null ? "版本未知" : "v" + Version)}, {Size / 1024 / 1024} MB" +
        $"{(OfficialHash ? ", 官方哈希匹配" : "")}{(LooksPatched ? ", 已打过补丁" : "")})";
}

/// <summary>
/// 启动器识别：<b>按内容识别，不依赖文件名</b>（很多人会改名，甚至改成中文名）。
/// </summary>
public static class LauncherProbe
{
    /// <summary>v2.15.1 官方发布包哈希（x64 / ARM64）。</summary>
    private static readonly string[] KnownOfficialSha256 =
    {
        "168bd8369d5ed4e5478942677562913330ef1d30a5193870af910bd18198050c",
        "e4f6a027b85c06730a038aa3fe9656b4dd63a026f181856d6c190926761606da"
    };

    private static readonly Regex VersionRegex = new(
        "\"base\"\\s*:\\s*\"(?<v>[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>判定是否为 PCL-CE 单文件包（PE + 包内清单里的程序集名标记）。</summary>
    public static bool IsLauncher(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var fi = new FileInfo(path);
            if (fi.Length < 64 * 1024) return false; // 单文件包本体至少这个量级

            using var fs = File.OpenRead(path);
            var head = new byte[4096];
            var read = fs.Read(head, 0, head.Length);
            if (read < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z') return false;
            if (!ContainsPeSignature(fs, head, read)) return false;

            // 包内清单/依赖里必然出现 "Plain Craft Launcher 2" 字样（与文件名无关）
            return ContainsAscii(path, "Plain Craft Launcher 2");
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsPeSignature(FileStream fs, byte[] head, int headLen)
    {
        try
        {
            fs.Position = 0x3C;
            var b = new byte[4];
            if (fs.Read(b, 0, 4) != 4) return false;
            var peOffset = BitConverter.ToInt32(b, 0);
            if (peOffset <= 0 || peOffset > fs.Length - 4) return false;
            fs.Position = peOffset;
            var sig = new byte[4];
            return fs.Read(sig, 0, 4) == 4 && sig[0] == (byte)'P' && sig[1] == (byte)'E'
                                             && sig[2] == 0 && sig[3] == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAscii(string path, string needle)
    {
        var pattern = Encoding.ASCII.GetBytes(needle);
        using var fs = File.OpenRead(path);
        var buffer = new byte[4 * 1024 * 1024];
        var carry = Array.Empty<byte>();
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            var chunk = new byte[carry.Length + read];
            Buffer.BlockCopy(carry, 0, chunk, 0, carry.Length);
            Buffer.BlockCopy(buffer, 0, chunk, carry.Length, read);
            if (IndexOf(chunk, pattern) >= 0) return true;

            var keep = Math.Min(pattern.Length - 1, chunk.Length);
            carry = new byte[keep];
            Buffer.BlockCopy(chunk, chunk.Length - keep, carry, 0, keep);
        }

        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var j = 0;
            for (; j < needle.Length; j++)
                if (haystack[i + j] != needle[j])
                    break;
            if (j == needle.Length) return i;
        }

        return -1;
    }

    public static LauncherInfo Inspect(string path)
    {
        var size = new FileInfo(path).Length;
        var sha = ComputeSha256(path);
        string? version = null;
        try
        {
            // metadata.json 内嵌在包内的主程序集资源里（位置靠后），必须整体扫描
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            var m = VersionRegex.Match(text);
            if (m.Success) version = m.Groups["v"].Value;
        }
        catch
        {
            // ignore
        }

        var official = KnownOfficialSha256.Any(x => string.Equals(x, sha, StringComparison.OrdinalIgnoreCase));
        var patched = LooksPatched(path);
        return new LauncherInfo(path, size, sha, version, official, patched);
    }

    /// <summary>粗略判断是否已经打过本工具的补丁（用于提示，不作为安全判定）。</summary>
    public static bool LooksPatched(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            // 补丁后的 CanCreateOtherProfile 方法体头为 tiny(0x9a=(38<<2)|2) + 17 2A 00...
            var pattern = new byte[] { 0x9A, 0x17, 0x2A, 0x00, 0x00 };
            return IndexOf(bytes, pattern) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>在指定目录里按内容自动探测启动器候选。</summary>
    public static System.Collections.Generic.List<LauncherInfo> DetectIn(string directory, string? excludeFileName = null)
    {
        var result = new System.Collections.Generic.List<LauncherInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
        {
            if (excludeFileName is not null &&
                string.Equals(Path.GetFileName(file), excludeFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsLauncher(file)) continue;
            result.Add(Inspect(file));
        }

        return result.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
