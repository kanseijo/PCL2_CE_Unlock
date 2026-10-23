#!/usr/bin/env bash
# 一体化构建：产出 ① 注入版组件（hook/） ② 补丁工具（可生成补丁版 exe）
set -euo pipefail
cd "$(dirname "$0")"

OUT="${1:-PCL2_CE_Unlock}"
rm -rf "$OUT"
mkdir -p "$OUT/hook"

echo "[1/2] 构建注入版组件 PCLUnlockHook.dll ..."
dotnet publish src/Hook/Hook.csproj -c Release -o "$OUT/hook" -v q --nologo

echo "[2/2] 构建补丁工具 PclCeUnlock.exe ..."
dotnet publish src/Tool/Tool.csproj -c Release -o "$OUT" -v q --nologo

# hook 目录里不需要 pdb/deps 之外的杂项
rm -f "$OUT"/hook/*.pdb 2>/dev/null || true

echo
echo "构建完成：$OUT"
echo "  - $OUT/PCL2_CE_Unlock.exe            主程序（检测/选择启动器、就地打补丁、注入启动、备份还原）"
echo "  - $OUT/hook/PCL2_CE_Unlock.Hook.dll  注入组件"
