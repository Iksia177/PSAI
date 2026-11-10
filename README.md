# PSAI

Probably Stolen Agent Interface，为 Steam 版《深空当铺：可能是偷的》提供 agent 游玩接口。

当前处于开发准备阶段。仓库包含可编译的原生模组骨架、离线构建与检查脚本，以及后续实现方案。游戏内骨架仅输出生命周期日志，CLI、MCP、观察与动作协议将在下一阶段实现。

## 已确认的方向

- 原生 C# 模组负责读取状态和执行正常游戏动作，外部 TypeScript CLI 与 MCP 适配器共用协议。
- 模组通过官方自有数据目录 API 交换 JSON，在 Unity 主线程处理游戏对象。
- 以玩家可见信息为边界，优先按需查询具体细节，图像作为复杂视觉特征的补充。
- 首版验收完成一个正常营业日，涵盖顾客、对话、库存、议价、交易与日结。

## 本机开发

需要 PowerShell 7、global.json 指定的 .NET SDK 和已安装的 Steam 游戏。后续 CLI 与 MCP 使用 Node.js 24。

将 `psai.local.example.json` 复制成 `psai.local.json`，填写游戏安装路径。该本机配置被 Git 忽略。脚本也支持显式传入 `-GameDirectory`。

```powershell
Copy-Item psai.local.example.json psai.local.json
pwsh -NoProfile -File scripts/Inspect-Game.ps1
pwsh -NoProfile -File scripts/Build.ps1
pwsh -NoProfile -File scripts/Scan.ps1
```

脚本会在常见安装目录寻找 `dotnet.exe`，可处理 SDK 已安装但尚未加入 PATH 的情况。构建从本机游戏目录引用程序集，通过本地空包源还原，无需下载 NuGet 依赖。SDK 缓存位于 `.cache/`。

产物位于 `artifacts/psai/`，仅包含模组 DLL 和清单。扫描结果保存在 `artifacts/scan.json`，静态检查结果保存在 `artifacts/compatibility.json`。这些生成文件均被 Git 忽略。

游戏内加载需要把产物放入游戏的 `Mods/psai/`，在模组菜单启用后重启。当前骨架的预期效果是出现 `[PSAI ...]` 生命周期日志，完整实机加载留待下一阶段验证。

## 项目文档

- [设计与验收约定](docs/DESIGN.md)
- [本轮准备结果与下一轮入口](docs/STATUS.md)

本仓库从本地 Git 开始记录开发。首轮功能验收后再配置 GitHub 仓库与推送。版本管理覆盖源码、脚本和文档，本机配置、游戏程序集、存档、日志、运行消息和构建产物通过忽略规则排除。
