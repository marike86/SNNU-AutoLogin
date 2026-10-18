using System;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SNNUAutoLogin.Services;

/// <summary>
/// 系统原生配色来源。
/// 强调色直接取 Windows 口音色（UISettings.Accent），状态色取 Fluent 语义色板
/// （SystemFillColorSuccess / Caution / Critical）的官方浅色与深色取值，
/// 与「设置」等系统应用完全同源，并跟随系统主题与口音色变化。
/// </summary>
public sealed class ThemeColors : IDisposable
{
    private readonly UISettings _ui;
    private readonly DispatcherQueue _dispatcher;
    private bool _dark;
    private bool _disposed;

    /// <summary>口音色、主题或对比度变化时触发（已切回 UI 线程）。可用时传 null。</summary>
    public event Action? Changed;

    public Color Accent { get; private set; } = Color.FromArgb(255, 0x00, 0x5F, 0xB8);

    public ThemeColors(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _ui = new UISettings();
        _ui.ColorValuesChanged += OnColorValuesChanged;
        Refresh();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        // 该回调来自后台线程，必须切回 UI 线程再改控件
        _dispatcher.TryEnqueue(() =>
        {
            Refresh();
            Changed?.Invoke();
        });
    }

    /// <summary>按当前主题重新解析全部颜色。</summary>
    public void Refresh(bool? isDark = null)
    {
        if (isDark.HasValue) _dark = isDark.Value;

        try
        {
            var accent = _ui.GetColorValue(UIColorType.Accent);
            // 深色主题下系统对强调色做提亮，避免在深底上发闷
            Accent = _dark ? Mix(accent, Colors.White, 0.30) : accent;
        }
        catch
        {
            Accent = _dark ? Color.FromArgb(255, 0x76, 0xB9, 0xED) : Color.FromArgb(255, 0x00, 0x5F, 0xB8);
        }
    }

    public bool IsDark => _dark;

    // ── Fluent 语义状态色（与 SystemFillColor* 的官方取值一致） ──

    public Color Success => _dark ? Rgb(0x6C, 0xCB, 0x5F) : Rgb(0x0F, 0x7B, 0x0F);

    public Color Caution => _dark ? Rgb(0xFC, 0xE1, 0x00) : Rgb(0x9D, 0x5D, 0x00);

    public Color Critical => _dark ? Rgb(0xFF, 0x99, 0xA4) : Rgb(0xC4, 0x2B, 0x1C);

    /// <summary>中性色，等价于 TextFillColorTertiary。</summary>
    public Color Neutral => _dark ? Rgb(0x8A, 0x8A, 0x8A) : Rgb(0x5C, 0x5C, 0x5C);

    // ── 工具 ──

    public static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            a.A,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _ui.ColorValuesChanged -= OnColorValuesChanged; } catch { }
    }
}
