# AGENTS

## 项目简介
- 本项目 `xidio` 是一个面向西安电子科技大学（西电）校园网场景的网络诊断辅助工具。
- 当前核心目标是采集网络环境信息，帮助定位“无法上网 / 网络异常 / 路由或代理配置异常”等问题。
- 现阶段以 **Windows 端诊断** 为主，CLI 输出供人工排障使用。

## 代码结构
- `xidio/xidio.Core`：平台无关核心层。
- `xidio/xidio.Platform.Windows`：Windows 平台实现（WMI + WLAN API）。
- `xidio/xidio.Platform.macOS`：macOS 平台实现（system_profiler + networksetup + netstat + airport）。
- `xidio/xidio.CLI`：命令行入口与诊断报告输出（多平台构建：`net10.0-windows` / `net10.0`）。
- `xidio/xidio.slnx`：解决方案入口。

## 技术栈
- 语言：`C#`
- SDK / 框架：`.NET 10`（`net10.0` / `net10.0-windows`）
- 平台能力：
  - `System.Net.NetworkInformation`（网卡、IP、网关、DNS）
  - `System.Management`（WMI 查询驱动版本、路由、接口指标）
  - `wlanapi.dll` P/Invoke（SSID/BSSID 无线连接信息）
- 产物形态：CLI 可执行程序（启用 AOT 发布配置）

## 当前实现重点
- `NetworkDiagnosticsCollector` 负责聚合诊断数据并生成 `NetworkDiagnosticReport`。
- 通过 `IPlatformNetworkDiagnosticsProvider` 抽象平台差异；Windows 端由 `WindowsPlatformNetworkDiagnosticsProvider` 实现。
- 已包含虚拟网卡过滤逻辑（如 Hyper-V、VMware、WSL、WireGuard、Npcap 等关键字）。
- 输出包含：
  - 主网卡识别（有线 / 无线 / PPPoE）
  - IP、网关、DHCP、DNS、路由与接口 Metric
  - 网卡驱动版本
  - 系统代理状态

## 开发注意事项（给后续 AI Agents）
- 保持分层边界：
  - `xidio.Core` 不应依赖 Windows 专有 API。
  - 平台相关实现放在 `xidio.Platform.*`。
- 新增平台能力时，优先扩展 `IPlatformNetworkDiagnosticsProvider`，不要把平台分支直接写进 CLI。
- 维持“诊断优先、不中断”原则：
  - 采集失败时优先降级返回，不要因单点异常导致整体退出。
  - 参考现有 `SafeGet` 和 provider 内部异常兜底模式。
- 变更虚拟网卡过滤规则时，注意误杀真实网卡风险；需结合中英文设备名与常见驱动关键词。
- CLI 文案可调整，但尽量保持关键信息字段稳定，避免影响后续日志解析或人工比对。
- 当前仓库暂无测试项目；若引入复杂逻辑（分类、过滤、路由排序），建议补充单元测试。

## 常用命令
- 还原依赖：`dotnet restore xidio/xidio.slnx`
- 构建：`dotnet build xidio/xidio.slnx -c Release`
- 运行 CLI：`dotnet run --project xidio/xidio.CLI/xidio.CLI.csproj`
- 发布（示例）：`dotnet publish xidio/xidio.CLI/xidio.CLI.csproj -c Release`

## 现状与后续
- `xidio.Core/Repairs` 与 `xidio.Core/Reporting` 目录已预留，尚未落地代码。
- 后续可在“诊断结论 + 自动修复建议”方向扩展，但应先保证采集结果稳定、可复现。
