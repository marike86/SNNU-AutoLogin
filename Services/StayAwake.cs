using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 「锁屏时不进入睡眠」的执行状态申领。
///
/// 为什么必须单开一个线程：<c>SetThreadExecutionState</c> 的状态是**按线程**记录的，
/// 线程一退出请求就失效。挂在 UI 线程上会在重建窗口 / 关闭内容树时被清掉，
/// 所以固定一个后台线程拿着它，只在需要时切换。
///
/// 只申领 <c>ES_SYSTEM_REQUIRED</c>（系统保持运行），**不碰显示器** ——
/// 锁屏后屏幕该灭还是灭，只是不进入睡眠，网络会话因此不会断。
/// </summary>
public sealed class StayAwake : IDisposable
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _started = new(false);

    private Thread? _thread;
    private bool _wanted;
    private volatile bool _active;
    private volatile bool _disposed;

    /// <summary>当前是否已向系统申领「保持运行」。</summary>
    public bool Active => _active;

    /// <summary>最近一次 <c>SetThreadExecutionState</c> 的返回值（0 表示失败），自检用。</summary>
    public uint LastResult { get; private set; }

    /// <summary>最近一次实际提交的标志位，自检用。</summary>
    public uint LastApplied { get; private set; }

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "SNNU.StayAwake"
        };
        _thread.Start();
        _started.Wait(1000);
    }

    /// <summary>申请 / 撤销「保持运行」。可在任意线程调用。</summary>
    public void Set(bool hold)
    {
        if (_disposed) return;

        lock (_gate) _wanted = hold;

        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// 睡眠唤醒后重新申领一次：执行状态请求在系统睡眠期间会被丢弃，
    /// 而本线程还以为自己拿着，需要强制回一次。
    /// </summary>
    public void Reassert()
    {
        if (_disposed) return;

        lock (_gate) _active = false;

        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    private void Loop()
    {
        _started.Set();

        while (true)
        {
            _signal.WaitOne();

            bool want;
            lock (_gate)
            {
                if (_disposed) break;
                want = _wanted;
            }

            if (want == _active) continue;

            uint flag = want ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS;
            uint result = SetThreadExecutionState(flag);
            LastResult = result;
            LastApplied = flag;

            if (result == 0)
            {
                // 系统拒绝（极少见）：保持原状态，下次 Set 再试
                continue;
            }

            _active = want;
        }

        // 线程退出前必须显式撤销，否则请求会跟着这个线程一直挂着
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _signal.Set();
        try { _thread?.Join(1200); } catch { }

        _signal.Dispose();
        _started.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);
}
