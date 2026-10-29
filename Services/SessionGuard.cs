using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 监听「锁屏 / 解锁 / 从睡眠恢复」。
///
/// 用一个独立线程上的隐藏顶层窗口接 WTS 会话通知，而不是挂在 WinUI 主窗口上 ——
/// 这样 <c>--tray</c> 开机自启（从来不创建窗口）时也照样能收到通知。
/// 窗口不可见、无 WS_VISIBLE、带 WS_EX_TOOLWINDOW，不会出现在任务栏或 Alt+Tab 里。
/// </summary>
public sealed class SessionGuard : IDisposable
{
    private const string ClassName = "SNNUAutoLogin.SessionGuard";

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_POWERBROADCAST = 0x0218;

    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;

    private const int PBT_APMRESUMESUSPEND = 0x0007;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;

    private const int NOTIFY_FOR_THIS_SESSION = 0;

    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    private readonly WndProcDelegate _proc;      // 必须字段持有，委托被回收后回调就成了野指针
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private IntPtr _hwnd;
    private volatile bool _locked;
    private volatile bool _available;
    private volatile bool _disposed;

    /// <summary>当前是否处于锁屏状态。</summary>
    public bool IsLocked => _locked;

    /// <summary>WTS 会话通知是否注册成功（失败时仍可用手动模拟，只是收不到真实事件）。</summary>
    public bool Available => _available;

    /// <summary>锁屏状态变化（在监听线程上触发，订阅方自行派发到 UI 线程）。</summary>
    public event Action<bool>? LockChanged;

    /// <summary>系统从睡眠恢复。</summary>
    public event Action? Resumed;

    public SessionGuard()
    {
        _proc = WndProc;
    }

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "SNNU.SessionGuard"
        };
        _thread.Start();

        // 等窗口建好再返回：启动阶段就要知道初始是锁屏还是解锁
        _ready.Wait(2000);
    }

    private void ThreadProc()
    {
        try
        {
            IntPtr hInst = GetModuleHandleW(null);

            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = hInst,
                lpszClassName = ClassName
            };

            RegisterClassExW(ref wc);   // 已注册会失败，忽略

            _hwnd = CreateWindowExW(
                WS_EX_TOOLWINDOW, ClassName, ClassName,
                0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
                return;

            _available = WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION);

            // 初始状态：锁屏时输入桌面是 Winlogon，OpenInputDesktop 会因权限被拒
            _locked = IsWorkstationLocked();
            _ready.Set();

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        catch
        {
            // 系统层面拿不到通知时保持静默：只影响锁屏策略，不影响认证主流程
        }
        finally
        {
            _ready.Set();
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_WTSSESSION_CHANGE:
            {
                int code = (int)wParam.ToInt64();
                if (code == WTS_SESSION_LOCK) RaiseLock(true);
                else if (code == WTS_SESSION_UNLOCK) RaiseLock(false);
                break;
            }

            case WM_POWERBROADCAST:
            {
                int code = (int)wParam.ToInt64();
                if (code is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
                {
                    try { Resumed?.Invoke(); } catch { }
                }
                break;
            }

            case WM_CLOSE:
                try { WTSUnRegisterSessionNotification(hWnd); } catch { }
                DestroyWindow(hWnd);
                _hwnd = IntPtr.Zero;
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void RaiseLock(bool locked)
    {
        if (_locked == locked) return;
        _locked = locked;

        try { LockChanged?.Invoke(locked); } catch { }
    }

    /// <summary>开发自检：把锁屏/解锁消息投给自己的窗口，走与真实事件完全相同的处理路径。</summary>
    internal void SimulateLock(bool locked)
    {
        IntPtr h = _hwnd;
        if (h == IntPtr.Zero) return;

        PostMessageW(h, WM_WTSSESSION_CHANGE,
            locked ? WTS_SESSION_LOCK : WTS_SESSION_UNLOCK, IntPtr.Zero);
    }

    private static bool IsWorkstationLocked()
    {
        IntPtr h = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (h != IntPtr.Zero)
        {
            CloseDesktop(h);
            return false;
        }

        // 5 = ERROR_ACCESS_DENIED：当前输入桌面不是我们的，说明停在 Winlogon
        return Marshal.GetLastWin32Error() == 5;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        IntPtr h = _hwnd;
        if (h != IntPtr.Zero) PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        try { _thread?.Join(1500); } catch { }
        _ready.Dispose();
    }

    // ────────────────────────── Win32 ──────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int exStyle, string className, string windowName,
        int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(int flags, bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
