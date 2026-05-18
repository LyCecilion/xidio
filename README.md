<!--markdownlint-disable MD033 MD036 MD041-->

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

## Roadmap

### 由用户回答

- 当前位置：
  - 南校区 / 北校区
  - 宿舍楼 / 教学楼 / 图书馆 / 实验室
  - 楼栋、楼层，可选
- 当前连接方式：
  - Wi-Fi
  - 宿舍有线直连
  - 通过路由器
  - 手机热点
  - 不确定
- 现象：
  - 连不上 Wi-Fi
  - 连上 Wi-Fi 但无 Internet
  - 认证页不弹
  - 认证成功但打不开网页
  - QQ/微信/游戏/浏览器某个能用某个不能用
  - 有线拨号失败
  - 有线拨号成功但没网
- 是否别人也有问题：
  - 只有我
  - 同宿舍也有
  - 同楼层也有
  - 不清楚

### 系统与网卡基础信息

- 操作系统：
  - Windows / macOS / Linux
  - 版本号
  - xidio 版本
  - 是否管理员权限运行
- 系统时间：
  - 本机时间
  - 与网络时间/HTTPS Date 头的偏差  
  因为时间错了会导致 HTTPS、认证页面、证书校验异常。
- 所有网络接口：
  - 接口名称
  - 接口类型：Ethernet / Wi-Fi / PPP / VPN / Loopback / Virtual
  - 是否启用
  - 是否连接
  - MAC 地址，默认应脱敏或哈希
  - 链路速度
  - MTU
  - 接口 metric
  - 收发包统计、错误包、丢弃包
- 当前默认路由到底走哪块网卡

如果是 Wi-Fi：

- 当前是否已关联 Wi-Fi
- SSID
- BSSID，也就是 AP 的 MAC，建议默认脱敏
- 信号强度 RSSI / Signal Quality
- 频段：
  - 2.4GHz
  - 5GHz
  - 6GHz，如果有
- 信道
- PHY 类型：
  - 802.11n/ac/ax 等
- 认证/加密类型
- 连接时长
- 当前连接速率
- 可见的校园网 SSID 列表
- 当前 AP 是否是已知校园 AP
- 最近 Wi-Fi 连接失败原因码，能拿到的话

如果是 PPPoE：

- 以太网接口是否有 carrier：
  - 网线是否插上
  - 对端是否有链路
- 链路速度：
  - 10M/100M/1G
- 是否存在 PPPoE/宽带连接
- PPPoE 当前状态：
  - 未拨号
  - 正在连接
  - 已连接
  - 已断开
- PPPoE 分配到的：
  - IP
  - DNS
  - 默认路由
- Windows RAS 错误码：
  - 691：账号/密码/认证失败
  - 651：PPPoE 服务不可达/链路层问题/驱动问题
  - 678/718：远端无响应/超时
  - 720： 协 -连接关闭
- 宽带连接名称是否包含非 ASCII 字符
- 是否安装 Npcap/WinPcap

### IP、DHCP、DNS、路由、ARP

- IPv4 地址
- IPv6 地址
- 子网掩码/前缀长度
- 是否 DHCP 获取
- DHCP 服务器地址
- DHCP 租约开始/过期时间
- 默认网关
- DNS 服务器
- 路由表
- ARP 表 / IPv6 Neighbor 表
- 是否存在 169.254.x.x 地址
- 是否存在奇怪的静态 IP
- 是否同时存在多个默认路由
- 每条默认路由的 metric
- 当前访问校园服务器/公网服务器时实际走哪条路由

### 本机干扰因素

- 系统代理：
  - Windows Internet Options Proxy
  - WinHTTP Proxy
  - 环境变量 `HTTP_PROXY` / `HTTPS_PROXY`
  - macOS network proxy
- 是否开启 VPN/TUN/TAP：
  - Clash TUN
  - v2rayN
  - WireGuard
  - OpenVPN
  - ZeroTier
  - Tailscale
  - WARP
  - VMware/VirtualBox 虚拟网卡
  - WSL 虚拟网卡
- DNS-over-HTTPS / Secure DNS 状态，能检测多少算多少
- hosts 文件是否包含校园认证域名、常见网站域名
- 防火墙状态
- 安全软件/过滤驱动，建议只检测常见网络过滤组件，不要默认上传完整进程列表
- 浏览器代理设置，至少 Edge/Chrome 可以检测系统代理；浏览器独立 DoH 比较难，但可以提示用户

### 校园认证与业务状态

- 当前是否已认证
- 当前账号在线状态
- 当前设备是否在在线会话列表里
- 套餐是否生效
- 运营商/套餐类型
- 宽带优先级/出口优先级
- 是否欠费
- 是否达到设备数上限
- 是否存在异常在线设备
- 最近登录错误码
- 是否被强制下线
- 认证服务器是否可达

### 主动探测

- ARP 默认网关
- ICMP Ping，注意 ICMP 失败不能直接代表网络失败
- TCP Connect：
  - 80
  - 443
  - 53 TCP
- UDP DNS 查询
- UDP echo/jitter/loss 测试
- HTTP GET，不跟随重定向
- HTTPS 握手与证书校验
- DNS 解析：
  - 使用系统解析器
  - 直接向当前 DNS 服务器查询
  - 分别测 A/AAAA
- Traceroute/MTR，深度模式再跑
- MTU/PMTUD 测试，尤其 PPPoE 场景

### 分级探测

```text
本机网卡
  ↓
有线链路 / Wi-Fi 关联
  ↓
IP 获取：DHCP / PPPoE
  ↓
默认网关可达
  ↓
校园认证服务器可达
  ↓
认证状态正常
  ↓
DNS 正常
  ↓
公网 IP 可达
  ↓
公网域名可达
  ↓
具体协议：HTTP / HTTPS / UDP / 游戏 / 微信等
```
