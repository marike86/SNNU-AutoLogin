using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace SNNUAutoLogin;

/// <summary>
/// 自建入口（取代 XAML 自动生成的 Main）。
/// 目的是让 <c>--clean-hosts</c> 这个提权子进程完全不碰 WinUI：
/// UAC 弹窗之后只做一次文件写入就退出，既快又不会闪窗口。
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int cleanIndex = Array.IndexOf(args, "--clean-hosts");
        if (cleanIndex >= 0)
        {
            return cleanIndex + 1 < args.Length
                ? Services.HostsCleanup.RunCleanMode(args[cleanIndex + 1])
                : 5;
        }

        // 开发自检：只算「清理后的 hosts 应该长什么样」，不落盘、不弹 UAC
        int selftestIndex = Array.IndexOf(args, "--hosts-selftest");
        if (selftestIndex >= 0)
        {
            return Services.HostsCleanup.SelfTest(selftestIndex + 1 < args.Length
                ? args[selftestIndex + 1]
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_hosts_selftest.txt"));
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
