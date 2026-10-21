<p align="center">
  <img src="Assets/app_256.png" width="96" alt="SNNUAutoLogin-GUI 图标">
</p>

<h1 align="center">SNNUAutoLogin-GUI</h1>

<p align="center">
  陕西师范大学校园网自动认证客户端 · 原生 WinUI 3 桌面程序
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%202004%2B%20%7C%2011-0078D4">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4">
  <img alt="WinUI" src="https://img.shields.io/badge/WinUI-3%20%2F%20Windows%20App%20SDK%201.8-0067C0">
  <img alt="Self-contained" src="https://img.shields.io/badge/self--contained-%E6%97%A0%E9%9C%80%E8%BF%90%E8%A1%8C%E5%BA%93-2E7D32">
  <img alt="Version" src="https://img.shields.io/badge/version-1.2.0-orange">
</p>

<p align="center">
  <img src="docs/ui-preview.png" width="880" alt="主界面">
</p>

连上校园网就要手动打开浏览器、输账号、点登录，一天几次；断线了还得再来一遍。这个小工具把这件事接管了：**开机静默驻留托盘，检测到没认证就自动登录，掉线自动补上**，全程没有浏览器、没有 Electron，界面走系统原生的 WinUI 3。

> 非官方项目，与陕西师范大学及网络信息中心无关。仅用于个人设备接入校园网，请遵守学校网络使用规定。

## 特性

- **后台常驻，几乎不占资源** — 关窗口不退出，收进托盘继续守候。窗口可见约 37 MB，收进托盘约 17 MB，开机静默（`--tray`，从不创建窗口）约 9 MB
- **开机自动登录** — 开机后不弹窗，直接静默驻留托盘，检测出口未认证即提交账号
- **断线自动重连** — 已连接时按设定间隔巡检；Wi-Fi 重连、换网、休眠唤醒时**立即**补一次认证（监听 `NetworkChange`，1.5 秒去抖），不等下一轮
- **失败不硬敲** — 连续失败按 10 → 20 → 40 → 80 → 120 秒指数退避；没填账号、关掉自动重连、或不在校园网时放慢到 30~60 秒一次，不会每 3 秒去敲一次 Portal
- **出口可选** — 校园网直连 / 中国移动 / 中国联通 / 中国电信（对应 Portal 的 `yys` 参数）
- **密码本地加密** — Windows DPAPI（当前用户作用域）加密后落盘，磁盘上不是明文；程序不上传任何数据到第三方
- **系统原生观感** — Mica 背板 + 系统强调色 + Fluent 语义色，跟随系统深浅色与强调色实时变化；Composition 动效，无第三方 UI 库
- **覆盖升级不丢配置** — 双击新版安装包即可，安装前自动结束托盘里的旧实例，账号密码与开关原样保留，自启路径自动纠正
- **完全自包含** — .NET 运行时、Windows App SDK、WinUI 全部随包，目标机不需要装 .NET、Windows App Runtime 或 VC++ 运行库

## 截图

| 深色模式 | 未登录 | 登录失败 |
| --- | --- | --- |
| ![深色](docs/ui-preview-dark.png) | ![未登录](docs/state-offline.png) | ![失败](docs/state-failed.png) |

> 截图里的学号与出口 IP 已做模糊处理。

## 下载与安装

前往 [Releases](../../releases/latest) 下载最新安装包 `SNNUAutoLogin-Setup-1.2.1.exe`，双击按向导下一步即可（向导全中文）。

便携版下载 `SNNUAutoLogin-Portable-1.2.1-win-x64.zip`，**完整解压到固定目录**后运行 `SNNUAutoLogin.exe`，无需安装运行时。不要只复制 exe；其余 DLL 和资源必须保留。移动目录后手动运行一次，程序会把开机启动项更新到新位置。凭据仍按下文保存在当前用户的 LocalAppData 中。

| 项目 | 说明 |
| --- | --- |
| 安装位置 | 默认 `%LOCALAPPDATA%\Programs\SNNUAutoLogin`，**当前用户安装、不弹 UAC** |
| 为所有用户安装 | 向导首屏可切换，此时装进 `Program Files` 并请求管理员权限 |
| 快捷方式 | 开始菜单（程序 + 卸载），可选桌面快捷方式 |
| 卸载 | 设置 → 应用，或开始菜单「卸载 陕师大校园网自动登录」 |
| 系统要求 | 64 位 Windows 10 2004（19041）及以上 / Windows 11 |
| 安装包体积 | 42.3 MB（展开后约 163 MB） |

安装程序会做这几件事：

