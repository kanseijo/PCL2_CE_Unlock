using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using PclCeUnlock.Injection;

// 重要：.NET 宿主按名字 "StartupHook" 在【全局命名空间】查找启动钩子类型，
// 因此 StartupHook 不能放进任何 namespace，否则会抛 TypeLoadException 让目标程序启动即崩溃。

/// <summary>
/// DOTNET_STARTUP_HOOKS 入口。
/// <para>
/// 注意：本类型<b>不能直接引用 MonoMod 类型</b>，否则 JIT 编译 Initialize 时会先加载依赖，
/// 早于程序集解析器注册 → 启动失败。真实挂钩逻辑放在 <see cref="Patcher"/> 中延迟 JIT。
/// </para>
/// </summary>
public static class StartupHook
{
    [ModuleInitializer]
    internal static void Init() => LocalResolver.Register();

    public static void Initialize()
    {
        try
        {
            LocalResolver.Register();
            Patcher.Run();
        }
        catch (Exception ex)
        {
            Logger.Note("注入失败（不影响启动器运行）：" + ex);
        }
        finally
        {
            Logger.Flush();
        }
    }
}

namespace PclCeUnlock.Injection
{
    /// <summary>从钩子所在目录解析依赖（MonoMod / Mono.Cecil 等）。</summary>
    internal static class LocalResolver
    {
        private static bool _registered;

        internal static string BaseDir { get; } =
            Path.GetDirectoryName(typeof(LocalResolver).Assembly.Location) ?? ".";

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                try
                {
                    var path = Path.Combine(BaseDir, new AssemblyName(e.Name).Name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                }
                catch
                {
                    return null;
                }
            };
        }
    }

    /// <summary>把挂钩结果写到启动器目录下的日志文件，便于用户自查。</summary>
    internal static class Logger
    {
        private static readonly List<string> Lines = new();

        internal static void Note(string text) => Lines.Add($"[{DateTime.Now:HH:mm:ss}] {text}");

        internal static void Flush()
        {
            if (Lines.Count == 0) return;
            var text = string.Join(Environment.NewLine, Lines) + Environment.NewLine;
            Lines.Clear();
            foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
            {
                try
                {
                    File.AppendAllText(Path.Combine(dir, "PCL2_CE_Unlock.log"), text);
                    return;
                }
                catch
                {
                    // 换下一个目录
                }
            }
        }
    }
}
