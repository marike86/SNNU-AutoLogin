using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SNNUAutoLogin.Services;
using Windows.UI;

namespace SNNUAutoLogin;

/// <summary>
/// 开发期自检钩子：出图、托盘生命周期、内存下沉验证。
/// 全部靠命令行参数触发，发布版里不参与正常流程。
/// </summary>
public sealed partial class MainWindow
{
    private bool _hooksRan;

    internal void RunStartupHooks()
    {
        if (_hooksRan) return;
        _hooksRan = true;

        var argv = Environment.GetCommandLineArgs();
        if (argv.AsSpan().Contains("--snapshot")) Guard(RunSnapshotAsync);
        else if (argv.AsSpan().Contains("--ui-test")) Guard(RunUiSelfTestAsync);
        else if (argv.AsSpan().Contains("--test-close")) Guard(RunCloseTestAsync);
        else if (argv.AsSpan().Contains("--test-tray")) Guard(RunTrayFootprintTestAsync);
        else if (argv.AsSpan().Contains("--test-min")) Guard(RunMinimizeTestAsync);
        else if (argv.AsSpan().Contains("--test-exit")) Guard(RunExitWhileHiddenAsync);
    }

    /// <summary>开发钩子异常不能静默吞掉（fire-and-forget 的 async 方法默认会）。</summary>
    private void Guard(Func<Task> hook)
    {
        _ = Run();

        async Task Run()
        {
            try
            {
                await hook();
            }
            catch (Exception ex)
            {
                Trace("hook-failed: " + ex);
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_hook_error.txt"),
                        ex.ToString());
                }
                catch { }
            }
        }
    }

    /// <summary>驻留托盘（内容树已释放）时点"退出"，进程必须真的结束、且不残留托盘图标。</summary>
    private async Task RunExitWhileHiddenAsync()
    {
        await Task.Delay(2200);
        HideToTray();
        await Task.Delay(2000);

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_exit.txt"),
            $"before-exit = {Mem()} built={_uiBuilt}\n", System.Text.Encoding.UTF8);

        _rt.Exit();
    }

    private async Task RunMinimizeTestAsync()
    {
        await Task.Delay(3000);
        if (_appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            p.Minimize();
    }

    private async Task RunSnapshotAsync()
    {
        await Task.Delay(2400);
        await CaptureAsync();
        _rt.Exit();
    }

    /// <summary>模拟点右上角关闭：应转成托盘驻留 + 释放内容树，召回后应能重建。</summary>
    private async Task RunCloseTestAsync()
    {
        await Task.Delay(3000);
        string before = Mem();

        Trace("before-close");
        PostMessageW(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x0010, IntPtr.Zero, IntPtr.Zero);
        await Task.Delay(1500);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool hidden = !IsWindowVisible(hwnd);
        bool tornDown = !_uiBuilt && Content is null;
        string afterClose = Mem();

        // 等回收完成后测量
        await Task.Delay(2500);
        string settled = Mem();

        RestoreFromTray();
        await Task.Delay(1600);
        string restored = Mem();

        var report =
            $"pid        = {Environment.ProcessId}\n" +
            $"mem.before = {before}\n" +
            $"mem.hidden = {afterClose}\n" +
            $"mem.settle = {settled}\n" +
            $"mem.back   = {restored}\n" +
            $"trayMode   = {_rt.Store.Settings.MinimizeToTray}\n" +
            $"hidden     = {_hiddenToTray}\n" +
            $"vis.hidden = {hidden}\n" +
            $"tornDown   = {tornDown}\n" +
            $"rebuilt    = {_uiBuilt}\n" +
            $"tray       = {_rt.Tray is not null}\n" +
            $"tip        = {AppRuntime.LabelOf(_rt.State)}\n";

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_tray.txt"), report,
            System.Text.Encoding.UTF8);

        _rt.Exit();
    }

    /// <summary>
    /// 量化「收进托盘」前后的常驻占用，并连做 3 轮验证重建没有累积泄漏。
    /// </summary>
    private async Task RunTrayFootprintTestAsync()
    {
        await Task.Delay(2500);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"pid     = {Environment.ProcessId}");
        sb.AppendLine($"visible = {Mem()}");

        for (int i = 1; i <= 3; i++)
        {
            HideToTray();
            await Task.Delay(2800);
            sb.AppendLine($"hide#{i}  = {Mem()}  built={_uiBuilt} content={(Content is null ? "null" : "alive")}");

            RestoreFromTray();
            await Task.Delay(2200);
            sb.AppendLine($"back#{i}  = {Mem()}  built={_uiBuilt} sel={OperatorBox.SelectedIndex} " +
                          $"sw={AutoStartSwitch.ActualWidth:F0} logs={Logs.Count} state={_lastState}");
        }

        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_footprint.txt"),
            sb.ToString(), System.Text.Encoding.UTF8);

        _rt.Exit();
    }

    /// <summary>把窗口内容渲染为 PNG（仅用于开发期视觉验证）。</summary>
    internal async Task CaptureAsync()
    {
        try
        {
            // RenderTargetBitmap 只画 XAML 树，取不到 Mica 背板。截图前铺一层
            // Mica 回退底色，让预览与实际观感一致（真实运行时这里仍是系统背板）。
            var originalBg = Root.Background;
            Root.Background = new SolidColorBrush(_theme.IsDark
                ? Color.FromArgb(255, 0x20, 0x20, 0x20)
                : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));

            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(Root);

            var pixels = await rtb.GetPixelsAsync();

            Root.Background = originalBg;
            var buffer = new byte[pixels.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels))
            {
                reader.ReadBytes(buffer);
            }

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_shot.png");

            string Sample(int x, int y)
            {
                int w = rtb.PixelWidth, h = rtb.PixelHeight;
                if (x < 0 || y < 0 || x >= w || y >= h) return "oob";
                int i = (y * w + x) * 4;
                return $"A{buffer[i + 3]:X2} R{buffer[i + 2]:X2} G{buffer[i + 1]:X2} B{buffer[i]:X2}";
            }

            using var mem = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, mem);

            encoder.SetPixelData(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, buffer);

            await encoder.FlushAsync();

            var size = (uint)mem.Size;
            var png = new byte[size];
            using (var reader = new Windows.Storage.Streams.DataReader(mem.GetInputStreamAt(0)))
            {
                await reader.LoadAsync(size);
                reader.ReadBytes(png);
            }

            System.IO.File.WriteAllBytes(path, png);

            var geom =
                $"appwindow  = {AppWindow.Size.Width} x {AppWindow.Size.Height}\n" +
                $"root       = {Root.ActualWidth} x {Root.ActualHeight}\n" +
                $"statusCard = {StatusCard.ActualWidth} x {StatusCard.ActualHeight}\n" +
                $"ringHost   = {RingHost.ActualWidth} x {RingHost.ActualHeight}\n" +
                $"connectBtn = {ConnectBtn.ActualWidth} x {ConnectBtn.ActualHeight}\n" +
                $"accountCard= {AccountCard.ActualWidth} x {AccountCard.ActualHeight}\n" +
                $"behaviorCrd= {BehaviorCard.ActualWidth} x {BehaviorCard.ActualHeight}\n" +
                $"logCard    = {LogCard.ActualWidth} x {LogCard.ActualHeight}\n" +
                $"rtb        = {rtb.PixelWidth} x {rtb.PixelHeight}\n" +
                $"operatorBox= {RectOf(OperatorBox)} sel={OperatorBox.SelectedIndex}\n" +
                $"interval   = {RectOf(IntervalBox)} sel={IntervalBox.SelectedIndex}\n" +
                $"toggle     = {RectOf(AutoStartSwitch)} minW={AutoStartSwitch.MinWidth} pad={AutoStartSwitch.Padding}\n" +
                $"opField    = {_operator} ({(int)_operator})\n" +
                $"settingsOp = {_rt.Store.Settings.Operator} ({(int)_rt.Store.Settings.Operator})\n" +
                $"theme      = dark={_theme.IsDark} accent=#{_theme.Accent.R:X2}{_theme.Accent.G:X2}{_theme.Accent.B:X2}\n" +
                $"success    = #{_theme.Success.R:X2}{_theme.Success.G:X2}{_theme.Success.B:X2}\n" +
                $"caution    = #{_theme.Caution.R:X2}{_theme.Caution.G:X2}{_theme.Caution.B:X2}\n" +
                $"uiReady    = {_uiReady}\n" +
                $"tray       = {(_rt.Tray is null ? "none" : "ok")}\n" +
                $"px.topLeft  = {Sample(6, 6)}\n" +
                $"px.leftEdge = {Sample(6, rtb.PixelHeight / 2)}\n" +
                $"px.topRight = {Sample(rtb.PixelWidth - 8, 6)}\n" +
                $"px.botLeft  = {Sample(6, rtb.PixelHeight - 8)}\n" +
                $"px.card    = {Sample((int)(StatusCard.ActualWidth / 2), 120)}\n";
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_geom.txt"), geom, System.Text.Encoding.UTF8);

            _rt.AddLog($"界面快照已保存：{path}");
        }
        catch (Exception ex)
        {
            _rt.AddLog($"快照失败：{ex.Message}");
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snnu_shot_error.txt"), ex.ToString());
            }
            catch { }
        }
    }
}
