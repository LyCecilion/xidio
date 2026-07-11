# xidio Agents Instructions

xidio（Xidian Internet Diagnostic Intelligence Operator）是 Project Hazelita 基于 C# 和 .NET 10 开发的跨平台西电校园网诊断工具。

## 开始工作前

1. 阅读 `README.md` 了解当前能力与构建方式。
2. 阅读 `docs/architecture.md` 和修改模块的相关文档。
3. 对照 `ROADMAP.md` 确认需求边界；路线图是开发方向，不代表功能已实现。
4. 检查当前分支和工作区，保留用户的现有修改。

## 修改原则

- 保持修改最小化，避免与任务无关的重构、格式化或依赖升级。
- 优先使用 .NET 跨平台 API；平台专用能力必须放入对应的 `xidio.Platform.*` Provider。
- 不得在 `xidio.Core` 中使用 `Console`、Spectre.Console、Avalonia 或其他 UI API。
- `xidio.Core` 应输出结构化数据，并明确分离诊断、修复和上报逻辑。
- 所有 I/O 与长时间操作使用异步 API，接受 `CancellationToken`，必要时报告结构化进度。
- 探测失败也是诊断数据；保留目标、时间、耗时、错误类型与必要原始信息，不得只返回布尔值。
- 未获得用户明确同意时，不收集 hosts、代理、VPN、防火墙、安全软件、账号业务状态等敏感信息，不执行修复。
- MAC、BSSID、账号、在线设备与日志中的个人信息默认脱敏；任何上报都必须 opt-in 且可预览。

## 架构边界

- `xidio.Core`：诊断、分析、修复、上报的抽象、模型与通用逻辑。
- `xidio.CLI`：终端交互、进度展示、结果渲染与平台 Provider 组装。
- `xidio.Platform.Windows`：Windows 系统 API、WLAN、RAS/PPPoE 及其他 Windows 专用收集。
- `xidio.Platform.macOS`：macOS 系统命令与 API 适配。
- `xidio.Platform.Linux`：Linux 系统 API，以及 `ip`、`resolvectl`、`nmcli`、`iw` 等工具的可控适配。

新增平台信息时，先在 Core 定义平台无关的模型和接口，再在各 Provider 中实现。单一平台不支持某项能力时，应返回明确的“不支持/不可用”结果，不得伪造数据或导致整份报告失败。

## 验证

- 根据修改范围运行最小相关构建或测试，再考虑完整解决方案构建。
- Windows：`dotnet build xidio/xidio.slnx -c Release`。
- Linux/macOS：`dotnet build xidio/xidio.CLI/xidio.CLI.csproj -c Release -f net10.0`。
- 平台专用修改必须说明实际验证平台；不得把交叉编译当作真实系统行为验证。
- 对外部命令的解析应覆盖空输出、命令不存在、非零退出码、本地化输出和权限不足。

## Git 与文档

- 遵循 Git Flow：功能与普通修复从 `develop` 分支开始，已发布版本的紧急修复从 `main` 建立 `hotfix/*`。
- 提交信息遵循 Conventional Commits，推荐使用精确 scope，例如 `feat(linux): collect wireless details`。
- 不要重写、压缩或删除用户现有提交，除非用户明确要求。
- 功能范围或计划变化时更新 `ROADMAP.md`；用户可见的使用、构建或平台状态变化时同步更新 `README.md` 和 `CHANGELOG.md`。
