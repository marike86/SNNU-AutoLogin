<div align="center">

<h1>SNNU 校园网自动登录</h1>

<p><strong>连接学校网络，剩下的交给它。</strong></p>

<p>为陕西师范大学校园网准备的轻量工具，支持校园网、移动、联通三种上网方式。</p>

<p>
  <a href="https://github.com/shixiang-liu/SNNU-AutoLogin/releases/tag/v1.0.1"><img src="https://img.shields.io/badge/Windows-v1.0.1-2563eb?style=flat-square" alt="Windows v1.0.1"></a>
  <a href="https://github.com/shixiang-liu/SNNU-AutoLogin/releases/tag/android-v1.0.0"><img src="https://img.shields.io/badge/Android-v1.0.0-16a34a?style=flat-square" alt="Android v1.0.0"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-64748b?style=flat-square" alt="MIT License"></a>
</p>

<p>
  <a href="#下载"><strong>下载</strong></a> ·
  <a href="#界面预览">界面预览</a> ·
  <a href="#开始使用">开始使用</a> ·
  <a href="android/README.md">Android 使用指南</a> ·
  <a href="https://github.com/shixiang-liu/SNNU-AutoLogin/issues">反馈问题</a>
</p>

</div>

## 下载

| 平台 | 系统要求 | 下载 |
| :--- | :--- | :--- |
| **Windows** | Windows 10 / 11，x64 | [免安装版 ZIP](https://github.com/shixiang-liu/SNNU-AutoLogin/releases/download/v1.0.1/SNNU-AutoLogin-v1.0.1-windows-x64.zip) · [单文件 EXE](https://github.com/shixiang-liu/SNNU-AutoLogin/releases/download/v1.0.1/SNNU-AutoLogin.exe) |
| **Android** | Android 8.0 及以上 | [安装包 APK](https://github.com/shixiang-liu/SNNU-AutoLogin/releases/download/android-v1.0.0/SNNU-AutoLogin-android.apk) |

Windows 约 **1 MB**，Android 安装包不到 **60 KB**。[查看更新记录 →](https://github.com/shixiang-liu/SNNU-AutoLogin/releases)

## 界面预览

| Windows | Android |
| :---: | :---: |
| <img src="docs/screenshots/home.png" width="320" alt="Windows 首页：连接状态与当前账号"> | <img src="docs/images/android/home.png" width="240" alt="Android 首页：连接状态与当前账号"> |

[Windows 设置预览](docs/screenshots/settings.png) · [更多 Android 界面](android/README.md#界面预览)

## 日常少操心

- **自动登录**：保存一次账号，连上学校网络后自动认证，省去反复打开网页。
- **断线恢复**：网络变化后自动检查，连接异常时尝试恢复；遇到账号问题会暂缓重试。
- **原生轻量**：Windows 托盘运行，Android 通知栏快捷操作，没有广告或统计 SDK。
- **账号留在本机**：密码加密保存，不经过开发者服务器，不会自动替换其他账号的在线连接。

## 开始使用

1. 下载对应版本，在系统中连接学校 Wi-Fi 或有线网络。
2. 打开工具，填写学号、校园网密码和上网方式，点击「保存并连接」。
3. 保持自动登录开启，关闭界面后仍可在后台使用。

Android 首次使用可按提示允许通知；需要锁屏后持续运行时，到「设置 → 后台与通知」检查后台权限。详细操作见 [Android 使用指南](android/README.md)。

## 文档与反馈

| 我想…… | 去这里 |
| :--- | :--- |
| 了解 Android 操作与后台设置 | [Android 使用指南](android/README.md) |
| 从源码构建或参与开发 | [构建说明](docs/building.md) |
| 查看 Android 测试与性能记录 | [验证记录](android/VALIDATION.md) |
| 反馈问题或提出建议 | [GitHub Issues](https://github.com/shixiang-liu/SNNU-AutoLogin/issues) · [邮件](mailto:shixiang.liu@foxmail.com) |

反馈时请附上系统版本、客户端版本和复现步骤，也可以附脱敏诊断报告。请勿上传密码或完整配置文件。

## 数据与隐私

账号密码只用于学校认证，保存在本机；不同客户端需要分别填写。学校认证门户使用 HTTP，**本机密码加密不代表网络传输加密**。

诊断报告会隐藏账号，不包含密码；分享前仍请检查 IP、路径等个人信息。

## 许可证

[MIT License](LICENSE)。本项目是面向陕西师范大学校园网的社区工具，与学校官方客户端无隶属关系。
