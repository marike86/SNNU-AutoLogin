# 从源码构建

只想安装使用？请到 [下载页面](../README.md#下载) 获取 Windows 或 Android 版本。本页面向开发者。

## Windows

需要 Rust stable、Visual Studio Build Tools 的「使用 C++ 的桌面开发」组件，以及 Windows SDK。在仓库根目录运行：

```powershell
./scripts/package.ps1
```

脚本会检查格式、运行回归测试并生成发布包：

- `dist/SNNU-AutoLogin.exe`：可直接运行的单文件程序。
- `dist/SNNU-AutoLogin-v<版本>-windows-x64.zip`：程序、说明与许可证。
- `dist/SHA256SUMS.txt`：发布文件校验值。

只编译程序时可以运行：

```powershell
cargo build --release --locked --target x86_64-pc-windows-msvc
```

## Android

需要完整的 JDK 21、Android SDK Platform 37.0 和 Build Tools 36.0.0，无需安装 Android Studio。

```powershell
./scripts/android-package.ps1
```

默认生成经过裁剪优化的测试包，使用独立包名与测试签名。正式签名、Linux/macOS 构建及设备测试见 [Android 开发与验证](android-development.md)。发布密钥和密码不得写入仓库。

## 代码入口

| 路径 | 职责 |
| :--- | :--- |
| `src/` | Windows 原生界面、门户协议、后台与本地存储 |
| `android/app/src/main/` | Android 界面、服务、网络与账号存储 |
| `android/app/src/test/` | Android 离线回归测试 |
| `android/app/src/androidTest/` | Android 设备检查 |
| `scripts/` | 构建、打包与测量脚本 |
| `.github/workflows/` | 持续集成 |

提交改动时，请说明解决的问题与验证方式，并避免提交账号配置、签名密钥、日志或未脱敏截图。
