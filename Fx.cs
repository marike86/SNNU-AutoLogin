using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace SNNUAutoLogin;

/// <summary>
/// 动效层：节奏与曲线对齐 Windows 11「设置」。
///
/// 三条约定：
/// 1. 位移一律走 Translation / Scale 通道（Composition，合成线程），不碰 Offset 与布局，避免和 DP 抢写；
/// 2. 透明度只能动画 DP 本身（Storyboard）—— 合成器的 Opacity 通道会被 XAML 的 DP 值覆盖；
/// 3. 所有 Composition 动画对象在 StartAnimation 之后立即 Dispose，长期驻留不累积句柄。
/// </summary>
public static class Fx
{
    // ── Fluent 2 动效 token（与系统设置一致） ──
    public const int FastMs = 150;
    public const int NormalMs = 250;
    public const int SlowMs = 320;

    /// <summary>标准减速曲线：起步快、收尾稳，界面元素进场的默认曲线。</summary>
    private static readonly Vector2 Decelerate1 = new(0.10f, 0.90f);
    private static readonly Vector2 Decelerate2 = new(0.20f, 1.00f);

    /// <summary>轻微过冲，只给"弹一下"的图标用。</summary>
    private static readonly Vector2 Overshoot1 = new(0.20f, 1.20f);
    private static readonly Vector2 Overshoot2 = new(0.30f, 1.00f);

    private static readonly ConditionalWeakTable<FrameworkElement, Token> Tokens = new();

    private sealed class Token
    {
        public int Value;
    }

    private static int NextToken(FrameworkElement el)
    {
        var t = Tokens.GetOrCreateValue(el);
        return ++t.Value;
    }

    public static Visual? V(UIElement el) => ElementCompositionPreview.GetElementVisual(el);

    public static void EnableTranslate(UIElement el) => ElementCompositionPreview.SetIsTranslationEnabled(el, true);

    // ────────────────────────── 入场 ──────────────────────────

    /// <summary>入场：淡入 + 上移。默认 300ms、12px，和系统设置页的进场一致。</summary>
    public static void FallIn(FrameworkElement el, int delayMs = 0, float dy = 12f, int durMs = SlowMs)
    {
        if (el is null) return;

        EnableTranslate(el);
        var v = V(el);
        if (v is not null)
        {
            var c = v.Compositor;
            var ease = c.CreateCubicBezierEasingFunction(Decelerate1, Decelerate2);

            var t = c.CreateVector3KeyFrameAnimation();
            t.InsertKeyFrame(0f, new Vector3(0f, dy, 0f));
            t.InsertKeyFrame(1f, Vector3.Zero, ease);
            t.Duration = TimeSpan.FromMilliseconds(durMs);
            t.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            v.StartAnimation("Translation", t);
            t.Dispose();
        }

        FadeTo(el, 1f, durMs, delayMs);
    }

