# 一体化构建：产出补丁工具 + hook 注入组件
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$out = if ($args.Count -ge 1) { $args[0] } else { 'build-out' }
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path "$out/hook" -Force | Out-Null

Write-Host '[1/2] 构建注入版组件 PCLUnlockHook.dll ...'
dotnet publish src/Hook/Hook.csproj -c Release -o "$out/hook" -v q --nologo

Write-Host '[2/2] 构建补丁工具 PclCeUnlock.exe ...'
dotnet publish src/Tool/Tool.csproj -c Release -r win-x64 --self-contained false -o $out -v q --nologo

Write-Host ''
Write-Host "构建完成：$out"
