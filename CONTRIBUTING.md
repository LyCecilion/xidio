# Contributing to xidio

感谢你愿意帮助 xidio 更准确地诊断西电校园网问题。我们接受 Issue 和 Pull Request；不论是问题复现、平台适配、诊断规则还是文档改进，都欢迎参与。

## 提交 Issue

在新建 Issue 前，请先搜索是否已有相同问题。报告 Bug 时建议包含：

- xidio 版本、操作系统与系统版本；
- 连接方式与可稳定复现的步骤；
- 期望结果、实际结果与必要日志；
- 是否以管理员或 root 权限运行。

发布日志、截图或诊断报告前，请删除账号、密码、完整 MAC/BSSID、IP 等不必要的个人或网络标识信息。

## 提交 Pull Request

1. 从最新的 `develop` 分支创建短期分支：新功能使用 `feature/<name>`，普通修复使用 `fix/<name>`。仅针对已发布版本的紧急修复从 `main` 创建 `hotfix/<name>`。
2. 保持修改最小化，不要在同一 PR 中混入无关重构。
3. 提交信息遵循 [Conventional Commits](https://www.conventionalcommits.org/)，例如 `feat(linux): collect default route` 或 `fix(core): preserve probe cancellation`。
4. 根据修改所在平台运行构建和相关测试，并在 PR 中说明未能验证的平台。
5. 默认将 PR 提交到 `develop`，在描述中关联 Issue，并说明用户可见变化、验证方法与隐私影响。

## 开发约定

- 先阅读 [`README.md`](./README.md)、[`AGENTS.md`](./AGENTS.md) 与 [`docs/architecture.md`](./docs/architecture.md)。
- `xidio.Core` 不得依赖 CLI/GUI，应输出结构化数据，并为长任务提供异步、进度与取消支持。
- 平台专用能力放入相应的 `xidio.Platform.*` 项目，通过 Core 中的抽象调用。
- 收集敏感信息或执行修复前必须获得用户明确同意；上报始终保持 opt-in。

## 构建

Windows：

```powershell
dotnet restore xidio/xidio.slnx
dotnet build xidio/xidio.slnx -c Release --no-restore
```

Linux 或 macOS：

```bash
dotnet restore xidio/xidio.CLI/xidio.CLI.csproj
dotnet build xidio/xidio.CLI/xidio.CLI.csproj -c Release -f net10.0 --no-restore
```

更多开发环境信息见 [`README.md`](./README.md#-development)。