1. 检测到旧版本还在托盘驻留时先结束它（这一步早于版本检查，不会弹「请先关闭程序」）
2. 按 AppId 识别旧版本，覆盖升级时保留账号、密码与全部设置
3. 把开机自启项改指到安装目录（旧版本若是便携版，不会再启动那个旧路径）
4. 写入标准卸载信息，在「应用和功能」里正常显示名称、版本、发布者
5. 卸载时同时清除开机自启项与 `%LOCALAPPDATA%\SNNUAutoLogin` 下的凭据

### 覆盖更新

直接双击新版本安装包，**不需要先卸载**：

| 场景 | 行为 |
| --- | --- |
| 程序正在托盘驻留 | 安装前自动结束（约 0.1 秒），继续安装 |
| 账号、密码、开关 | 原样保留，不用重填 |
| 安装目录 | 沿用旧目录，不会装出两份 |
| 开机自启 | 路径自动指向新安装位置 |
| 静默升级（`/VERYSILENT`） | 若升级前程序在运行，装完自动拉起 |

> 安装包未做代码签名，首次运行可能被 Windows SmartScreen 拦一下，点「更多信息 → 仍要运行」即可。

## 使用

1. 从开始菜单启动
2. 填写**学号**（不需要带 `@mobile` 后缀，带了也能自动识别出口）与**密码**
3. 选择网络出口，点「立即登录」验证一次
4. 打开「开机自动登录」，完成

### 托盘

点右上角关闭按钮不会退出，而是收进通知区域（可在「自动化」里关掉「关闭时驻留托盘」恢复原行为）。

| 托盘菜单 | 说明 |
| --- | --- |
| 显示主窗口 | 双击图标等效 |
| 立即登录 | 不打开窗口直接发一次认证 |
| 开机自动登录 / 断线自动重连 / 关闭时驻留托盘 | 与界面开关同步 |
| 退出 | 真正结束进程 |

开机自启写入 `"...\SNNUAutoLogin.exe" --tray`，开机不弹窗，直接静默驻留。

开关变更立即写入当前用户的启动项，无需管理员权限；写入失败会记录日志并回退开关。首次运行会按保存的设置注册，目录移动或旧启动命令缺少 `--tray` 时会修复。Windows 登录用户后才会启动程序；若在任务管理器中禁用了此启动项，需要先手动重新启用。

「开机自动登录」与「断线自动重连」分别控制：开机登录在网络就绪后完成一次认证（失败按退避策略重试）；认证成功后是否继续自动补登录由自动重连开关控制。账号和密码需先填写保存。

## 隐私与数据

- 程序只会向学校 Portal（`202.117.144.205:8603`）提交账号、密码与出口类型，**没有遥测、没有第三方上报、没有联网检查更新**
- 密码用 Windows DPAPI 以当前用户密钥加密，只有同一台机器的同一账户能解开
- 设置与凭据都在本机，卸载时会一并删除

| 内容 | 路径 |
| --- | --- |
| 设置 | `%LOCALAPPDATA%\SNNUAutoLogin\settings.json` |
| 加密密码 | `%LOCALAPPDATA%\SNNUAutoLogin\secret.bin` |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `SNNUAutoLogin` |

## 资源占用

界面与后台是彻底分开的两层：`AppRuntime`（凭据、巡检状态机、托盘、日志环形缓冲）与 `MainWindow`（纯视图）。窗口可以被完整拆掉再重建，后台不受影响。

1. 点关闭 → 先把界面上的值落盘，`Content = null` 丢弃整棵 XAML 树，并把 `x:Name` 生成的控件字段全部置空
2. 强制 GC（含大对象堆压缩）+ `EmptyWorkingSet`
3. 从托盘召回 → 复位 `_contentLoaded` 后重新 `InitializeComponent()`，按运行时状态回填设置、日志、连接状态
4. 开机自启带 `--tray`：一个窗口都不创建，只有托盘 + 巡检在跑

实测（.NET 8 / Windows App SDK 1.8，x64，175% 缩放）：

| 场景 | 工作集 |
| --- | --- |
| 刚启动（窗口可见，20 秒后自动 Trim） | ~166 MB |
| 窗口可见（稳定） | **~37 MB** |
| 关窗收进托盘 | **~17 MB** |
| 开机自启 `--tray`（始终无窗口） | **~9 MB** |
| 私有提交 | ~110 MB（.NET + WinAppSDK 固定底子，Trim 动不了） |

> 任务管理器的「内存」列显示的是工作集。Trim 只换出物理页面、不释放提交内存，切回窗口时是毫秒级换页。

