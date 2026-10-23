using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using dnlib.DotNet;
using PclCeUnlock.Shared;

namespace PclCeUnlock.Tool;

/// <summary>单个方法的处理结果。</summary>
public sealed class PatchOutcome
{
    public string TargetId { get; init; } = string.Empty;
    public string Member { get; init; } = string.Empty;
    public long CodeOffset { get; init; }
    public int CodeSize { get; init; }
    public string BodyKind { get; init; } = string.Empty;
    public byte[] OldIl { get; init; } = Array.Empty<byte>();
    public byte[] NewIl { get; init; } = Array.Empty<byte>();
}

/// <summary>补丁版生成器：直接在官方单文件包内部原地改写 5 个方法的 IL。</summary>
public static class PatchEngine
{
    public static void Run(string sourcePath, string outputPath, TextWriter log)
    {
        var data = File.ReadAllBytes(sourcePath);
        var bundle = BundleArchive.Parse(data);
        log.WriteLine($"单文件包：v{bundle.Major}.{bundle.Minor}，内部文件 {bundle.Entries.Count} 个");
        if (bundle.Entries.Any(e => e.IsCompressed))
            throw new InvalidDataException("包内存在压缩条目，本工具暂不支持。");

        var outcomes = new List<PatchOutcome>();
        var untouchedBefore = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var asmName in UnlockTargets.RequiredAssemblies)
        {
            var entry = bundle.Find(asmName) ?? throw new InvalidDataException($"包内找不到 {asmName}。");
            var asmBytes = bundle.Extract(entry);

            var temp = Path.Combine(Path.GetTempPath(), "pclce-unlock-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllBytes(temp, asmBytes);
                using var module = ModuleDefMD.Load(temp);

                var r2r = ReadManagedNativeHeaderRva(asmBytes);
                if (r2r != 0)
                    throw new InvalidDataException(
                        $"{asmName} 已做 ReadyToRun 预编译（ManagedNativeHeader 非 0），改 IL 不会生效，无法安全打补丁。");
                if (module.IsStrongNameSigned)
                    log.WriteLine($"警告：{asmName} 带强名称签名，改写 IL 会使签名失效（PCL-CE 正常情况下不会出现）。");

                // 记录“不得改动”的方法原始字节
                foreach (var (mustAsm, typeName, member) in UnlockTargets.MustNotChange)
                {
                    if (!string.Equals(mustAsm + ".dll", asmName, StringComparison.OrdinalIgnoreCase)) continue;
                    var body = FindBody(module, asmBytes, typeName, member);
                    if (body is not null) untouchedBefore[typeName + "." + member] = body.Bytes;
                }

                foreach (var target in UnlockTargets.Patches.Where(t =>
                             string.Equals(t.Assembly + ".dll", asmName, StringComparison.OrdinalIgnoreCase)))
                {
                    var found = FindBody(module, asmBytes, target.TypeName,
                        target.IsProperty ? "get_" + target.Member : target.Member);
                    if (found is null)
                        throw new InvalidDataException(
                            $"找不到方法 {target.TypeName}.{target.Member}（上游可能改名）。");
                    var b = found;
                    if (b.HasExceptionHandlers)
                        throw new InvalidDataException($"{target.Member} 带异常处理段，无法原地改写。");

                    var il = target.BuildIl();
                    if (il.Length > b.CodeSize)
                        throw new InvalidDataException($"{target.Member} 的方法体太短，放不下新 IL。");

                    // 与后继方法体不重叠才能安全原地改写
                    var nextRva = module.GetTypes()
                        .SelectMany(t => t.Methods)
                        .Where(m => (uint)m.RVA > b.Rva)
                        .Select(m => (long)m.RVA)
                        .DefaultIfEmpty(asmBytes.Length)
                        .Min();
                    if (b.Rva + b.HeaderSize + b.CodeSize > nextRva)
                        throw new InvalidDataException($"{target.Member} 的方法体与后继方法重叠，拒绝改写。");

                    // 关键：方法体必须收缩到 IL 的“精确长度”，并把原体积的剩余字节清零。
                    // 若保留原长度、用 nop 填充（尤其是 fat 方法体），JIT 会判 InvalidProgramException。
                    var bodyOffset = (int)(entry.Offset + b.FileOffset);
                    var oldIl = new byte[b.CodeSize];
                    Buffer.BlockCopy(b.FileCode, 0, oldIl, 0, b.CodeSize);

                    int codeOffset;
                    if (b.Kind == "tiny")
                    {
                        data[bodyOffset] = (byte)((il.Length << 2) | 0x02); // 小头：低 2 位=0b10，其余=代码长度
                        codeOffset = bodyOffset + 1;
                    }
                    else
                    {
                        // 大头：保留头部（MaxStack / LocalVarSigTok 不动），只把 CodeSize 改成精确长度
                        data[bodyOffset + 4] = (byte)il.Length;
                        data[bodyOffset + 5] = 0x00;
                        data[bodyOffset + 6] = 0x00;
                        data[bodyOffset + 7] = 0x00;
                        codeOffset = bodyOffset + b.HeaderSize;
                    }

                    for (var i = 0; i < il.Length; i++) data[codeOffset + i] = il[i];
                    for (var i = codeOffset + il.Length; i < bodyOffset + b.HeaderSize + b.CodeSize; i++)
                        data[i] = 0x00; // 收缩后遗留的字节清零（不再参与执行）

                    outcomes.Add(new PatchOutcome
                    {
                        TargetId = target.Id,
                        Member = target.TypeName + "." + target.Member,
                        CodeOffset = bodyOffset,
                        CodeSize = b.CodeSize,
                        BodyKind = b.Kind,
                        OldIl = oldIl,
                        NewIl = il
                    });
                    log.WriteLine($"  [改写] {target.TypeName}.{target.Member}: " +
                                  $"{b.Kind} code {b.CodeSize}B → {il.Length}B  IL {Hex(oldIl, 8)} → {Hex(il, 8)}");
                }
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (outcomes.Count != UnlockTargets.Patches.Count)
            throw new InvalidOperationException(
                $"只处理了 {outcomes.Count}/{UnlockTargets.Patches.Count} 个目标，已中止（不写出半成品）。");

        File.WriteAllBytes(outputPath, data);
        log.WriteLine($"已写出：{Path.GetFileName(outputPath)}（{data.Length} 字节，与原始大小一致）");

        // ---------- 复核 ----------
        log.WriteLine();
        log.WriteLine("复核：");
        Verify(sourcePath, outputPath, untouchedBefore, log);
    }

    private static void Verify(string sourcePath, string outputPath, Dictionary<string, byte[]> untouched, TextWriter log)
    {
        var src = File.ReadAllBytes(sourcePath);
        var dst = File.ReadAllBytes(outputPath);
        var ok = true;

        void Check(string label, bool condition)
        {
            log.WriteLine($"  {label}：{(condition ? "OK" : "FAIL")}");
            if (!condition) ok = false;
        }

        Check("文件长度不变", src.Length == dst.Length);

        var srcBundle = BundleArchive.Parse(src);
        var dstBundle = BundleArchive.Parse(dst);
        Check("单文件包清单不变",
            srcBundle.Entries.Count == dstBundle.Entries.Count &&
            srcBundle.Entries.All(e =>
            {
                var o = dstBundle.Find(e.Name);
                return o is not null && o.Offset == e.Offset && o.Size == e.Size;
            }));

        var diff = 0;
        for (var i = 0; i < Math.Min(src.Length, dst.Length); i++)
            if (src[i] != dst[i])
                diff++;
        log.WriteLine($"  变化字节数：{diff}");

        // 逐目标确认 IL 已改写
        foreach (var asmName in UnlockTargets.RequiredAssemblies)
        {
            var srcEntry = srcBundle.Find(asmName)!;
            var dstEntry = dstBundle.Find(asmName)!;
            var srcAsm = srcBundle.Extract(srcEntry);
            var dstAsm = dstBundle.Extract(dstEntry);

            var temp = Path.Combine(Path.GetTempPath(), "pclce-verify-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllBytes(temp, dstAsm);
                using var module = ModuleDefMD.Load(temp);

                foreach (var target in UnlockTargets.Patches.Where(t =>
                             string.Equals(t.Assembly + ".dll", asmName, StringComparison.OrdinalIgnoreCase)))
                {
                    var b = FindBody(module, dstAsm, target.TypeName,
                        target.IsProperty ? "get_" + target.Member : target.Member);
                    if (b is null)
                    {
                        Check($"{target.Member} 可定位", false);
                        continue;
                    }

                    var il = target.BuildIl();
                    var good = b.CodeSize == il.Length;
                    for (var i = 0; good && i < il.Length; i++)
                        good = b.FileCode[i] == il[i];
                    Check($"{target.Member} 已改写为 {target.Action}（code={il.Length}B，末尾为 ret）", good);
                }

                foreach (var (mustAsm, typeName, member) in UnlockTargets.MustNotChange)
                {
                    if (!string.Equals(mustAsm + ".dll", asmName, StringComparison.OrdinalIgnoreCase)) continue;
                    var after = FindBody(module, dstAsm, typeName, member);
                    var before = untouched.TryGetValue(typeName + "." + member, out var b0) ? b0 : null;
                    var same = after is not null && before is not null &&
                               after.Bytes.SequenceEqual(before);
                    Check($"{typeName}.{member} 逐字节未改动（正版劝导）", same);
                }
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
            }
        }

        log.WriteLine(ok ? "结果：全部通过" : "结果：存在失败项，请勿使用该输出文件");
        if (!ok) throw new InvalidOperationException("补丁复核失败。");
    }

    /// <summary>
    /// 只读检测：统计目标文件里"已经是本工具补丁版"的方法个数。
    /// 用于避免重复打补丁，以及在还原时确认备份确实是原版。
    /// </summary>
    public static (int Matched, int Total) CountApplied(string path)
    {
        var total = UnlockTargets.Patches.Count;
        try
        {
            var data = File.ReadAllBytes(path);
            var bundle = BundleArchive.Parse(data);
            var matched = 0;
            foreach (var asmName in UnlockTargets.RequiredAssemblies)
            {
                var entry = bundle.Find(asmName);
                if (entry is null) return (0, total);
                var asmBytes = bundle.Extract(entry);

                var temp = Path.Combine(Path.GetTempPath(), "pclce-count-" + Guid.NewGuid().ToString("N") + ".dll");
                try
                {
                    File.WriteAllBytes(temp, asmBytes);
                    using var module = ModuleDefMD.Load(temp);
                    foreach (var target in UnlockTargets.Patches.Where(t =>
                                 string.Equals(t.Assembly + ".dll", asmName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var b = FindBody(module, asmBytes, target.TypeName,
                            target.IsProperty ? "get_" + target.Member : target.Member);
                        if (b is null) continue;
                        var il = target.BuildIl();
                        if (b.CodeSize != il.Length) continue;
                        var ok = true;
                        for (var i = 0; i < il.Length && ok; i++)
                            ok = b.FileCode[i] == il[i];
                        if (ok) matched++;
                    }
                }
                finally
                {
                    try { File.Delete(temp); } catch { }
                }
            }

            return (matched, total);
        }
        catch
        {
            return (0, total);
        }
    }

    // ---------- 方法体定位 ----------

    private sealed class BodyRef
    {
        public uint Rva { get; init; }
        public long FileOffset { get; init; }
        public int HeaderSize { get; init; }
        public int CodeSize { get; init; }
        public bool HasExceptionHandlers { get; init; }
        public string Kind { get; init; } = string.Empty;
        public byte[] FileCode { get; init; } = Array.Empty<byte>();
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
    }

    private static BodyRef? FindBody(ModuleDefMD module, byte[] asmBytes, string typeName, string member)
    {
        var type = module.GetTypes().FirstOrDefault(t =>
            string.Equals(t.FullName, typeName, StringComparison.Ordinal));
        var method = type?.Methods.FirstOrDefault(m => m.Name == member);
        if (method is null || !method.HasBody || method.RVA == 0) return null;

        var fileOffset = RvaToFileOffset(module, (uint)method.RVA);
        if (fileOffset < 0) return null;

        var kind = DetectBodyHeader(asmBytes, (int)fileOffset, out var headerSize, out var codeSize, out var hasEh);
        if (kind is null) return null;

        var bytes = new byte[headerSize + codeSize];
        Buffer.BlockCopy(asmBytes, (int)fileOffset, bytes, 0, bytes.Length);
        var code = new byte[codeSize];
        Buffer.BlockCopy(asmBytes, (int)fileOffset + headerSize, code, 0, codeSize);

        return new BodyRef
        {
            Rva = (uint)method.RVA,
            FileOffset = fileOffset,
            HeaderSize = headerSize,
            CodeSize = codeSize,
            HasExceptionHandlers = hasEh,
            Kind = kind,
            FileCode = code,
            Bytes = bytes
        };
    }

    private static string? DetectBodyHeader(byte[] data, int offset, out int headerSize, out int codeSize,
        out bool hasExceptionHandlers)
    {
        headerSize = 0;
        codeSize = 0;
        hasExceptionHandlers = false;
        if (offset < 0 || offset >= data.Length) return null;

        var b = data[offset];
        switch (b & 0x03)
        {
            case 0x02: // tiny
                headerSize = 1;
                codeSize = b >> 2;
                return "tiny";
            case 0x03: // fat
            {
                var flagsAndSize = R16(data, offset);
                var flags = flagsAndSize & 0x0FFFu;
                headerSize = (flagsAndSize >> 12) * 4;
                codeSize = R32(data, offset + 4);
                hasExceptionHandlers = (flags & 0x08) != 0;
                if (headerSize < 12 || codeSize <= 0) return null;
                return "fat";
            }
            default:
                return null;
        }
    }

    private static long RvaToFileOffset(ModuleDefMD module, uint rva)
    {
        var pe = module.Metadata.PEImage;
        foreach (var section in pe.ImageSectionHeaders)
        {
            var va = (uint)section.VirtualAddress;
            var size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva < va || rva >= va + size) continue;
            return (uint)section.PointerToRawData + (rva - va);
        }

        return -1;
    }

    /// <summary>读取 COR20 头的 ManagedNativeHeader RVA（非 0 表示 ReadyToRun）。</summary>
    private static uint ReadManagedNativeHeaderRva(byte[] asmBytes)
    {
        var peOff = R32(asmBytes, 0x3C);
        var optHeader = peOff + 24;
        var magic = R16(asmBytes, optHeader);
        var isPe32Plus = magic == 0x20B;
        var dataDir = optHeader + (isPe32Plus ? 112 : 96);
        var cliRva = R32(asmBytes, dataDir + 14 * 8);
        if (cliRva <= 0) return 0;

        var cliOff = RvaToFileOffsetRaw(asmBytes, (uint)cliRva);
        if (cliOff < 0) return 0;
        return (uint)R32(asmBytes, (int)cliOff + 64);
    }

    private static ReadOnlySpan<byte> Span4(byte[] buffer, int offset) =>
        new(buffer, offset, 4);

    private static ReadOnlySpan<byte> Span2(byte[] buffer, int offset) =>
        new(buffer, offset, 2);

    private static int R32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(Span4(buffer, offset));

    private static ushort R16(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(Span2(buffer, offset));

    private static long RvaToFileOffsetRaw(byte[] data, uint rva)
    {
        var peOff = R32(data, 0x3C);
        var sectionCount = R16(data, peOff + 6);
        var optSize = R16(data, peOff + 20);
        var sectionStart = peOff + 24 + optSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var s = sectionStart + i * 40;
            var vsize = R32(data, s + 8);
            var vaddr = R32(data, s + 12);
            var rawSize = R32(data, s + 16);
            var rawPtr = R32(data, s + 20);
            var size = Math.Max(vsize, rawSize);
            if (rva >= vaddr && rva < vaddr + size) return rawPtr + (rva - (uint)vaddr);
        }

        return -1;
    }

    private static string Hex(byte[] bytes, int take) =>
        string.Join(' ', bytes.Take(take).Select(x => x.ToString("x2")));
}
