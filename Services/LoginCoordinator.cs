using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace SNNUAutoLogin.Services;

public enum ConnState
{
    Idle,
    Checking,
    Online,
    Offline,
    Connecting,
    Failed
}

/// <summary>连接状态机：定时巡检出口认证状态，掉线自动重新登录，开机网络未就绪时指数退避。</summary>
public sealed class LoginCoordinator : IDisposable
{
    private const int IdleDelaySeconds = 30;      // 没配账号 / 关了自动重连时的慢速巡检
    private const int PortalDownDelayMax = 60;    // Portal 不可达（多半不在校园网）
    private const int FailureDelayMax = 120;      // 登录一直失败时的封顶间隔

    private readonly PortalClient _client = new();
    private readonly CredentialStore _store;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private bool _suppressAuto;
    private int _failures;
    private bool _portalDown;
    private string? _idleReason;
    private int _pendingCheck;
    private TaskCompletionSource<object?>? _wake;

    public ConnState State { get; private set; } = ConnState.Idle;
    public LoginResult? LastResult { get; private set; }
    public DateTime? LastCheckAt { get; private set; }

    /// <summary>当前巡检节奏（秒），供界面显示。</summary>
    public int CurrentDelaySeconds { get; private set; } = 8;

    public event Action<ConnState, LoginResult?>? StateChanged;
    public event Action<string>? Log;

    public LoginCoordinator(CredentialStore store, DispatcherQueue dispatcher)
    {
        _store = store;
        _dispatcher = dispatcher;
    }

    /// <summary>自动认证的前提条件是否齐备（账号、密码、自动重连）。</summary>
    private bool AutoReady =>
        _store.Settings.AutoReconnect
        && !string.IsNullOrWhiteSpace(_store.Settings.Account)
        && _store.HasPassword;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _failures = 0;
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void SuppressAuto(bool suppress) => _suppressAuto = suppress;

    /// <summary>
    /// 请求立刻巡检一次（网络恢复、用户手动刷新时用）。
    /// 正在睡的那一轮会被提前叫醒，不用等到下一轮。
    /// </summary>
    public void RequestImmediateCheck()
    {
        Interlocked.Exchange(ref _pendingCheck, 1);
        Volatile.Read(ref _wake)?.TrySetResult(null);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        bool first = true;

        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var (ok, portalDown) = await TickAsync(ct).ConfigureAwait(false);
                _portalDown = portalDown;
                _failures = ok ? 0 : _failures + 1;

                delay = NextDelay(first);
                first = false;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogLine($"巡检异常：{ex.Message}");
                delay = TimeSpan.FromSeconds(15);
            }

