# 开发说明

本页供修改 PSAI 源码、构建模组和执行开发检查时使用。普通游玩只需 README 的安装步骤及运行时接口。

## 构建和手动安装

需要 Windows、PowerShell 7、`global.json` 指定的 .NET SDK、Node.js 24 和受支持的 Steam 游戏。将 `psai.local.example.json` 复制成被忽略的 `psai.local.json`，填写本机游戏目录；脚本也接受 `-GameDirectory`。

```powershell
Copy-Item psai.local.example.json psai.local.json
pwsh -NoProfile -File scripts/Build.ps1
pwsh -NoProfile -File scripts/Scan.ps1
```

构建引用本机游戏程序集，不分发游戏代码或资产。安装产物在 `artifacts/psai/`，包括 DLL 和清单。原版扫描检查权限与兼容性。

退出游戏后运行 `pwsh -NoProfile -File scripts/Install.ps1`。安装器扫描模组并校验文件哈希，然后复制到游戏的 `Mods/psai/`。之后启用模组、重启游戏并载入存档。

## 开发检查

```powershell
npm ci --ignore-scripts --registry=https://registry.npmjs.org
npm run check
npm run build
npm test
pwsh -NoProfile -File scripts/Inspect-Game.ps1
pwsh -NoProfile -File scripts/Test-Mod.ps1
```

这些离线检查不要求运行游戏。`npm run test:live`、`npm run test:panels` 是会实际操作游戏的开发验收脚本，仅在明确准备好相应验收条件时运行；正常安装和游玩不需要执行它们。

保持六工具协议与现有字段兼容。游戏对象只在 Unity 主线程访问，状态和动作条件实时读取，动作调用原版交互入口。详情查询遵守窗口可见性；会话、状态版本和命令去重检查由桥接统一处理。

## 性能诊断

`node scripts/Measure-Bridge.ts` 测量离线目录枚举；用户确认可操作存档就绪后，`--live` 读取桥接耗时。设置 `PSAI_PROFILE=1` 时，CLI/MCP 返回端到端时间和回执体积。原版回执包含快照和目录枚举统计，诊断数据不参与状态版本。

开发者个人的交接、测试流水及临时约束应保存在本机忽略目录，不作为游玩指导或发行能力说明。版本说明描述用户可用能力；新增接口的实机覆盖范围由发布时实际验证结果说明。

## 工坊发布

先构建并通过原版扫描，再运行 `pwsh -NoProfile -File scripts/Prepare-Workshop.ps1`。它只打包模组 DLL 和清单，生成预览图及公开发布配置；工坊文案位于 `workshop/description.txt`。

将 Valve 官方 SteamCMD 安装到 `.cache/steamcmd/`，在可交互终端运行 `pwsh -NoProfile -File scripts/Upload-Workshop.ps1`，使用拥有游戏的账号登录。密码和 Steam Guard 由 SteamCMD 交互处理，不写进脚本。首次发布可能需要在 Steam 网页接受工坊协议。

发布配置 `.local-dev/workshop/psai.vdf` 不进入 Git。SteamCMD 会将工坊条目 ID 写回该文件，后续发布保留它以更新原条目，避免重复创建。上传成功后再更新 README 的订阅链接，并核对网页可见性及版本。

已发布条目的公共 ID 同步保存在 `workshop/item.json`。准备脚本在新工作目录中也会复用该 ID；如果本机配置指向不同条目，会停止而不是覆盖。工坊包只在实际上传后更新，GitHub 提交本身不会改变已发布的二进制文件。
