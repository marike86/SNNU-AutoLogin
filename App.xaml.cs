using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SNNUAutoLogin.Services;

namespace SNNUAutoLogin;

public partial class App : Application
{
    private AppRuntime? _runtime;
    private Mutex? _singleInstance;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            CrashLog("Xaml", e.Exception);
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog("AppDomain", e.ExceptionObject as Exception);
    }

    private static void CrashLog(string source, Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_crash.txt");
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        bool Has(string s) => argv.Any(a => a.Equals(s, StringComparison.OrdinalIgnoreCase));

        if (Has("--diag"))
        {
            Diagnostics.WriteReport();
            Exit();
            return;
        }

        // 单实例：两个进程同时认证会互相把对方踢下线
        _singleInstance = new Mutex(true, @"Local\SNNUAutoLogin.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在跑（它的窗口可能已经被释放）：广播唤醒，然后静默退出
            TrayIcon.BroadcastWake();
            Exit();
            return;
        }

        _runtime = new AppRuntime(DispatcherQueue.GetForCurrentThread(), launchedAtStartup: Has("--tray"));
        _runtime.AttachTray();

        // --tray（开机自启路径）：完全不创建窗口，进程只跑托盘 + 巡检
        if (!Has("--tray"))
            _runtime.ShowWindow();

        _runtime.Start();
    }
}
