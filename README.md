<!-- markdownlint-disable MD033 MD036 MD041 -->

<div align="center">

<br/>
<img src="./assets/xidio_icon.svg" alt="xidio Logo" width="130"><br/>

<img src="./assets/xidio_logo_bright.svg#gh-light-mode-only" alt="xidio Logo" width="150">
<img src="./assets/xidio_logo_dark.svg#gh-dark-mode-only" alt="xidio Logo" width="150">
<br/>

_✨**Xidian Internet Diagnostic Intelligence Operator**✨_\
**西电校园网诊断工具**

由 Project Hazelita 开发 · 以 MIT License 开源 · [Marduk](https://github.com/NanCunChild/Marduk) 精神续作\
**【✦ —— 拨开网络迷雾，触及以太真实 —— ✦】**

</div>

## 📖 About

xidio _(Xidian Internet Diagnostic Intelligence Operator)_ 是一款由 Project Hazelita 开发的西安电子科技大学校园网诊断工具。项目基于 C# 和 .NET 10，面向 Windows、Linux 与 macOS，希望将分散的网卡、IP、DHCP、DNS、路由、Wi-Fi 及主动探测信息整理为可理解、可上报的诊断结果。

xidio 的灵感来自 [NanCunChild](https://github.com/NanCunChild) 的 [Marduk](https://github.com/NanCunChild/Marduk)。项目当前处于早期开发阶段：v0.1.1 已提供 CLI 与 Windows、Linux、macOS 平台 Provider，完整分析、修复与上报功能仍在实现中。

## ✨ Features

- **交互式场景问询**：通过 CLI 收集位置、连接方式、问题现象与影响范围，为后续判断补充用户上下文。
- **分层网络信息收集**：收集操作系统、时间、网络接口、IP、DHCP、DNS、默认路由、邻居表和主动探测结果。
- **Windows 深度适配**：利用 Windows 系统能力获取网卡、Wi-Fi、路由、代理与 PPPoE 等诊断信息。
- **跨平台架构**：通用诊断逻辑位于 `xidio.Core`，平台差异由独立 Provider 隔离；当前已有 Windows、Linux 和 macOS Provider。
- **结构化与可取消**：核心诊断输出结构化数据，长时间任务支持进度报告和 `CancellationToken`。

## 🚀 Quick Start

当前版本需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。从源码运行 CLI：

```bash
git clone https://github.com/LyCecilion/xidio.git
cd xidio
dotnet restore xidio/xidio.CLI/xidio.CLI.csproj
dotnet run --project xidio/xidio.CLI/xidio.CLI.csproj -f net10.0
```

Windows 上需要调用 Windows 平台 Provider 时，请将最后的目标框架改为 `net10.0-windows`。部分系统信息需要管理员或 root 权限；仅在信任项目源码且确有需要时提权运行。

## 📁 Project Structure

```text
xidio/
├── assets/                       # Logo、图标与设计源文件
├── docs/                         # 架构与核心开发准则
├── xidio/
│   ├── xidio.CLI/                   # 命令行入口与交互式报告
│   ├── xidio.Core/                  # 诊断模型、收集、分析与抽象
│   ├── xidio.Platform.Linux/        # Linux 平台 Provider
│   ├── xidio.Platform.Windows/      # Windows 平台 Provider
│   ├── xidio.Platform.macOS/        # macOS 平台 Provider
│   └── xidio.slnx                   # .NET 解决方案
├── AGENTS.md                    # 仓库内 AI Agent 协作约定
├── CONTRIBUTING.md              # 贡献指南
├── ROADMAP.md                   # 开发路线与诊断范围
└── flake.nix                    # 可选的 Nix 开发环境
```

## 💻 Development

通用要求：

- 使用 `global.json` 声明的 .NET 10 SDK；代码风格由 `Directory.Build.props` 中的 .NET Analyzer 规则约束。
- 修改前阅读 [`AGENTS.md`](./AGENTS.md)、[`docs/architecture.md`](./docs/architecture.md) 和相关模块文档。
- 分支遵循 Git Flow，提交信息遵循 [Conventional Commits](https://www.conventionalcommits.org/)。

### Windows

安装 .NET 10 SDK，使用 PowerShell 运行：

```powershell
dotnet restore xidio/xidio.slnx
dotnet build xidio/xidio.slnx -c Debug
dotnet run --project xidio/xidio.CLI/xidio.CLI.csproj -f net10.0-windows
```

### Linux

可以从发行版安装 .NET 10 SDK，也可使用 Nix。原生 SDK 环境下：

Linux Provider 会按需调用 `ip`、`nmcli` 和 `iw`；建议安装 `iproute2`、NetworkManager 与 `iw`。命令不存在时，xidio 仍会返回 .NET 与 `/sys` 可获取的其他诊断信息。

```bash
dotnet restore xidio/xidio.CLI/xidio.CLI.csproj
dotnet build xidio/xidio.CLI/xidio.CLI.csproj -c Debug -f net10.0
```

使用 Nix 时，无需全局安装 .NET SDK：

```bash
nix develop
dotnet build xidio/xidio.CLI/xidio.CLI.csproj -c Debug -f net10.0
```

### macOS

安装 .NET 10 SDK 后运行：

```bash
dotnet restore xidio/xidio.CLI/xidio.CLI.csproj
dotnet build xidio/xidio.CLI/xidio.CLI.csproj -c Debug -f net10.0
dotnet run --project xidio/xidio.CLI/xidio.CLI.csproj -f net10.0
```

Apple Silicon Mac 也可使用已启用 `aarch64-darwin` 的 `nix develop`。当前 nixpkgs 已不再支持 `x86_64-darwin`，Intel Mac 请使用原生 .NET 10 SDK。

## 🗺️ Roadmap

详细的诊断范围、分析顺序与分阶段开发计划见 [`ROADMAP.md`](./ROADMAP.md)。

## 📰 Changelog

版本变更记录见 [`CHANGELOG.md`](./CHANGELOG.md)，文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 规范。

## 🤝 Contributing

我们欢迎通过 Issue 报告问题或提出建议，也接受 Pull Request。提交前请阅读 [`CONTRIBUTING.md`](./CONTRIBUTING.md)。

## 📜 License

xidio 以 [MIT License](./LICENSE) 开源。