## 工作原理

- **认证端点**：`http://202.117.144.205:8603/snnuportal/`
- **登录**：`POST application/x-www-form-urlencoded`，表单字段 `account` / `password` / `yys`（`mobile` / `unicom` / `telecom`），自动跟随 302
- **状态判定**：请求 `userstatus.jsp`，页面含「当前登录账号」即视为在线；页面是 GB2312，客户端按 GB18030 解码并做 UTF-8 回退
- **认证按出口 IP 绑定**：所以每次登录前先探测，已在线就直接跳过，不会重复提交
- **遇到图形验证码**：程序会明确提示先用浏览器登录一次

## 构建

依赖：.NET 8 SDK、Windows App SDK 1.8（NuGet 自动还原）。

```bat
git clone https://github.com/<your-name>/SNNUAutoLogin-GUI.git
cd SNNUAutoLogin-GUI
dotnet build -c Debug -p:Platform=x64
```

发布便携版（自包含）：

运行 `powershell -File tools/build_portable.ps1` 会生成 `artifacts/SNNUAutoLogin-Portable-<版本>-win-x64.zip` 及 SHA256 校验文件。安装 Inno Setup 6 后运行 `powershell -File tools/build_release.ps1`，会用同一份自包含发布目录同时生成安装版 EXE、便携版 ZIP 和 `SHA256SUMS.txt`。GitHub Actions 的 Windows release build 会运行启动项、登录及凭据回归检查，验证安装、启动、卸载，再上传两种包。也可以只发布目录：

```bat
dotnet publish -c Release -r win-x64 -p:Platform=x64 --self-contained -o release
```

打安装包（需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)：`winget install JRSoftware.InnoSetup`）：

```bat
dotnet publish -c Release -r win-x64 -p:Platform=x64 --self-contained -o release
cd installer && build_installer.bat
```

产物：`installer\Output\SNNUAutoLogin-Setup-<版本>.exe`。

升级版本时改两处：`SNNUAutoLogin.csproj` 的 `<Version>` 与 `installer/SNNUAutoLogin.iss` 的 `#define AppVer`。
**`AppId` 一旦发布就不要改**，否则新版本会被当成另一个程序（无法覆盖升级、残留旧卸载项）。

### 自包含是怎么做到的

产物里 exe 内嵌了 946 条 `activatableClass` 清单（自包含部署的 WinRT 激活表），因此不需要目标机预装任何运行时：

```bat
python tools/check_deps.py release
```

逐个解析发布目录里 256 个 PE 文件的导入表，把依赖分成「随包 / 系统自带 / 缺失」三类。当前结论：所有非系统依赖都在发布目录内。

发布时还会剔除用不到的组件（Windows ML 的 `onnxruntime` + `DirectML` 约 39 MB、桌面小组件、.NET 崩溃转储、WebView2 运行时），体积 210 MB → 160 MB。

## 项目结构

```
├─ App.xaml(.cs)          应用入口，解析 --tray / --diag
├─ AppRuntime.cs          后台：凭据、巡检状态机、托盘、日志缓冲、网络监听
├─ MainWindow.xaml(.cs)   纯视图：状态卡片、账号设置、自动化开关、日志
├─ MainWindow.Dev.cs      开发期自检钩子（出图 / 托盘生命周期 / 内存下沉）
├─ MainWindow.UiTest.cs   下拉框逐项切换与命中测试
├─ Fx.cs                  Composition 入场动画与按压回弹
├─ Services/
│  ├─ PortalClient.cs         Portal 协议：探测、登录、解码
│  ├─ LoginCoordinator.cs     巡检调度：间隔、退避、手动登录、立即唤醒
│  ├─ NetworkWatch.cs         网络事件监听 → 触发即时重连
│  ├─ CredentialStore.cs      设置读写 + DPAPI 加密密码（含内存缓存）
│  ├─ StartupManager.cs       开机自启注册与路径自愈
│  ├─ TrayIcon.cs             纯 Win32 Shell_NotifyIcon 托盘
│  ├─ WindowCloseHook.cs      子类化拦 WM_CLOSE，实现关窗转驻留
│  ├─ MemoryOptimizer.cs      GC + EmptyWorkingSet
│  ├─ ThemeColors.cs          系统强调色与语义色跟随
│  └─ Diagnostics.cs          只读诊断输出
├─ installer/             Inno Setup 打包脚本 + 全中文语言文件
├─ tools/check_deps.py    PE 导入表解析，验证自包含
├─ tools/png_redact.py    README 截图的马赛克脱敏工具（纯标准库）
└─ docs/                  界面截图
```

