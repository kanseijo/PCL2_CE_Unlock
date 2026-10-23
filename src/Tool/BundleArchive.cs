using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PclCeUnlock.Tool;

/// <summary>单文件包内的一个条目。</summary>
public sealed class BundleEntry
{
    public string Name { get; init; } = string.Empty;
    public long Offset { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public byte Type { get; init; }

    public bool IsCompressed => CompressedSize != 0;
}

/// <summary>.NET 单文件包（host bundle）解析器。</summary>
public sealed class BundleArchive
{
    public const string RequiredKind = "bundle";

    private BundleArchive(int headerOffset, int major, int minor, string bundleId,
        IReadOnlyList<BundleEntry> entries, byte[] data)
    {
        HeaderOffset = headerOffset;
        Major = major;
        Minor = minor;
        BundleId = bundleId;
        Entries = entries;
        Data = data;
    }

    public int HeaderOffset { get; }
    public int Major { get; }
    public int Minor { get; }
    public string BundleId { get; }
    public IReadOnlyList<BundleEntry> Entries { get; }
    public byte[] Data { get; }

    public BundleEntry? Find(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public static BundleArchive Parse(byte[] data)
    {
        var start = LocateHeader(data);
        if (start < 0) throw new InvalidDataException("未能定位单文件包头部（可能不是 .NET 单文件发布程序）。");

        var p = start;
        var major = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
        var minor = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 8));
        p += 12;

        var idLen = data[p++];
        var bundleId = Encoding.UTF8.GetString(data, p, idLen);
        p += idLen;
        p += 40; // deps 偏移/大小、runtimeconfig 偏移/大小、保留字段

        var entries = new List<BundleEntry>(count);
        for (var i = 0; i < count; i++)
        {
            if (p + 25 > data.Length) throw new InvalidDataException("包内条目越界。");
            var offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p));
            var size = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p + 8));
            var csize = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p + 16));
            var type = data[p + 24];
            p += 25;
            var nameLen = data[p++];
            if (nameLen == 0 || p + nameLen > data.Length) throw new InvalidDataException("包内条目名非法。");
            var name = Encoding.UTF8.GetString(data, p, nameLen);
            p += nameLen;

            if (offset <= 0 || size <= 0 || offset + Math.Max(size, csize) > data.Length)
                throw new InvalidDataException($"包内条目 {name} 的偏移/大小越界。");

            entries.Add(new BundleEntry
            {
                Name = name, Offset = offset, Size = size, CompressedSize = csize, Type = type
            });
        }

        return new BundleArchive(start, major, minor, bundleId, entries, data);
    }

    private static int LocateHeader(byte[] data)
    {
        var from = Math.Max(0, data.Length - 8192);
        for (var start = from; start < data.Length - 16; start++)
        {
            if (!LooksLikeHeader(data, start)) continue;
            try
            {
                Validate(data, start);
                return start;
            }
            catch
            {
                // 继续找
            }
        }

        return -1;
    }

    private static bool LooksLikeHeader(byte[] data, int start)
    {
        if (start + 12 > data.Length) return false;
        var major = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start));
        var minor = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 4));
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 8));
        return major is >= 1 and <= 10 && minor is >= 0 and <= 10 && count is > 0 and < 5000;
    }

    private static void Validate(byte[] data, int start)
    {
        var p = start + 12;
        var idLen = data[p++];
        if (idLen is 0 or > 200) throw new InvalidDataException();
        p += idLen + 40;

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 8));
        if (count <= 0 || count > 5000) throw new InvalidDataException();

        // 注意：清单里的条目顺序是任意的，不能按清单顺序做连续性校验，必须先收集再按偏移排序
        var spans = new List<(long Offset, long End)>(count);
        for (var i = 0; i < count; i++)
        {
            if (p + 25 > data.Length) throw new InvalidDataException();
            var offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p));
            var size = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p + 8));
            var csize = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(p + 16));
            p += 25;
            var nameLen = data[p++];
            if (nameLen == 0) throw new InvalidDataException();
            if (p + nameLen > data.Length) throw new InvalidDataException();
            p += nameLen;

            var end = offset + Math.Max(size, csize);
            if (offset <= 0 || size <= 0 || end > data.Length) throw new InvalidDataException();
            spans.Add((offset, end));
        }

        spans.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        var prevEnd = 0L;
        foreach (var span in spans)
        {
            if (span.Offset < prevEnd) throw new InvalidDataException();
            prevEnd = span.End;
        }
    }

    /// <summary>取出某个条目的原始字节（不处理压缩，压缩条目直接报错）。</summary>
    public byte[] Extract(BundleEntry entry)
    {
        if (entry.IsCompressed)
            throw new InvalidDataException($"条目 {entry.Name} 在包内是压缩存储，本工具暂不支持。");
        var buffer = new byte[entry.Size];
        Buffer.BlockCopy(Data, (int)entry.Offset, buffer, 0, (int)entry.Size);
        return buffer;
    }
}
