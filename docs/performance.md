# 体积与内存记录

这些数据用于说明测量条件与实际占用，不是所有设备上的性能保证。Windows 工作集、macOS physical footprint 与 Android PSS 的统计口径不同，不能直接横向排名。

## macOS v1.0.0

来源：[双架构构建与验证](https://github.com/shixiang-liu/SNNU-AutoLogin/actions/runs/40517224607)，源码 `83401fc`，测量日期 2026-10-27。程序报告版本与 App 元数据均已核对为 **1.0.0**。

系统为 macOS 15.7.9，使用优化构建和模拟门户；每种场景预热 10 秒，再每 2 秒采样，持续 60 秒。下面是采样均值。

| 架构 | App 体积 | 前台物理占用 | 在线后台物理占用 | 等待网络时物理占用 |
| :--- | ---: | ---: | ---: | ---: |
| Apple Silicon | 1,265,079 字节 | 13.28 MiB | 13.39 MiB | 13.56 MiB |
| Intel | 1,313,239 字节 | 9.75 MiB | 9.70 MiB | 9.76 MiB |

App 体积指 `.app` 内文件总量，不是外层 ZIP 大小。在线后台平均 CPU 分别为单核的 0.127% 与 0.306%。

- [Apple Silicon 测量摘要](measurements/macos-v1.0.0-arm64.json)
- [Intel 测量摘要](measurements/macos-v1.0.0-x64.json)

云端验证不等同于真实校园网和 MacBook 合盖测试，也没有验证 macOS 13 实机。

## Android v1.0.0

正式 APK 为 **56,516 字节（55.2 KiB）**。一加 11 / Android 16 的优化测试构建，每组采样 30 次、间隔 2 秒：

| 场景 | PSS 中位数 | 最大值 |
| :--- | ---: | ---: |
| 前台页面可见 | 96.9 MiB | 97.3 MiB |
| 后台服务在线 | 23.9 MiB | 24.3 MiB |

测试构建与正式包使用独立包名。详细条件与尚待验证项目见 [Android 验证记录](../android/VALIDATION.md)。

## Windows 历史基线

[Windows v1.0.0 的测量说明](https://github.com/shixiang-liu/SNNU-AutoLogin/blob/v1.0.0/README.md)记录：Windows 11 x64，校园网在线、关闭窗口，60 秒内每 2 秒采样，工作集约 **14 MiB**、私有内存约 **2.6 MiB**。

这是 v1.0.0 的历史基线，不代表已重新测量 v1.0.1。当前 v1.0.1 发布 EXE 为 1,025,536 字节（约 0.98 MiB）。