## 排障

```bat
SNNUAutoLogin.exe --diag
```

只读检测 Portal 可达性与出口认证状态，结果写入 `%TEMP%\snnu_diag.txt`，不改动任何认证状态。
程序异常退出时，堆栈写到 `%TEMP%\snnu_crash.txt`。

开发期自检（发布版同样可用）：

| 参数 | 作用 |
| --- | --- |
| `--snapshot` | 整窗渲染到 `%TEMP%\snnu_shot.png`，几何与主题色写入 `snnu_geom.txt` |
| `--dark` | 配合 `--snapshot` 强制深色主题 |
| `--ui-test` | 下拉框逐项切换 + 命中测试 + 各状态出图 |
| `--test-close` | 模拟点关闭 → 应转驻留托盘（释放内容树）→ 再召回重建 |
| `--test-tray` | 连做 3 轮「藏 ↔ 唤」，输出每轮内存与重建后控件状态 |
| `--test-exit` | 在托盘驻留状态下退出，验证进程真结束、无残留图标 |
| `--op=N` | 强制指定出口（0 校园网 / 1 移动 / 2 联通 / 3 电信） |

## 常见问题

**会把我的账号密码传到别处吗？**
不会。只有一个目的地：学校 Portal。密码加密存在本机，代码全部开源可查。

**为什么点关闭程序还在？**
设计如此：关窗只是收进托盘，这样断线时才能自动重连。不想要就到「自动化」里关掉「关闭时驻留托盘」，或托盘右键退出。

**不在校园网（家里宽带 / 手机热点）会怎样？**
Portal 探测失败，巡检自动放慢到最多 60 秒一次，不会刷请求；回到校园网后靠网络事件监听立即接管。

**提示需要图形验证码？**
Portal 偶发要求验证码，本程序不做验证码识别。用浏览器手动登录一次即可，之后正常。

**Windows 10 能用吗？**
能。Mica 背板只在 Windows 11 生效，Win10 上自动退化为纯色背景，功能不受影响。

**托盘图标不见了？**
Windows 会把不常用图标折叠进「^」溢出区，拖出来固定一下即可。

## 更新记录

### 1.2.1

- 修复首次运行未注册开机启动、便携版移动目录后误认旧路径、启动项缺少托盘参数的问题。
- 主窗口与托盘共用启动项写入和失败回退逻辑，避免显示已开启但实际注册失败。
- 开机自动登录不再依赖自动重连开关；网络未就绪或认证失败时继续退避重试。
- 增加 Windows 回归测试与便携版 ZIP / SHA256 构建脚本。
- 修复凭据保存失败后不重试的问题；写入失败显示日志，设置与密码改为临时文件写完后再替换，避免直接截断原文件。
- 对无效账号、出口枚举和巡检间隔配置做恢复处理；托盘注册失败时保留可访问窗口，重复后台启动不唤出主窗口。
- 安装版与便携版从同一目录构建，CI 验证安装、启动与卸载；安装升级也会修复同路径但缺少 `--tray` 的启动命令。

### 1.2.0

- **巡检不再空转**：没填账号、关掉自动重连时由每 3 秒一次放慢到 30 秒一次，并在日志里说明原因（同一原因只记一条）
- **失败指数退避**：10 → 20 → 40 → 80 → 120 秒封顶；Portal 不可达最多 60 秒一次
- **网络恢复即重连**：新增 `NetworkWatch`，连上 Wi-Fi、换网、唤醒后立刻补一次认证
- **少做无用功**：密码改为内存缓存（原来每轮巡检都做一次 DPAPI 解密 + 读盘）；设置与密码内容没变就不写盘；手动刷新不再和后台巡检抢锁
- **修掉的小问题**：关于页版本号改为读程序集（原来写死 `1.0.0`）；学号/密码为空时点登录直接聚焦输入框
- **安装包**：安装前结束托盘实例（`InitializeSetup` 早于 AppMutex 检查），静默升级后自动拉起，自启路径自动纠正

### 1.1.0

首个安装包：用户级安装（免 UAC）、全中文向导、覆盖升级保留配置、卸载清理干净。

## 致谢

界面规范参考 Windows 11 系统应用；托盘与关窗拦截走纯 Win32（WinUI 3 的 `AppWindow.Closing` 在程序内 `Close()` 时不触发，只能子类化拦 `WM_CLOSE`）。

---

> 本项目仅面向陕西师范大学校园网 Portal 的认证流程。其他学校 Portal 的字段与判定逻辑不同，需要自行改写 `Services/PortalClient.cs`。