            if (!await SleepAsync(delay, ct).ConfigureAwait(false)) return;
        }
    }

    /// <summary>
    /// 下一轮间隔。原则：能用才快，不能用就慢 —— 避免无意义地反复请求 Portal。
    /// </summary>
    private TimeSpan NextDelay(bool first)
    {
        TimeSpan delay;

        if (State == ConnState.Online)
        {
            delay = TimeSpan.FromSeconds(Math.Max(5, _store.Settings.CheckIntervalSeconds));
        }
        else if (!AutoReady)
        {
            // 账号/密码没填、或用户关掉了自动重连：没必要每 3 秒去敲一次 Portal
            delay = TimeSpan.FromSeconds(IdleDelaySeconds);
        }
        else if (_failures <= 0)
        {
            delay = TimeSpan.FromSeconds(first ? 2 : 10);
        }
        else if (_portalDown)
        {
            delay = TimeSpan.FromSeconds(Math.Min(PortalDownDelayMax, 10 * _failures));
        }
        else
        {
            // 登录失败（密码错、被锁、欠费…）：10 → 20 → 40 → 80 → 120 指数退避
            delay = TimeSpan.FromSeconds(Math.Min(FailureDelayMax,
                10 * Math.Pow(2, Math.Min(4, _failures - 1))));
        }

        CurrentDelaySeconds = (int)delay.TotalSeconds;
        return delay;
    }

    /// <summary>
    /// 睡到下一轮；期间若有人 RequestImmediateCheck 会被立刻唤醒。
    /// 返回 false 表示已取消（退出）。
    /// </summary>
    private async Task<bool> SleepAsync(TimeSpan delay, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _pendingCheck, 0) == 1) return true;
        if (delay <= TimeSpan.Zero) return true;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _wake, tcs);

        // 发布唤醒点之后再查一次：避免「刚检测完 pending、还没挂上 tcs」这一拍丢信号
        if (Interlocked.Exchange(ref _pendingCheck, 0) == 1)
        {
            Volatile.Write(ref _wake, null);
            return true;
        }

        try
        {
            var delayTask = Task.Delay(delay, ct);
            var done = await Task.WhenAny(delayTask, tcs.Task).ConfigureAwait(false);

            if (done == tcs.Task) LogLine("网络状态变化，立即重新检测");
            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Volatile.Write(ref _wake, null);
        }
    }

    /// <summary>一次完整巡检：查状态 → 必要时登录。返回 (是否正常, Portal 是否不可达)。</summary>
    private async Task<(bool ok, bool portalDown)> TickAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var status = await _client.GetStatusAsync(ct).ConfigureAwait(false);
            LastCheckAt = DateTime.Now;

            if (status.Success)
            {
                NoteIdleReason(null);
                SetState(ConnState.Online, status);
                return (true, false);
            }

            bool portalDown = status.Message.Contains("Portal", StringComparison.Ordinal)
                              || status.Message.Contains("超时", StringComparison.Ordinal);

            if (portalDown)
            {
                NoteIdleReason("portal");
                SetState(ConnState.Failed, status);
                return (false, true);
            }

            // 离线：是否允许自动登录
            var account = _store.Settings.Account;
            var password = _store.LoadPassword();

            if (_suppressAuto || !_store.Settings.AutoReconnect
                || string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                NoteIdleReason("idle");
                SetState(ConnState.Offline, status);
                return (true, false);
            }

            NoteIdleReason(null);
            SetState(ConnState.Connecting, status);
            LogLine("检测到未认证，正在自动登录…");

            var r = await _client.LoginAsync(account, password, _store.Settings.Operator, ct).ConfigureAwait(false);
            if (r.Success)
            {
                SetState(ConnState.Online, r);
                LogLine($"登录成功 · {r.Account ?? account} · {r.Ip ?? "IP 未知"}");
                return (true, false);
            }

            SetState(ConnState.Failed, r);
            LogLine($"登录失败：{r.Message}");
            return (false, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>同一个原因只写一条日志，避免慢速巡检把日志刷满。</summary>
    private void NoteIdleReason(string? reason)
    {
        if (_idleReason == reason) return;
        _idleReason = reason;

        if (reason == "idle")
            LogLine($"未配置账号/密码或已关闭自动重连，巡检放慢到 {IdleDelaySeconds} 秒一次");
        else if (reason == "portal")
            LogLine("认证门户不可达（可能不在校园网），已放慢巡检");
    }

    /// <summary>用户手动点击登录/重连。</summary>
    public async Task ManualConnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var account = _store.Settings.Account;
            var password = _store.LoadPassword();

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                Push(ConnState.Offline, LoginResult.Fail("请先填写账号与密码"));
                return;
            }

            SetState(ConnState.Connecting, null);
            LogLine("正在登录…");

            var r = await _client.LoginAsync(account, password, _store.Settings.Operator).ConfigureAwait(false);
            if (r.Success)
            {
                _failures = 0;
                SetState(ConnState.Online, r);
                LogLine(r.AlreadyOnline ? "当前已在线" : $"登录成功 · {r.Account ?? account} · {r.Ip ?? "IP 未知"}");
            }
            else
            {
                SetState(ConnState.Failed, r);
                LogLine($"登录失败：{r.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>配置变更后立即重新巡检一次。</summary>
    public async Task RefreshAsync()
    {
        // Wait(0) 是原子的：巡检正忙时不再插一脚，改为让下一轮提前跑
        if (!_gate.Wait(0))
        {
            RequestImmediateCheck();
            return;
        }
        _gate.Release();

        try
        {
            await TickAsync(_cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private void SetState(ConnState state, LoginResult? result)
    {
        State = state;
        LastResult = result;
        Push(state, result);
    }

    private void Push(ConnState state, LoginResult? result)
    {
        _dispatcher.TryEnqueue(() => StateChanged?.Invoke(state, result));
    }

    private void LogLine(string text) => _dispatcher.TryEnqueue(() => Log?.Invoke(text));

    public void Dispose()
    {
        Stop();

        // 唤醒可能还挂在 SleepAsync 上等消息的循环，避免带着已释放的 gate 继续跑
        Volatile.Read(ref _wake)?.TrySetResult(null);

        _client.Dispose();
        _gate.Dispose();
    }
}
