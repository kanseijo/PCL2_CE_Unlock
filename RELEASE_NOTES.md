## PCL2_CE_Unlock

> **非官方第三方工具**，与 PCL / PCL 社区版、PCL-Community、龙腾猫跃均无关联。
> 本项目不含 PCL 的任何代码或二进制文件。

PCL2_CE 解锁工具：解除地区限制与首次登录限制，提供「补丁版」与「注入版」两种形态，
可就地替换并一键还原，**保留正版劝导**。

### 压缩包内容

解压后得到上层 `PCL2_CE_Unlock/` 文件夹，内含主程序、注入组件 `hook/`、`dnlib.dll`、README 与第三方声明。
校验和见附件 `.sha256`。

### 快速使用

1. 把 `PCL2_CE_Unlock` 文件夹放进**你的 PCL 目录**（启动器位于其上一级）；
2. 运行 `PCL2_CE_Unlock.exe`，自动识别上级目录的启动器；
3. `3)` 打补丁（就地替换 + 自动备份）或 `4)` 注入启动（官方 exe 原样）。

### 说明

- 解锁范围：地区限制、大陆环境判定、首次建档限制；**不改动正版劝导**（`McLaunchPrecheck` / `HasMicrosoftProfile` 逐字节不变）。
- 请支持正版 Minecraft。
- 详细变更见仓库 `docs/release-notes/` 下对应版本文件。

本项目代码采用 MIT 许可；随附第三方组件（MonoMod / Mono.Cecil / dnlib）均 MIT，声明见 `THIRD-PARTY-NOTICES.txt`。
