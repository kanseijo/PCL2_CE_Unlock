using System;

namespace PclCeUnlock.Shared;

/// <summary>工具自身信息。</summary>
public static class ToolInfo
{
    public const string Version = "1.3.2";

    public const string HookFileName = "PCL2_CE_Unlock.Hook.dll";

    public const string Name = "PCL2_CE 解锁工具（第三方·非官方）";

    public static string Title =>
        $"{Name} v{Version}（规格 {UnlockTargets.SpecVersion}：地区限制 + 大陆判定 + 首次建档）";
}
