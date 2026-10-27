# 版本与分支约定

## 一个主分支，三个客户端

`main` 保存三个平台当前维护的源码。Windows 与 macOS 共享 `src/` 中的 Rust 核心，macOS 系统适配位于 `src/macos/`；Android 位于独立的 `android/` 目录。

不长期维护 `windows`、`macos`、`android` 三套平行主分支，避免共享协议修复在平台之间遗漏。

## 按功能开发，按平台发布

- 功能或修复在短期分支上进行，例如 `feature/notification`、`fix/portal-detection`，完成后压缩为完整提交，合入 `main`。
- 合并并验证后删除临时分支，分支名说明用途。
- Windows 沿用 `v<版本>` 标签；macOS 使用 `macos-v<版本>`；Android 使用 `android-v<版本>`。各平台可以独立迭代版本。
- 已发布标签与安装包保持可追溯，不用开发过程中的零碎提交充当发布记录。

构建和验证命令见 [构建说明](docs/building.md)。提交中应说明解决的问题与验证结果，不包含账号配置、签名密钥或未脱敏日志。

版本来源：Windows 使用 `Cargo.toml` 的包版本；macOS 使用 `src/macos/mod.rs` 的 `VERSION`，打包时直接读取程序报告的版本；Android 使用 `android/app/build.gradle`。共享代码不要求三个客户端使用同一个版本号。
