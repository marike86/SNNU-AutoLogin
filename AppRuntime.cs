using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SNNUAutoLogin.Services;

namespace SNNUAutoLogin;

public sealed class LogEntry
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>
/// 进程级运行时：凭据、巡检状态机、托盘、日志缓冲全部在这里，与 UI 生命周期解耦。
/// 窗口只是一个可随时拆卸的视图 —— 关窗到托盘时把 XAML 内容树整个丢掉，
/// 工作集能从 ~160MB 掉到 11MB 左右，召回来再重新构建。
/// </summary>
public sealed class AppRuntime : IDisposable
{
    private const int MaxLogLines = 60;

    private readonly List<LogEntry> _logs = new(MaxLogLines + 4);
    private readonly object _logGate = new();

    private MainWindow? _window;
    private NetworkWatch? _netWatch;
    private bool _disposed;

    public static AppRuntime? Current { get; private set; }

    public CredentialStore Store { get; } = new();
    public ThemeColors Theme { get; }
    public LoginCoordinator Coordinator { get; }
    public DispatcherQueue Dispatcher { get; }
    public TrayIcon? Tray { get; private set; }

    public ConnState State { get; private set; } = ConnState.Idle;
    public LoginResult? LastResult { get; private set; }
    public bool Exiting { get; private set; }

    /// <summary>状态中文标题（托盘提示与窗口共用）。</summary>
    public string StateLabel { get; private set; } = "待机";

    public event Action<ConnState, LoginResult?>? StateChanged;
    public event Action<string>? Logged;

    /// <summary>托盘菜单里改了配置（窗口若开着需要回读）。</summary>
    public event Action? SettingsChanged;

    public AppRuntime(DispatcherQueue dispatcher, bool launchedAtStartup = false)
    {
        Current = this;
        Dispatcher = dispatcher;

        Store.Load();

        // 首次启动与便携目录移动后也要注册/修复；关闭时清除遗留项。
        if (!StartupManager.SetEnabled(Store.Settings.AutoStart))
        {
            Store.Settings.AutoStart = StartupManager.IsEnabled();
            Store.Save();
            AddLog("开机自启设置失败，请检查当前用户的启动项权限");
        }

        Theme = new ThemeColors(dispatcher);
        Theme.Changed += () => { };

        Coordinator = new LoginCoordinator(Store, dispatcher, launchedAtStartup);
        Coordinator.StateChanged += OnStateChanged;
        Coordinator.Log += AddLog;

        AddLog("程序启动");
        AddLog(Store.Settings.AutoStart && StartupManager.IsEnabled()
            ? "开机自启已注册"
            : "开机自启未启用");
    }

    public MainWindow? Window => _window;

    public IReadOnlyList<LogEntry> LogSnapshot
    {
        get
        {
            lock (_logGate) return _logs.ToArray();
        }
    }

    // ────────────────────────── 托盘 ──────────────────────────

    /// <summary>注册托盘图标；失败只记日志，不影响巡检。</summary>
    public void AttachTray()
    {
        if (Tray is not null) return;

        try
        {
            Tray = new TrayIcon();
            Tray.ShowRequested += () => Dispatcher.TryEnqueue(ShowWindow);
            Tray.CommandInvoked += () => Dispatcher.TryEnqueue(OnTrayCommand);

            bool ok = Tray.Show(MenuState(), Environment.ProcessPath ?? "");
            AddLog(ok ? "已驻留任务栏托盘" : "托盘图标注册失败（系统未接受）");
        }
        catch (Exception ex)
        {
            AddLog($"托盘图标创建失败：{ex.Message}");
        }
    }

    private TrayMenuState MenuState() => new()
    {
        AutoStart = Store.Settings.AutoStart,
        AutoReconnect = Store.Settings.AutoReconnect,
        MinimizeToTray = Store.Settings.MinimizeToTray,
        Tip = $"校园网自动登录 · {StateLabel}"
    };