    /// <summary>淡入淡出。动画的是 Opacity 依赖属性本身（Storyboard），保证真的逐帧过渡。</summary>
    public static void FadeTo(FrameworkElement el, float to, int durMs = NormalMs, int delayMs = 0,
        EasingMode mode = EasingMode.EaseOut)
    {
        if (el is null) return;

        float from = (float)el.Opacity;
        el.Opacity = to;

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durMs)),
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = mode }
        };

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, "Opacity");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    public static void FadeIn(FrameworkElement el, int durMs = NormalMs, int delayMs = 0) =>
        FadeTo(el, 1f, durMs, delayMs);

    /// <summary>
    /// 展开一段内容（验证码面板之类）：先显示再淡入上移。
    /// 高度变化本身是布局行为，但配上淡入后观感是"长出来"而不是"跳出来"。
    /// </summary>
    public static void Reveal(FrameworkElement el, float dy = 8f, int durMs = NormalMs)
    {
        if (el is null) return;

        _ = NextToken(el);        // 作废还在排队的那次收起，避免刚展开就被折叠

        bool wasVisible = el.Visibility == Visibility.Visible;
        el.Visibility = Visibility.Visible;

        // 从"刚收起"回来时 Opacity 已被复位成 1，这里要显式归零，否则只有位移没有淡入
        if (!wasVisible) el.Opacity = 0;

        FallIn(el, 0, dy, durMs);
    }

    /// <summary>收起：淡出后再真正隐藏，避免高度瞬间塌陷。</summary>
    public static void Conceal(FrameworkElement el, int durMs = FastMs)
    {
        if (el is null || el.Visibility != Visibility.Visible) return;

        int token = NextToken(el);

        var anim = new DoubleAnimation
        {
            From = el.Opacity,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(durMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, "Opacity");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            if (Tokens.GetOrCreateValue(el).Value != token) return;

            el.Opacity = 1;
            el.Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    // ────────────────────────── 文字与图标 ──────────────────────────

    /// <summary>
    /// 换文字：先 80ms 淡出、换字、再淡入上移。
    /// 比"直接赋值 + 重放入场"少了硬切的闪烁，状态在几个词之间跳变时尤其明显。
    /// </summary>
    public static void SwapText(TextBlock tb, string text, bool animate = true, float dy = 5f, int durMs = 220)
    {
        if (tb is null) return;
        if (string.Equals(tb.Text, text, StringComparison.Ordinal)) return;

        if (!animate || !tb.IsLoaded || string.IsNullOrEmpty(tb.Text))
        {
            tb.Text = text;
            if (animate) FallIn(tb, 0, dy, durMs);
            return;
        }

        int token = NextToken(tb);

        var anim = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(80)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        Storyboard.SetTarget(anim, tb);
        Storyboard.SetTargetProperty(anim, "Opacity");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            if (Tokens.GetOrCreateValue(tb).Value != token) return;

            tb.Text = text;
            FallIn(tb, 0, dy, durMs);
        };
        sb.Begin();
    }

    /// <summary>图标弹一下：0.86 → 1，带轻微过冲。用于状态图标切换。</summary>
    public static void Pop(FrameworkElement el, float from = 0.86f, int durMs = 300)
    {
        if (el is null) return;

        var v = V(el);
        if (v is null) return;

        var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(Overshoot1, Overshoot2);

        v.CenterPoint = new Vector3(el.ActualSize.X / 2f, el.ActualSize.Y / 2f, 0f);

        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, new Vector3(from, from, 1f));
        a.InsertKeyFrame(1f, Vector3.One, ease);
        a.Duration = TimeSpan.FromMilliseconds(durMs);
        v.StartAnimation("Scale", a);
        a.Dispose();
    }

    // ────────────────────────── 按下反馈 ──────────────────────────

    /// <summary>
    /// 按压缩放：合成线程 Scale 通道，松手带一点回弹。
    /// Button 内部会把 PointerPressed 标记为已处理，必须用 handledEventsToo 才收得到。
    /// </summary>
    public static void PressFeedback(FrameworkElement el, float press = 0.975f)
    {
        var v = V(el);
        if (v is null) return;

        var c = v.Compositor;
        v.CenterPoint = new Vector3(el.ActualSize.X / 2f, el.ActualSize.Y / 2f, 0f);

        el.SizeChanged += (_, e) =>
            v.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);

        var ease = c.CreateCubicBezierEasingFunction(Decelerate1, Decelerate2);

        void Animate(bool down)
        {
            var a = c.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(1f, new Vector3(down ? press : 1f, down ? press : 1f, 1f), ease);
            a.Duration = TimeSpan.FromMilliseconds(down ? 90 : 200);
            v.StartAnimation("Scale", a);
            a.Dispose();
        }

        el.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => Animate(true)), true);
        el.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => Animate(false)), true);
        el.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler((_, _) => Animate(false)), true);
        el.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => Animate(false)), true);
    }
}
