# 一体化构建：产出主程序 + 注入组件（输出目录默认 PCL2_CE_Unlock）
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$out = if ($args.Count -ge 1) { $args[0] } else { 'PCL2_CE_Unlock' }
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path "$out/hook" -Force | Out-Null

Write-Host '[1/2] 构建注入组件 PCL2_CE_Unlock.Hook.dll ...'
dotnet publish src/Hook/Hook.csproj -c Release -o "$out/hook" -v q --nologo

Write-Host '[2/2] 构建主程序 PCL2_CE_Unlock.exe ...'
dotnet publish src/Tool/Tool.csproj -c Release -r win-x64 --self-contained false -o $out -v q --nologo

Write-Host ''
Write-Host "构建完成：$out"
Write-Host "  - $out/PCL2_CE_Unlock.exe            主程序"
Write-Host "  - $out/hook/PCL2_CE_Unlock.Hook.dll  注入组件"