    public void RefreshTray() => Tray?.UpdateState(MenuState());

    private void OnTrayCommand()
    {
        if (Tray is null) return;

        switch (Tray.LastCommand)
        {
            case TrayIcon.CmdLogin:
                _ = Coordinator.ManualConnectAsync();
                break;

            case TrayIcon.CmdToggleAutoStart:
                SetAutoStart(!Store.Settings.AutoStart);
                break;

            case TrayIcon.CmdToggleReconnect:
                Store.Settings.AutoReconnect = !Store.Settings.AutoReconnect;
                Store.Save();
                SettingsChanged?.Invoke();
                RefreshTray();
                break;

            case TrayIcon.CmdToggleTray:
                Store.Settings.MinimizeToTray = !Store.Settings.MinimizeToTray;
                Store.Save();
                SettingsChanged?.Invoke();
                RefreshTray();
                break;

            case TrayIcon.CmdExit:
                Exit();
                break;
        }
    }

    public void SetAutoStart(bool enabled)
    {
        if (StartupManager.SetEnabled(enabled))
            AddLog(enabled ? "开机自启已注册" : "开机自启已关闭");
        else
            AddLog("开机自启设置失败，请检查当前用户的启动项权限");

        Store.Settings.AutoStart = StartupManager.IsEnabled();
        Store.Save();
        SettingsChanged?.Invoke();
        RefreshTray();
    }

    // ────────────────────────── 巡检 ──────────────────────────

    public void Start()
    {
        Coordinator.Start();

        // 网络一通（重连 Wi-Fi、换网、休眠唤醒）就立刻补一次认证，不等下一个轮询周期
        _netWatch = new NetworkWatch(Dispatcher, () =>
        {
            AddLog("检测到网络状态变化，立即重新认证");
            Coordinator.RequestImmediateCheck();
        });
        _netWatch.Start();

        // 启动期的临时分配已无用，20 秒后归还一次未使用页面
        _ = Task.Delay(20000).ContinueWith(_ => MemoryOptimizer.Trim(force: true),
            TaskScheduler.Default);
    }

    private void OnStateChanged(ConnState state, LoginResult? result)
    {
        State = state;
        LastResult = result;
        StateLabel = LabelOf(state);

        Tray?.UpdateTip($"校园网自动登录 · {StateLabel}");
        StateChanged?.Invoke(state, result);
    }

    public static string LabelOf(ConnState s) => s switch
    {
        ConnState.Online => "已连接",
        ConnState.Connecting => "正在登录",
        ConnState.Offline => "未登录",
        ConnState.Failed => "登录失败",
        ConnState.Checking => "正在检测",
        _ => "待机"
    };

    public void AddLog(string text)
    {
        var entry = new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Text = text };

        lock (_logGate)
        {
            _logs.Add(entry);
            if (_logs.Count > MaxLogLines) _logs.RemoveRange(0, _logs.Count - MaxLogLines);
        }

        Logged?.Invoke(text);
    }

    // ────────────────────────── 窗口生命周期 ──────────────────────────

    /// <summary>显示主窗口；窗口内容树已被释放时重新构建。</summary>
    public void ShowWindow()
    {
        if (_disposed || Exiting) return;

        if (_window is null)
        {
            _window = new MainWindow(this);
        }

        _window.RestoreFromTray();
    }

    /// <summary>窗口把它自己销毁了（内容树已释放），通知运行时。</summary>
    public void OnWindowTornDown(MainWindow window)
    {
        if (ReferenceEquals(_window, window)) _window = null;
    }

    public void Exit()
    {
        if (Exiting) return;
        Exiting = true;

        _window?.PersistToStore();

        Tray?.Dispose();
        Tray = null;

        try { Application.Current.Exit(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _netWatch?.Dispose();
        _netWatch = null;

        Coordinator.Dispose();
        Theme.Dispose();
        Tray?.Dispose();
        Tray = null;
    }
}
