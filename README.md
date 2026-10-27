# 陕师大校园网自动登录

连接学校网络，剩下的交给它

面向陕西师范大学校园网的 Windows 小工具，提供自动登录、掉线重连和托盘运行。Rust + 原生 Win32 界面，免安装，双击即可运行

**v1.0.1 · Windows 10 / 11 x64 · MIT 开源**

[下载最新版](https://github.com/shixiang-liu/SNNU-AutoLogin/releases/latest) · [反馈问题](https://github.com/shixiang-liu/SNNU-AutoLogin/issues)

## 轻巧，日常少操心

- **约 1 MB 的单文件程序**：EXE 为 1,025,536 字节，约 0.98 MiB
- **后台占用低**：v1.0.0 在本机已连接、关闭界面时，工作集约 14 MiB，私有内存约 2.6 MiB；本版尚未重新测量
- **免安装绿色版**：解压运行，不需要管理员权限，不安装服务或驱动
- **无需额外运行库**：VC++ 运行库静态链接，不依赖 Electron、WebView2、Java 或 .NET
- **默认在本机处理账号**：密码使用 Windows DPAPI 加密保存，发布文件不包含预设账号
- **界面保持简单**：首页一个按钮控制连接，关闭窗口后继续在托盘运行

内存数据来自 Windows 11 x64 上 60 秒的采样（每 2 秒一次，共 30 次），实际占用随环境和运行状态变化。

「绿色版」指免安装：保存账号、记录日志和开启开机自启动时，仍会写入当前用户的数据目录或启动项。

## 界面预览

以下为软件实际界面的原始尺寸截图，学号、密码区域和 IP 已使用不透明遮盖处理

| 主页 | 设置 |
| --- | --- |
| ![主页](docs/screenshots/home.png) | ![设置](docs/screenshots/settings.png) |

<details>
<summary>查看账号设置、功能介绍和网络信息</summary>

| 功能介绍 | 网络信息 |
| --- | --- |
| ![功能介绍](docs/screenshots/features.png) | ![网络信息](docs/screenshots/network.png) |

![账号设置（隐私已遮挡）](docs/screenshots/account.png)

</details>

## 开始使用

1. 从 [Releases](https://github.com/shixiang-liu/SNNU-AutoLogin/releases/latest) 下载 Windows x64 发布包
2. 解压到固定位置，双击「校园网自动登录.exe」
3. 填写学号、校园网密码和上网方式，点击「保存并连接」

每位用户首次使用都需要保存自己的账号密码。若校园网网页已登录，程序可能识别当前在线学号，但不会读取网页密码。主页会显示当前 Wi-Fi 的真实名称，有线连接显示「有线网络」。

| 操作 | 效果 |
| --- | --- |
| 首页圆形按钮：连接 | 连接校园网认证，同时开启自动登录 |
| 首页圆形按钮：断开 | 断开校园网认证，同时关闭自动登录；Wi-Fi 保持连接 |
| 设置 → 自动登录 | 决定是否自动登录与重连；关闭后保留当前连接 |
| 设置 → 开机自动运行 | 决定是否随 Windows 启动后台 |
| 关闭窗口 | 关闭界面，后台继续运行 |
| 单击托盘图标 | 打开界面 |
| 托盘右键 → 退出程序 | 退出界面与后台，保留当前网络连接 |

## 功能

| 功能 | 说明 |
| --- | --- |
| 自动登录 | 连接学校网络后自动完成认证 |
| 开机运行 | 随 Windows 启动后台，可在设置中关闭 |
| 自动重连 | 掉线、换网和休眠唤醒后检查连接，按网络情况重新认证 |
| 连接保活 | 定时发送轻量请求，维持连接活动 |
| 异常处理 | 失败后退避重试，账号异常时暂缓提交，网络修复设有冷却时间 |
| 认证地址自动识别 | 认证地址变化时重新寻找并验证门户 |
| 密码加密保存 | 使用 Windows DPAPI 在当前用户下加密保存密码 |
| 托盘运行 | 单击打开，右键进行快捷操作，关闭窗口不退出后台 |
| 诊断报告 | 查看网络信息，导出账号脱敏的连接信息与最近日志 |

界面支持高 DPI、键盘操作、加载反馈与长内容滚动。自动修复网络前会再次确认在线账号和上网方式，避免自动修复影响其他账号。

## 数据与隐私

账号和运行数据保存在当前 Windows 用户目录，不写入 EXE

```text
%APPDATA%\SNNU-AutoLogin\config.json   账号和加密后的密码
%APPDATA%\SNNU-AutoLogin\state.json    运行状态与认证地址缓存
%APPDATA%\SNNU-AutoLogin\login.log     运行日志，超过 200 KB 自动轮转
```

密码使用 Windows DPAPI 加密，账号密码仅提交给学校认证门户。联网检测会访问小米、微软的公开检测地址，不携带账号密码。诊断报告不包含密码，账号会脱敏；分享前请检查其中的 IP、文件路径等信息。

如需移除程序，先关闭「开机自动运行」，从托盘退出，再删除程序文件。如需一并清空本机记录，可在退出后删除上述数据目录。

## 从源码编译

需要 Windows、Rust stable 的 `x86_64-pc-windows-msvc` 工具链，以及 Visual Studio Build Tools 的「使用 C++ 的桌面开发」组件和 Windows SDK

```powershell
cargo build --release --locked
```

产物为 `target/x86_64-pc-windows-msvc/release/snnulogin.exe`。项目默认构建 Windows x64，并静态链接 VC++ 运行库。编译脚本会自动寻找 Windows SDK 中的资源编译器；自定义安装可用 `RC` 环境变量指定 `rc.exe` 路径。版本由 `Cargo.toml` 生成，界面和 EXE 文件属性保持一致。

检查和打包：

```powershell
cargo fmt --all -- --check
cargo test --release --locked
./scripts/package.ps1
```

打包脚本会执行格式检查、回归测试和发布构建，并生成：

- `dist/SNNU-AutoLogin-v1.0.1-windows-x64.zip`：程序、README 使用说明和许可证
- `dist/SNNU-AutoLogin.exe`：独立可执行文件
- `dist/SHA256SUMS.txt`：文件校验值

发布构建将本机编译目录映射为通用路径，不嵌入开发者的目录信息。GitHub Actions 在推送和 Pull Request 时执行检查并上传构建产物。`Cargo.lock` 纳入版本管理，构建产物、本机配置、日志和编辑器数据均已忽略。

## 项目结构

```text
src/
  win32_main.rs    程序入口、命令行、诊断导出
  desktop_ui.rs    原生 Win32 界面与交互
  ui_model.rs      状态文案与主要操作
  network.rs       Windows Wi-Fi 和有线网络信息
  portal.rs        门户探测、认证、地址发现与错误归类
  agent.rs         后台巡检、重连、保活和控制接口
  config.rs        配置、DPAPI 加密与原子写入
  process_lock.rs  跨进程互斥
  autostart.rs     当前用户的开机运行设置
  notification.rs 通知状态与过期控制
  tray.rs         Windows 托盘与快捷操作
  tray_model.rs   托盘事件与菜单模型
scripts/          检查、打包与第三方许可证收集
docs/screenshots/ 界面预览截图
.cargo/           Windows x64 与静态运行库构建设置
.github/          Windows 持续集成
app.rc            Windows 图标与版本资源模板
app.manifest      系统兼容与 DPI 声明
build.rs          SDK 发现与资源编译
```

调试构建支持离线界面预览，不修改真实账号配置或校园网认证

```powershell
cargo run -- --preview online
cargo run -- --preview warning --preview-page help
cargo run -- --preview features --preview-dpi 192
```

## 反馈与适用范围

本项目针对陕西师范大学的 Dr.COM 校园网门户，不是通用的校园网客户端。首次公开版本为 1.0.0，后续根据用户反馈维护。

可以通过 [GitHub Issues](https://github.com/shixiang-liu/SNNU-AutoLogin/issues) 或反馈邮箱 [shixiang.liu@foxmail.com](mailto:shixiang.liu@foxmail.com) 联系作者。

反馈时请附程序版本、Windows 版本、复现步骤和预期结果。连接异常可在「设置 → 网络信息」导出诊断报告，请勿提交密码或完整配置文件。命令行自检可使用 `snnulogin.exe --doctor`，本地输出包含账号，公开分享时请先脱敏。

## 许可证

[MIT License](LICENSE)

本项目是社区工具，与学校官方客户端无隶属关系

## v1.0.1 更新

修复开启 FlClash 等虚拟网卡后，校园网已经登录却显示“等待校园网”的问题。认证请求自动绑定当前物理网络，未开启代理时同样使用；不修改系统代理或路由。账号配置继续保留。详细测试与多网卡边界见 [验证记录](docs/windows-network-fix-validation.md)。
