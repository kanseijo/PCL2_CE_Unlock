# pclce-unlock — PCL-CE 解锁工具（第三方 · 非官方）

> **非官方第三方工具**，与 [PCL 社区版](https://github.com/PCL-Community/PCL-CE)、PCL-Community、龙腾猫跃均无关联，不代表其立场。

解除 PCL 社区版的**地区限制**与**首次登录限制**，提供「补丁版」和「注入版」两种形态，
**可逆、可还原、保留正版劝导**。

---

## 一、解锁内容（只做三件事）

| # | 目标方法 | 改写为 | 效果 |
| --- | --- | --- | --- |
| 1 | `PCL.RestrictedFeaturePolicy.get_IsRestricted` | `false` | 解除地区限制判定 |
| 2 | `PCL.RestrictedFeaturePolicy.Check(...)` | `true` | 解锁 Minecraft 下载 / 补全 / 修复 / 重置 / 整合包安装（14 处调用点） |
| 3 | `PCL.RestrictedFeaturePolicy.ThrowIfRestricted(...)` | 直接返回 | 不再抛取消异常 |
| 4 | `PCL.Core.App.Localization.Lang.get_IsFeaturesUnrestricted` | `true` | 解除"大陆环境"判定：任何地区都走**可跳过**的劝导 |
| 5 | `PCL.ProfileUi.CanCreateOtherProfile()` | `true` | 首次启动即可选择「第三方验证 / 离线登录」 |

**明确不改动**（每次打补丁都会逐字节校验）：

- `PCL.ModLaunch.McLaunchPrecheck` —— 正版购买劝导 / 试玩 / 返回逻辑
- `PCL.Core...ProfileService.get_HasMicrosoftProfile` —— 劝导的入口条件

因此：没有正版档案时，启动仍会看到**可跳过**的「考虑一下正版？」提示，只是不再被"必须登录正版"硬拦。
**请支持正版 Minecraft。**

---

## 二、两种形态

| 形态 | 做法 | 特点 |
| --- | --- | --- |
| **补丁版** | 直接在官方 exe 内部原地改写 5 个方法的 IL | 双击即用；exe 被改动，官方 sigstore 签名不再匹配 |
| **注入版** | `DOTNET_STARTUP_HOOKS` + 运行时方法替换（MonoMod） | **官方 exe 一字节不改**，哈希/签名保持有效；需通过本工具启动 |

两者共用同一份解锁定义（[`src/Shared/UnlockTargets.cs`](src/Shared/UnlockTargets.cs)），不会出现行为不一致。

### 补丁版的实现要点（容易踩的坑）

IL 改写必须把方法体**收缩到精确长度**（tiny 重写头字节、fat 只改 `CodeSize`），
并把原体积剩余字节清零，**方法体最后一个字节必须是 `ret`**。
若保留原长度并用 `nop` 填充，**fat 方法体**（带局部变量头，如 `Check`）会让 JIT 判整段 IL 非法，
表现为 `System.InvalidProgramException: Common Language Runtime detected an invalid program`。

---

## 三、使用

### 3.1 布局（强制）

工具必须放在**启动器所在目录的子文件夹**里，启动器在**上一级目录**——这样工具自己的文件不会混进 PCL 目录：

```
<你的 PCL 目录>\
├─ PCL2_CE_Release_x64.exe    ← 官方启动器（名字随意，可中文、可改名）
├─ ... PCL 自己的文件 ...
└─ 解锁工具\                  ← 本工具放在这里
    ├─ PclCeUnlock.exe
    ├─ PclCeUnlock.dll / .deps.json / .runtimeconfig.json
    ├─ dnlib.dll
    └─ hook\                  ← 注入组件（勿单独移动）
```

选择其它目录的启动器会被**拒绝并提示**；把工具直接放进启动器目录也会给出"请新建子文件夹"的提示。

### 3.2 菜单

```
1) 重新自动检测            扫描上级目录，按内容识别启动器（不依赖文件名）
2) 手动选择启动器          文件对话框 / 拖入路径
3) 打补丁                  就地替换原启动器（同名，快捷方式不失效）+ 生成原版备份
4) 以注入方式启动          官方 exe 保持原样
5) 从备份恢复原版启动器
6) 创建桌面快捷方式        注入启动PCL2_CE
7) 查看配置与状态
```

### 3.3 命令行

```
PclCeUnlock --inject    用已记住的启动器注入启动
PclCeUnlock --patch     打补丁（替换原启动器 + 备份）
PclCeUnlock --restore   从备份还原
PclCeUnlock --shortcut  创建桌面快捷方式
PclCeUnlock --status    打印配置与解锁目标
```

### 3.4 安全与可逆

- 打补丁前先把结果写到临时文件并**自校验**（文件长度、单文件包清单、5 个目标、劝导两方法未改动），通过后才替换；
- 替换前把原文件改名为 `<原文件名>.<时间戳>.原版.bak`，**只有原版备份能用于还原**；
- 还原时当前补丁版留档为 `<原文件名>.<时间戳>.补丁版.bak`；
- 重复打补丁会被识别（读取方法体比对，提示 `5/5 已改写`），不会叠加；
- 设置文件只保存**文件名**并附校验码，被手工改动会被识别并忽略。

---

## 四、构建

需要 **.NET SDK 7 或更新**（目标框架 `net7.0`，`RollForward=Major`，可在 .NET 7/8/9/10 运行）：

```bash
./build.sh              # Linux/macOS
pwsh -File build.ps1    # Windows
```

产物：补丁工具 `PclCeUnlock.exe`（win-x64）+ `hook/PCLUnlockHook.dll`。

运行需要 **.NET Desktop Runtime**（能运行 PCL-CE 说明已具备）。

---

## 五、已知限制

1. 上游若把这些方法**改名或改结构**，补丁会在生成阶段报错中止（不会产出半成品），注入版会在日志里写「未找到 …（上游可能改名）」。
2. 只针对 **PCL-CE v2.15.1** 验证；其它版本可能失效。
3. 补丁版替换后，上游自动更新会把它覆盖（建议关闭自动更新，或更新后重新打补丁）。
4. 实例级「登录方式 = 要求正版验证」仍会拦截（用户自设项）；LabyMod 实例仍要求微软档案（上游逻辑）。

---

## 六、与其它项目的区别

同类项目 [Myitian/UnlockPCLCE](https://github.com/Myitian/UnlockPCLCE)（MIT）通过 Harmony 注入实现类似效果，本项目在以下方面不同：

- **保留正版劝导**：`McLaunchPrecheck` 与 `HasMicrosoftProfile` 逐字节不改；对方会改写 `HasMicrosoftProfile`，劝导不再出现。
- **免编译**：提供预编译工具与依赖，用户无需安装 .NET SDK。
- **二进制补丁 + 可逆**：可就地打补丁并区分「原版 / 补丁版」备份，一键还原；官方文件哈希与文件名保持不变，桌面快捷方式继续有效。
- **按内容识别启动器**：读取包内程序集判断，不依赖文件名模式。
- **目录整洁**：强制"工具在子文件夹、启动器在上一级"。

（客观描述差异，无贬低之意；欢迎互相参考。）

---

## 七、免责声明

- 本项目为第三方工具，**可能导致启动器行为与官方不同**，请自行评估风险；
- 修改/替换启动器请**先备份**（本工具会自动备份，但仍建议自行另存一份）；
- 请遵守 Minecraft 最终用户许可协议与微软服务条款，**通过官方渠道购买游戏**；
- 作者不对使用本工具产生的任何后果负责。

## 八、致谢与版权

- PCL / PCL 社区版的版权归 **龙腾猫跃** 及其权利主体所有（见其仓库 `LICENSE`）。
  如果觉得 PCL 好用，可以到 [爱发电](https://afdian.com/a/LTCat) 支持作者。
- 本项目**不包含** PCL 的任何源代码或二进制文件。
- 第三方组件（MonoMod、Mono.Cecil、dnlib，均为 MIT）声明见 [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)。
- 本项目代码采用 [MIT](LICENSE) 许可。
