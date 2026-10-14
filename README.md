# Antigravity Proxy Launcher

用于 Windows 的 Antigravity 代理启动器。启动时读取系统代理，为 Antigravity 的界面和语言服务器配置代理，使应用能够通过本地 HTTP/mixed 代理使用，无需开启 TUN。

运行时只需一个 `AntigravityLauncher.exe`。代理端口、用户名和安装路径由运行环境确定。

## 功能

- 读取 Windows 当前用户的静态系统代理。
- 为新进程设置 `HTTP_PROXY`、`HTTPS_PROXY`、`NO_PROXY` 和 Electron 代理参数。
- 代理环境变量仅由 Antigravity 及其子进程继承。
- 保留界面与本地语言服务器之间的 loopback 直连。
- 系统代理关闭时使用直连；已有实例运行时唤起原窗口。
- 自动查找应用安装位置，支持显式指定路径和转交启动参数。
- 接入、备份和恢复当前用户的桌面、开始菜单及同名固定快捷方式。
- 后台启动，发生错误时显示提示；支持按需输出诊断报告。

## 环境要求

- Windows 10 或更高版本。
- .NET Framework 4.5 或更高版本。
- 已安装 Antigravity 桌面应用。
- 需要代理时，准备可用的 HTTP 或 mixed 代理端口。

程序采用 C# 和系统框架库实现，运行时无需携带脚本、源码、编译配置或额外 DLL。

## 快速开始

1. 获取或编译 `dist/AntigravityLauncher.exe`。
2. 将 EXE 放到 Antigravity 安装目录，与 `Antigravity.exe` 同级。
3. 在代理客户端中开启普通系统代理。
4. 将原快捷方式的目标改为 `AntigravityLauncher.exe`，图标继续使用原 Antigravity 图标。
5. 点击快捷方式启动应用。

也可以直接双击启动器。应用查找顺序为：启动器所在目录、Windows 标准用户/程序目录，最后查询 App Paths 和卸载注册表中的安装信息。

新启动时读取当前代理配置。应用已运行时保留原会话；代理地址变化后，在下次完整启动时应用新配置。

## 编译

在项目根目录打开 PowerShell，执行：

~~~powershell
New-Item -ItemType Directory -Path dist -Force | Out-Null

& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" "@build.rsp"
~~~

编译结果为 `dist/AntigravityLauncher.exe`。`build.rsp` 使用相对路径定义源码和输出位置；编译器位置通过 Windows 环境变量确定。

源码和编译配置仅用于开发。部署时复制编译生成的 EXE 即可。

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `--check` | 检查应用位置、代理配置和端口连通性，不启动应用。 |
| `--proxy-port <port>` | 使用本机指定的 HTTP/mixed 代理端口。 |
| `--proxy-url <url>` | 使用指定 HTTP 代理地址。 |
| `--executable <path>` | 指定 Antigravity 程序路径，支持相对当前工作目录的路径。 |
| `--report <path>` | 将操作结果写入指定 JSON 文件。 |
| `--install-shortcuts` | 接入当前用户已有的 Antigravity 快捷方式。 |
| `--restore-shortcuts` | 从备份恢复原快捷方式。 |
| `-- <arguments>` | 将后续参数转交给 Antigravity。 |

`--proxy-port` 与 `--proxy-url` 互斥。检查、安装快捷方式、恢复快捷方式三种操作互斥。

以下示例在 EXE 所在目录运行：

~~~powershell
# 检查当前系统代理并输出报告
.\AntigravityLauncher.exe --check --report ".\reports\check.json"

# 指定本机代理端口，7890 为示例值
.\AntigravityLauncher.exe --proxy-port 7890

# 使用指定代理地址
.\AntigravityLauncher.exe --proxy-url http://127.0.0.1:7890

# 指定自定义安装位置
.\AntigravityLauncher.exe --executable ".\custom-install\Antigravity.exe"

# 转交应用启动参数
.\AntigravityLauncher.exe -- --user-data-dir ".\test-profile"
~~~

默认启动过程不生成日志或配置文件。指定 `--report` 后，报告包含操作结果、应用路径、代理地址和相关错误信息。程序为 GUI 类型，无控制台输出；需要命令行诊断时使用报告参数。

## 快捷方式管理

在准备长期存放启动器的位置执行：

~~~powershell
.\AntigravityLauncher.exe --install-shortcuts
~~~

程序查找当前用户已有的 `Antigravity.lnk`，将其指向启动器，并保留应用图标、工作目录和任务栏识别信息。目标不属于原应用或当前启动器的链接会跳过；包含自定义参数的快捷方式需手动处理。

原快捷方式保存在 EXE 旁的 `integration/backups` 中，清单与 SHA256 校验值保存在 `integration/shortcuts.json`。这些文件用于维护和恢复，普通启动不依赖它们。

恢复原入口：

~~~powershell
.\AntigravityLauncher.exe --restore-shortcuts
~~~

保留 `integration` 目录即可保留恢复能力。如果应用更新重建了原快捷方式，可重新执行安装操作；程序会备份当前版本并保留历史备份。

快捷方式引用接入时的 EXE 位置。移动启动器前先恢复原入口，再在新位置重新接入。手动修改或新固定的任务栏入口应指向启动器。

## 代理机制

启动器将 Windows 静态系统代理转换为子进程配置：

~~~text
HTTP_PROXY=<HTTP 代理地址>
HTTPS_PROXY=<HTTPS 请求使用的代理地址>
NO_PROXY=localhost,127.0.0.1,::1,<可转换的绕过域名>
~~~

HTTPS 请求通过 HTTP 代理的 CONNECT 隧道传输，因此代理入口地址仍使用 `http://`。Electron 网络请求另外通过 `--proxy-server` 与 `--proxy-bypass-list` 配置。

支持这些设置的 Node 运行时使用 `NODE_USE_ENV_PROXY=1`。复杂绕过规则在 Go、Node 和 Chromium 中不能完全等价转换；域名绕过项会尽量转换，loopback 始终保留直连。

## 支持范围

- 支持 Windows 静态 HTTP/mixed 系统代理，以及显式指定的 HTTP 代理地址。
- 支持单一代理地址和分协议的 `http=...;https=...` 配置。
- 支持 IPv4、IPv6 和代理主机名。
- PAC、代理认证、SOCKS-only 入口、WSL 后端尚未支持。
- 自定义网络客户端及应用启动的工具是否遵循代理配置，取决于它们自身的实现。

## 开发与测试

项目结构：

~~~text
.
├── src/
│   └── DesktopLauncher.cs
├── tests/
│   ├── LauncherTests.cs
│   └── compiler.rsp
├── dist/                    # 编译生成的分发文件
├── build.rsp
├── .gitignore
└── README.md
~~~

测试覆盖代理地址解析、绕过规则、环境变量隔离、直连时的旧代理清理、Windows 参数转义和本地 TCP 预检。

在项目根目录编译并运行测试：

~~~powershell
New-Item -ItemType Directory -Path build -Force | Out-Null

& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" "@tests\compiler.rsp"

.\build\LauncherTests.exe
~~~

`dist`、`build`、运行报告及快捷方式备份属于生成文件，已加入 `.gitignore`。
