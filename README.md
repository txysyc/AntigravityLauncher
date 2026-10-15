<div align="center">

# Antigravity Proxy Launcher

**开启系统代理，照常使用 Antigravity。**

面向 Windows 的轻量代理启动器 · 单文件 EXE · 无需开启 TUN

<p>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-v0.1.0-2563eb?style=flat-square" alt="Version v0.1.0"></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078d4?style=flat-square" alt="Windows">
  <img src="https://img.shields.io/badge/runtime-.NET_Framework_4.5%2B-512bd4?style=flat-square" alt=".NET Framework 4.5+">
  <a href="LICENSE"><img src="https://img.shields.io/badge/code_license-MIT-16a34a?style=flat-square" alt="Code license: MIT"></a>
</p>

[快速开始](#快速开始) · [编译](#编译) · [使用说明](#使用说明) · [支持范围](#支持范围) · [许可证](#许可证)

</div>

---

启动器读取 Windows 当前用户的系统代理，为 Antigravity 及其子进程设置代理环境变量与 Electron 启动参数。配置好快捷方式后，仍然通过熟悉的应用入口启动。

本项目是独立社区工具，与 Google 无隶属或背书关系。Antigravity 需单独安装。

## 特性

- **单文件运行**：C# 与系统框架库实现，运行时只需一个 EXE。
- **跟随系统代理**：自动读取静态 HTTP/mixed 代理；关闭系统代理时使用直连。
- **进程环境隔离**：代理变量仅由新启动的 Antigravity 及其子进程继承。
- **保留日常操作**：转交应用参数，已有实例运行时唤起原窗口。
- **按需维护**：提供代理预检、JSON 报告，以及快捷方式备份与恢复。
- **本地图标设置**：从已安装的 Antigravity 读取图标，为启动器 EXE 设置图标。

## 快速开始

### 1. 准备启动器

自行[编译](#编译)，或从本仓库的 Releases 直接下载 `AntigravityLauncher.exe`，无需解压。

将启动器放到 Antigravity 安装目录，与原程序同级：

~~~text
Antigravity 安装目录/
├── Antigravity.exe
├── AntigravityLauncher.exe
└── ...
~~~

### 2. 修改快捷方式

在原快捷方式的「属性」中，将「目标」指向 `AntigravityLauncher.exe`。快捷方式的图标可继续引用本机的 `Antigravity.exe`。

也可以直接双击启动器，或使用下文的[快捷方式管理](#快捷方式管理)命令。

### 3. 开启系统代理并启动

在代理客户端中开启普通系统代理，再点击快捷方式。无需开启 TUN，也无需手动设置环境变量或执行脚本。

新启动时读取当前代理配置。应用已运行时保留原会话；代理地址变化后，完整退出 Antigravity 再启动，以应用新配置。

## 环境要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 或更高版本 |
| 运行环境 | .NET Framework 4.5 或更高版本 |
| 目标应用 | 已安装 Antigravity 桌面应用 |
| 代理入口 | HTTP 或 mixed 端口 |

## 编译

在项目根目录打开 PowerShell：

~~~powershell
New-Item -ItemType Directory -Path dist -Force | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" "@build.rsp"
~~~

输出：`dist/AntigravityLauncher.exe`。

`build.rsp` 使用相对路径引用源码和许可文件。编译器路径通过 Windows 环境变量确定；若系统只安装了 32 位框架，可将 `Framework64` 改为 `Framework`。

编译时将版本信息、`LICENSE` 和 `THIRD_PARTY_NOTICES.md` 嵌入 EXE。默认发行版不包含 Antigravity 原版图标。运行时无需携带源码、编译配置、独立图标、脚本或额外 DLL。

GitHub Releases 直接提供单个 EXE，无需打包为 ZIP。许可与第三方声明已内嵌，源码仓库中也保留相应文件；可另附独立校验文件供下载者核验。

## 使用说明

默认双击启动即可。以下参数用于自定义配置或排查问题：

| 参数 | 说明 |
| --- | --- |
| `--check` | 检查应用位置、代理配置和端口连通性，不启动应用 |
| `--proxy-port <port>` | 使用本机指定的 HTTP/mixed 代理端口 |
| `--proxy-url <url>` | 使用指定 HTTP 代理地址 |
| `--executable <path>` | 指定 Antigravity 程序路径 |
| `--report <path>` | 将操作结果写入指定 JSON 文件 |
| `--install-shortcuts` | 接入当前用户已有的 Antigravity 快捷方式 |
| `--restore-shortcuts` | 从备份恢复原快捷方式 |
| `--use-app-icon` | 从本机 Antigravity 读取图标，设置到启动器 EXE 本身 |
| `-- <arguments>` | 将后续参数转交给 Antigravity |

路径参数支持相对当前工作目录的路径。`--proxy-port` 与 `--proxy-url` 互斥；检查、安装快捷方式、恢复快捷方式和图标设置四种操作互斥。图标设置不接受代理或应用启动参数。

<details>
<summary><strong>命令行示例</strong></summary>

以下命令在 EXE 所在目录执行，`7890` 为示例端口，请按实际代理配置修改。

~~~powershell
# 检查配置，并输出诊断报告
.\AntigravityLauncher.exe --check --report ".\reports\check.json"

# 指定本机代理端口
.\AntigravityLauncher.exe --proxy-port 7890

# 指定代理地址
.\AntigravityLauncher.exe --proxy-url http://127.0.0.1:7890

# 指定应用位置
.\AntigravityLauncher.exe --executable ".\custom-install\Antigravity.exe"

# 转交应用启动参数
.\AntigravityLauncher.exe -- --user-data-dir ".\test-profile"
~~~

程序为 GUI 类型，无控制台输出。默认启动不生成日志或配置文件；需要诊断时使用 `--report`，报告会包含应用路径、代理地址及相关错误信息。

</details>

### 设置启动器本身的图标

在启动器所在目录执行一次：

~~~powershell
.\AntigravityLauncher.exe --use-app-icon
~~~

启动器自动查找本机 Antigravity，以资源方式读取其多尺寸图标，将图标写入启动器副本，再由临时 C# 进程在原进程退出后替换文件。完成后显示结果并清理临时文件。此操作仅设置图标，不启动 Antigravity。

自定义安装位置可配合 `--executable`；需要自动化处理时可配合 `--report`。图标设置通过辅助进程异步完成，报告中的 `action` 为 `icon-updated` 时表示设置成功。

设置时需要启动器所在目录的写入权限；如目录不可写，可先在可写目录完成设置，再放回安装目录。替换会改变 EXE 的哈希；校验发布文件时请使用未定制的版本。资源管理器可能需要刷新后才显示新图标。

图标仅在用户本机读取，仓库和默认发行版不分发原版图标。定制后的 EXE 若再次分发，仍需自行确认图标的适用许可。恢复默认图标可使用原始发布文件替换启动器。

### 快捷方式管理

在准备长期存放启动器的位置执行：

~~~powershell
.\AntigravityLauncher.exe --install-shortcuts
~~~

程序查找当前用户已有的 `Antigravity.lnk`，将其指向启动器，并保留图标、工作目录和任务栏识别信息。目标不属于原应用或当前启动器的链接会跳过；包含自定义参数的快捷方式需手动处理。

原快捷方式保存在 EXE 旁的 `integration/backups` 中，清单及 SHA256 校验值保存在 `integration/shortcuts.json`。这些文件用于维护和恢复，普通启动不依赖它们。

恢复原入口：

~~~powershell
.\AntigravityLauncher.exe --restore-shortcuts
~~~

保留 `integration` 目录即可保留恢复能力。如果应用更新重建了原快捷方式，可重新执行安装操作；程序会备份当前版本并保留历史备份。移动启动器前先恢复原入口，再在新位置重新接入。

## 支持范围

| 场景 | 支持情况 |
| --- | --- |
| Windows 静态 HTTP/mixed 系统代理 | 支持 |
| 单一代理地址、分协议 `http=...;https=...` 配置 | 支持 |
| IPv4、IPv6、代理主机名 | 支持 |
| 系统代理关闭时直连 | 支持 |
| PAC 自动配置、代理认证、SOCKS-only 入口 | 暂不支持 |
| WSL 后端 | 暂不支持 |

自定义网络客户端及应用启动的工具是否遵循代理配置，取决于它们自身的实现。Antigravity 后续版本的网络实现发生变化时，可能需要调整启动器。

## 工作原理

启动器依次完成应用定位、代理读取与端口预检，再为子进程构造配置：

~~~text
Windows 系统代理
       │
       ▼
AntigravityLauncher.exe
       ├── HTTP_PROXY / HTTPS_PROXY / NO_PROXY / NODE_USE_ENV_PROXY
       └── Electron --proxy-server / --proxy-bypass-list
                │
                ▼
        Antigravity 及其子进程
~~~

应用查找顺序为：启动器所在目录、Windows 标准用户/程序目录，最后查询 App Paths 和卸载注册表。可用 `--executable` 显式指定路径。

HTTPS 请求通过 HTTP 代理的 CONNECT 隧道传输，因此代理入口地址仍使用 `http://`。界面与本地服务之间的 loopback 连接保持直连。支持环境代理的 Node 运行时使用 `NODE_USE_ENV_PROXY=1`。

复杂绕过规则在不同网络栈中不能完全等价转换；启动器尽量转换域名规则，并保留 loopback 直连。系统代理关闭时，会清理子进程继承的旧代理变量。

## 开发

~~~text
.
├── src/
│   └── DesktopLauncher.cs   # 启动逻辑及版本信息
├── tests/
│   ├── LauncherTests.cs
│   └── compiler.rsp
├── build.rsp
├── CHANGELOG.md
├── LICENSE
├── THIRD_PARTY_NOTICES.md
└── README.md
~~~

编译并运行测试：

~~~powershell
New-Item -ItemType Directory -Path build -Force | Out-Null
New-Item -ItemType Directory -Path dist -Force | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" "@build.rsp"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" "@tests\compiler.rsp"
.\build\LauncherTests.exe
~~~

测试覆盖代理地址解析、绕过规则、环境变量隔离、直连时的旧代理清理、Windows 参数转义、本地 TCP 预检，以及 EXE 图标替换与临时文件清理。图标测试使用 Windows 自带资源和临时启动器副本，不启动 Antigravity。`dist`、`build`、运行报告及快捷方式备份均为生成文件，已加入 `.gitignore`。

欢迎通过 Issue 报告问题或提交 Pull Request。反馈时请附上启动器版本、Antigravity 版本、代理类型及经过隐私处理的诊断报告。

## 版本约定

采用 [SemVer](https://semver.org/) 风格的 `主版本.次版本.修订版本`，Git 标签加 `v` 前缀。更新内容见 [CHANGELOG.md](CHANGELOG.md)。

| 版本示例 | 本项目约定 |
| --- | --- |
| `v0.1.0` | 首个公开版本，处于初期迭代阶段 |
| `v0.1.1` | 兼容性修复或问题修复 |
| `v0.2.0` | 新增功能，或初期阶段的行为调整 |
| `v1.0.0` | 命令行接口与主要使用行为形成稳定约定 |

初期 `0.x` 阶段可能调整接口，发布时会在更新记录中说明。版本号独立于 Antigravity 自身版本。

## 许可证

本项目原创源码、测试、编译配置和文档采用 [MIT License](LICENSE)。

仓库和默认发行版不包含 Antigravity 原版图标。用户可在本机通过 `--use-app-icon` 设置图标，或让快捷方式引用已安装的原程序。原版图标和相关商标的权利归原权利方所有，**不属于本项目的 MIT 授权范围**；本项目不授予额外的图标使用或再分发许可。

完整来源与权利说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。Antigravity 的使用另受其[官方条款](https://antigravity.google/terms)约束。
