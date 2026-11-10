# PSAI

Probably Stolen Agent Interface，为 Steam 版《深空当铺：可能是偷的》提供 agent 游玩接口。

当前为实验性 v0.1，已经实现游戏内 JSON 桥接、CLI 和 MCP 入口，并实机完成第 1 天普通营业、进入第 2 天早晨。日结界面的读取与关闭、鉴定细节、局部图像及部分异常场景仍待验证或补齐，完整进度见 `docs/STATUS.md`。

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

退出游戏后运行 `pwsh -NoProfile -File scripts/Install.ps1` 安装到 `Mods/psai/`。安装器先扫描，再核对已有文件及安装回执，校验复制后的哈希。在模组菜单启用后重启并载入独立测试存档。

## CLI 与 MCP

Node.js 24 可以直接运行本项目的 TypeScript 入口。安装锁定依赖后执行检查与测试。

```powershell
npm ci --ignore-scripts --registry=https://registry.npmjs.org
npm run check
npm run build
npm test
node src/client/cli.ts status
node src/client/cli.ts observe
node src/client/cli.ts actions
```

CLI 默认使用当前用户的 `AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/psai`。可使用 `--data-dir` 或 `PSAI_DATA_DIR` 指定路径。`observe` 回执提供 `session_id` 和 `data.revision`，动作必须携带这两个值。

```powershell
node src/client/cli.ts act '{"action":"begin_control"}' --revision 1 --session <session_id>
node src/client/cli.ts inspect '{"target":"item-1","detail":"description"}'
node src/client/cli.ts result <command_id> --session <session_id>
```

实际 revision 与对象 ID 来自最新观察。`begin_control` 临时切换到手动对话和手动叫号，`end_control` 恢复原选项。工具输出是结构化数据，游戏中的文本按游戏内容处理。

MCP stdio 启动命令为 `node D:/CodexWork/PSAI/src/client/mcp.ts`。在支持 MCP 的 harness 中将它配置为本地 stdio 服务，可选设置 `PSAI_DATA_DIR`。工具为 `psai_status`、`psai_observe`、`psai_inspect`、`psai_actions`、`psai_act`、`psai_result`。`psai_act` 明确要求观察中的 `session_id` 和 `expected_revision`。

`act` 返回 completed 表示正常操作处理函数已返回，随后应重新观察动画和后续面板。超时使用原命令 ID 查询，重试复用同一 ID 和完全相同参数。uncertain 表示需要重新观察后判断实际结果。当前 `inspect` 支持可见物品说明，鉴定细节与局部图像仍待扩展。

## 项目文档

- [设计与验收约定](docs/DESIGN.md)
- [本轮准备结果与下一轮入口](docs/STATUS.md)
- [自动检查与实机验收步骤](docs/TESTING.md)

本仓库从本地 Git 开始记录开发。首轮功能验收后再配置 GitHub 仓库与推送。版本管理覆盖源码、脚本和文档，本机配置、游戏程序集、存档、日志、运行消息和构建产物通过忽略规则排除。
