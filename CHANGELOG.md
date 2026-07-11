# Changelog

本项目的所有重要变更都将记录在此文件中。

本文档格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，项目遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

## [0.1.1] - 2026-07-11

### Added

- 增加 Linux 平台 Provider，支持系统与权限、物理网卡与驱动、Wi-Fi、DHCP、路由、metric、默认路由和邻居表信息收集。
- 增加 Nix flake、锁文件与 direnv 集成，为 Linux 开发环境提供 .NET 10、`iproute2`、NetworkManager 和 `iw`。
- 增加 Roadmap、Contributing、MIT License 与完整的项目文档。
- 增加项目图标与 Logo 的 PNG 资源。

### Changed

- 重写 `AGENTS.md`，将开发路线与诊断范围迁移至 `ROADMAP.md`。
- 根据当前 nixpkgs 支持范围，Nix devShell 支持 `x86_64-linux`、`aarch64-linux` 和 `aarch64-darwin`。

### Fixed

- 修复未征得用户同意即收集系统代理信息的隐私问题。
- 修复 CLI 原样显示 MAC、BSSID 与邻居链路层地址的问题，默认隐藏设备标识字节。

## [0.1.0] - 2026-05-22

### Added

- 增加交互式 CLI，用于收集用户场景并展示网络诊断报告。
- 增加 `xidio.Core` 诊断抽象、结构化模型、进度报告和可取消的信息收集流程。
- 增加 Windows 平台网络诊断 Provider，覆盖网络接口、地址、网关、DHCP、DNS、metric、路由和 Wi-Fi 信息。
- 增加 macOS 平台网络诊断 Provider 的初始实现。
- 增加项目 Logo、图标与 Figma 设计源文件。
- 增加 CI、标签发布工作流与 Nerdbank.GitVersioning 版本管理。

[0.1.0]: https://github.com/LyCecilion/xidio/releases/tag/v0.1.0
[0.1.1]: https://github.com/LyCecilion/xidio/compare/v0.1.0...v0.1.1
[Unreleased]: https://github.com/LyCecilion/xidio/compare/v0.1.1...HEAD
