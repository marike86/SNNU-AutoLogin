using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 后台驻留优化：把托管堆与工作集还给系统。
/// WinUI 3 应用仅 XAML/Composition 子系统就有数十 MB 基线，
/// 最小化后不再需要这些页面驻留物理内存，及时 Trim 可显著降低"后台占用"。
/// </summary>
public static class MemoryOptimizer
{
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static DateTime _lastTrim = DateTime.MinValue;

    /// <summary>归还未引用的托管内存并修剪工作集。最小间隔 15 秒，避免频繁 page fault。</summary>
    public static void Trim(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastTrim).TotalSeconds < 15) return;
        _lastTrim = now;

        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            EmptyWorkingSet(GetCurrentProcess());
        }
        catch
        {
            // 修剪失败不影响主流程
        }
    }

    /// <summary>当前进程内存快照，用于日志。</summary>
    public static string Snapshot()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            return $"工作集 {p.WorkingSet64 / 1048576.0:F0} MB · 私有 {p.PrivateMemorySize64 / 1048576.0:F0} MB · 线程 {p.Threads.Count}";
        }
        catch
        {
            return "内存信息不可用";
        }
    }
}
