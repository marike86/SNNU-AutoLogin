using System;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 网络状态监听：Wi-Fi 连上/断开、换网、从休眠唤醒后地址变化时，
/// 立刻叫醒巡检逻辑重试一次，而不是傻等到下一个轮询周期。
/// 事件在后台线程触发，统一切回 UI 线程并做去抖（同一波变化只触发一次）。
/// </summary>
public sealed class NetworkWatch : IDisposable
{
    private const int DebounceMs = 1500;

    private readonly DispatcherQueue _dispatcher;
    private readonly Action _onChanged;

    private int _cooldown;      // 1 = 去抖窗口内，已排队的触发会被丢弃
    private bool _started;
    private bool _disposed;

    public NetworkWatch(DispatcherQueue dispatcher, Action onChanged)
    {
        _dispatcher = dispatcher;
        _onChanged = onChanged;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
        }
        catch
        {
            // 某些受限环境不支持这些事件，退化为纯轮询即可
        }
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Schedule();

    private void OnAddressChanged(object? sender, EventArgs e) => Schedule();

    private void Schedule()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _cooldown, 1) == 1) return;

        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;

            try { _onChanged(); }
            finally
            {
                // 去抖窗口结束后才允许下一次触发（定时器跑在 UI 线程，无需加锁）
                var t = _dispatcher.CreateTimer();
                t.Interval = TimeSpan.FromMilliseconds(DebounceMs);
                t.IsRepeating = false;
                t.Tick += (_, _) =>
                {
                    Volatile.Write(ref _cooldown, 0);
                    t.Stop();
                };
                t.Start();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        }
        catch { }
    }
}
