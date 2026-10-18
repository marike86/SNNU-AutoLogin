using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace SNNUAutoLogin;

/// <summary>
/// 原生 Composition 动画封装。
/// 位移一律走 Translation 通道（ElementCompositionPreview.SetIsTranslationEnabled），
/// 避免与布局系统的 Offset 抢写导致动画被打断。
/// 所有 Composition 动画对象在 StartAnimation 后立即释放本地引用，防止长期运行累积。
/// </summary>
public static class Fx
{
    private static readonly Vector2 EaseOutCurve1 = new(0.16f, 1f);
    private static readonly Vector2 EaseOutCurve2 = new(0.30f, 1f);

    public static Visual? V(UIElement el) => ElementCompositionPreview.GetElementVisual(el);

    public static void EnableTranslate(UIElement el) => ElementCompositionPreview.SetIsTranslationEnabled(el, true);

    /// <summary>入场：淡入 + 上移，带延迟形成错落感。</summary>
    public static void FallIn(FrameworkElement el, int delayMs = 0, float dy = 20f, int durMs = 620)
    {
        EnableTranslate(el);
        var v = V(el);
        if (v is null) return;

        var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(EaseOutCurve1, EaseOutCurve2);

        var t = c.CreateVector3KeyFrameAnimation();
        t.InsertKeyFrame(0f, new Vector3(0f, dy, 0f));
        t.InsertKeyFrame(1f, Vector3.Zero, ease);
        t.Duration = TimeSpan.FromMilliseconds(durMs);
        t.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Translation", t);
        t.Dispose();

        // 透明度必须动画 DP 本身：合成器 Opacity 通道会被 XAML 的 DP 值覆盖，
        // 结果就是"淡入"退化成直接显示（DP=1）或者永远不显示（DP=0）。
        FadeIn(el, durMs, delayMs);
    }

    /// <summary>淡入。动画的是 Opacity 依赖属性本身（Storyboard），保证真的逐帧过渡。</summary>
    public static void FadeIn(FrameworkElement el, int durMs = 260, int delayMs = 0)
    {
        el.Opacity = 1;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(durMs)),
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(anim, el);
        Storyboard.SetTargetProperty(anim, "Opacity");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    /// <summary>
    /// 按压缩放反馈：走 Composition 的 Scale 通道（合成线程），不参与布局、不产生 DP 竞争。
    /// 用弹簧式关键帧收尾，松手时有一点回弹，比系统的线性缩放更有"实感"。
    /// </summary>
    public static void PressFeedback(FrameworkElement el, float press = 0.975f)
    {
        var v = V(el);
        if (v is null) return;

        var c = v.Compositor;
        var s = el.ActualSize;
        v.CenterPoint = new Vector3(s.X / 2f, s.Y / 2f, 0f);

        // 尺寸可能在首次调用时还没算出来，挂一次 SizeChanged 修正中心点
        el.SizeChanged += (_, e) =>
            v.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);

        var ease = c.CreateCubicBezierEasingFunction(EaseOutCurve1, EaseOutCurve2);

        void Animate(bool down)
        {
            var a = c.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(1f, new Vector3(down ? press : 1f, down ? press : 1f, 1f), ease);
            a.Duration = TimeSpan.FromMilliseconds(down ? 90 : 220);
            v.StartAnimation("Scale", a);
            a.Dispose();
        }

        // Button 内部会把 PointerPressed 标记为已处理，必须用 handledEventsToo 才收得到
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
