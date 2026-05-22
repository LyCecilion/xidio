# 项目整体架构

下面我们介绍 xidio 的项目整体架构。这包括代码架构、xidio 的诊断和修复功能的流程架构和上报流程。

## 代码架构

项目使用 C# 开发，SDK 基于 .NET 10（`net10.0` 和 `net10.0-windows`）。整个解决方案划分为以下模块：

- `xidio.Core`：xidio 的核心模块，封装了所有诊断、修复和上报逻辑，供 CLI 与 GUI 调用。
- `xidio.CLI`：xidio 的命令行版本，同时也可作为终端内的交互式工具使用。我们将使用 `System.CommandLine` 和 `Spectre.Console`。
- `xidio.GUI`：xidio 的图形界面版本，基于 Avalonia 框架实现跨平台功能。

### xidio 的调用能力和平台特性

为了维持项目的跨平台性，我们建议优先采用 .NET 提供的能力，在平台强关联的功能中再调用或封装操作系统提供的接口。

#### 通用层

我们可以使用 .NET 提供的：

- `System.Net.NetworkInformation.NetworkInterface`
- `IPGlobalProperties`
- `Ping`
- `Socket`
- `HttpClient`
- 自己实现 DNS UDP/TCP 查询，或者使用可靠 DNS 库
- JSON/YAML 规则配置

#### Windows

我们可以调用或封装：

```powershell
Get-NetIPConfiguration
Get-NetRoute
Get-NetAdapter
Get-NetAdapterStatistics
Get-DnsClientServerAddress
```

```cmd
ipconfig /all
route print
arp -a
netsh wlan show interfaces
netsh wlan show networks mode=bssid
netsh winhttp show proxy
rasdial
```

并获取来自 WLAN AutoConfig、DHCP Client、DNS Client 和 RasClient 的日志。不过在部分情况下，我们也可以使用 P/Invoke 进行 Win32 API 的调用。

#### Linux

我们可以调用或封装：

```bash
ip addr
ip route
ip neigh
resolvectl status
nmcli
iw dev wlan0 link
journalctl -u NetworkManager
```

#### macOS

我们可以调用或封装：

```bash
ifconfig
netstat -rn
route -n get default
arp -an
scutil --dns
networksetup
airport -I
```

## 诊断和修复功能的流程架构

xidio 的所有功能必须是分级的，因为每一级别的功能都会带来不同程度的影响。

### Drip

Drip 模式的所有操作要求是 read-only 的。在这一层中，xidio 仅收集数据和分析，不进行任何修改系统的修复。

### Drift

Drift 模式的 xidio 可以提供一些基础的修复能力。这些能力是可回滚且不会造成破坏的，且即使失败也基本不会让情况变差。这包括但不限于

1. 关闭失效代理。在系统代理开启、代理地址是本机或局域网地址、xidio 无法连接该代理端口、HTTP/HTTPS 访问失败、直连访问成功的情况下，xidio 支持关闭失效代理。
2. 刷新 DNS 缓存。
3. DHCP 重新获取地址。

### Current

Current 模式的 xidio 可以提供一些可回滚但可能有影响的修复，这些修复会修改系统网络配置，需要明确征求用户同意。这包括但不限于：

1. 修改 DNS。包括修改为自动设置 DNS 或设置为某个指定的 DNS。
2. 调整 metric。在有网络适配器有网络连接，但默认路由并没有经过该适配器时可用。
3. 临时禁用明显冲突的虚拟网卡。在它们持有默认路由或 DNS 指向它们等情况下可用。
4. 禁用 IPv6。一般地，我们并不推荐禁用 IPv6，但它在西电校园网环境中有奇效，尤其是当哔哩哔哩等网站过于卡顿时。

### Surge

Surge 模式的 xidio 可以进行侵入式的修复。这些修改可能造成很大的影响，因此必须要征得用户确认。这包括但不限于：

1. 重置 Winsock。
2. 重置 IP 协议栈。
3. 进行 Windows 网络重置。
4. 回滚、更新驱动或删除网卡设备。
5. 修改电源管理策略。如关闭「允许计算机关闭此设备以节约电源」。

上述的所有操作都必须留下日志。除 Surge 模式的操作外，其他模式的操作都必须是可回滚的。

## 上报流程

我们将上报分为三类。

- 第一类是崩溃/异常上报，包括 xidio 的崩溃情况、某个平台 provider 的异常、Avalonia UI 的未处理异常、某个 probe 的失败、版本、OS、堆栈等。这类上报将使用 Sentry 或 GlitchTip。
- 第二类是使用情况统计，包括 xidio 的实际使用人数、各个功能的使用比例、CLI 和 GUI 的使用占比、版本分布等。这类上报将使用 xidio 自定义的 collector。
- 第三类是校园网情况上报。

所有的上报都必须是 opt-in 的，且提供这样的选项：在每次上报之前由用户确认上报信息。
