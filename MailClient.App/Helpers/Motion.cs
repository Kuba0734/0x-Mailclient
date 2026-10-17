using MailClient.App.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace MailClient.App.Helpers;

/// <summary>
/// All UI motion. Only opacity and transform (translation/scale/rotation) are animated,
/// timings and easing come from Themes/Tokens.xaml, and everything collapses to the
/// final state when motion is reduced (Windows setting or the app setting).
/// </summary>
public static class Motion
{
    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();

    public static bool Enabled =>
        !SettingsService.Current.ReduceMotion && UiSettings.AnimationsEnabled;

    private static Visual VisualOf(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    private static CompositionEasingFunction Ease(Compositor c) =>
        c.CreateCubicBezierEasingFunction(
            new Vector2((float)Tokens.Double("MotionEaseX1"), (float)Tokens.Double("MotionEaseY1")),
            new Vector2((float)Tokens.Double("MotionEaseX2"), (float)Tokens.Double("MotionEaseY2")));

    private static void Center(Visual v, UIElement e)
    {
        if (e is FrameworkElement fe)
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
    }

    private static void Animate(Visual v, string property, float to, TimeSpan duration, TimeSpan delay = default)
    {
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, to, Ease(v.Compositor));
        a.Duration = duration;
        a.DelayTime = delay;
        v.StartAnimation(property, a);
    }

    private static void Animate(Visual v, string property, Vector3 to, TimeSpan duration, TimeSpan delay = default)
    {
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, to, Ease(v.Compositor));
        a.Duration = duration;
        a.DelayTime = delay;
        v.StartAnimation(property, a);
    }

    // ── Enter / exit ─────────────────────────────────────────────────────

    /// <summary>Fade + slide (+ optional scale) into place.</summary>
    public static void FadeIn(UIElement element, float fromY = 8, int delayMs = 0,
        float fromScale = 1f, float fromX = 0, string duration = "MotionBase")
    {
        var v = VisualOf(element);
        if (!Enabled)
        {
            Reset(element);
            return;
        }

        var c = v.Compositor;
        var ease = Ease(c);
        var d = Tokens.Ms(duration);
        var delay = TimeSpan.FromMilliseconds(delayMs);

        var opacity = c.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, ease);
        opacity.Duration = d;
        opacity.DelayTime = delay;
        opacity.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", opacity);

        var translation = c.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0f, new Vector3(fromX, fromY, 0));
        translation.InsertKeyFrame(1f, Vector3.Zero, ease);
        translation.Duration = d;
        translation.DelayTime = delay;
        translation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Translation", translation);

        Center(v, element);
        var scale = c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(fromScale, fromScale, 1));
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = d;
        scale.DelayTime = delay;
        scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Scale", scale);
    }

    /// <summary>Reverse of <see cref="FadeIn"/>; completes when finished.</summary>
    public static Task FadeOut(UIElement element, float toY = 8, float toScale = 1f, float toX = 0,
        string duration = "MotionFast")
    {
        var v = VisualOf(element);
        if (!Enabled)
        {
            v.Opacity = 0;
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        var batch = v.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var d = Tokens.Ms(duration);
        Center(v, element);
        Animate(v, "Opacity", 0f, d);
        Animate(v, "Translation", new Vector3(toX, toY, 0), d);
        Animate(v, "Scale", new Vector3(toScale, toScale, 1), d);
        batch.End();
        batch.Completed += (_, _) => tcs.TrySetResult();
        return tcs.Task;
    }

    /// <summary>Opacity-only transition (crossfades, overlays, hover reveals).</summary>
    public static void FadeTo(UIElement element, float opacity, string duration = "MotionFast")
    {
        var v = VisualOf(element);
        if (!Enabled)
        {
            v.StopAnimation("Opacity");
            v.Opacity = opacity;
            return;
        }
        Animate(v, "Opacity", opacity, Tokens.Ms(duration));
    }

    /// <summary>Puts composition properties back to the resting state.</summary>
    public static void Reset(UIElement element)
    {
        var v = VisualOf(element);
        v.StopAnimation("Opacity");
        v.StopAnimation("Translation");
        v.StopAnimation("Scale");
        v.Opacity = 1f;
        v.Properties.InsertVector3("Translation", Vector3.Zero);
        v.Scale = Vector3.One;
    }

    /// <summary>Crossfade between two sibling views (tab/page switch). Input is never blocked.</summary>
    public static async void Crossfade(UIElement from, UIElement to)
    {
        to.Visibility = Visibility.Visible;
        VisualOf(to).Opacity = 0;
        FadeTo(to, 1, "MotionBase");
        FadeTo(from, 0, "MotionBase");
        from.IsHitTestVisible = false;
        to.IsHitTestVisible = true;

        if (Enabled) await Task.Delay(Tokens.Ms("MotionBase"));
        if (!from.IsHitTestVisible)
        {
            from.Visibility = Visibility.Collapsed;
            Reset(from);
            from.IsHitTestVisible = true;
        }
    }

    // ── Hover / press ────────────────────────────────────────────────────

    /// <summary>
    /// Hover: translateY(-2px) and a subtle glow (descendant named "HoverGlow" fades in).
    /// Press: scale(0.97).
    /// </summary>
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject o) => (bool)o.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject o, bool value) => o.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || e.NewValue is not true) return;

        el.PointerEntered += (_, _) => Hover(el, true);
        el.PointerExited += (_, _) => Hover(el, false);
        el.PointerCanceled += (_, _) => Hover(el, false);
        el.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(el, true)), true);
        el.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Press(el, false)), true);
    }

    public static void Hover(UIElement el, bool on)
    {
        var v = VisualOf(el);
        var lift = on ? (float)Tokens.Double("MotionHoverLift") : 0f;
        if (Enabled) Animate(v, "Translation", new Vector3(0, lift, 0), Tokens.Ms("MotionFast"));
        else v.Properties.InsertVector3("Translation", new Vector3(0, lift, 0));

        if (FindGlow(el) is { } glow) FadeTo(glow, on ? 1 : 0);
        if (!on) Press(el, false);
    }

    private static void Press(UIElement el, bool down)
    {
        var v = VisualOf(el);
        Center(v, el);
        var s = down ? (float)Tokens.Double("MotionPressScale", 0.97) : 1f;
        if (Enabled) Animate(v, "Scale", new Vector3(s, s, 1), Tokens.Ms("MotionFast"));
        else v.Scale = new Vector3(s, s, 1);
    }

    private static UIElement? FindGlow(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: "HoverGlow" } fe) return fe;
            if (FindGlow(child) is { } found) return found;
        }
        return null;
    }

    // ── Loading / feedback ───────────────────────────────────────────────

    /// <summary>Skeleton shimmer: a soft highlight sweeping across <paramref name="highlight"/>'s parent.</summary>
    public static void Shimmer(FrameworkElement highlight, bool on)
    {
        var v = VisualOf(highlight);
        v.StopAnimation("Translation");
        if (!on || !Enabled)
        {
            highlight.Visibility = Visibility.Collapsed;
            return;
        }

        highlight.Visibility = Visibility.Visible;
        var width = (float)Math.Max(300, (highlight.Parent as FrameworkElement)?.ActualWidth ?? 400);
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, new Vector3(-width, 0, 0));
        a.InsertKeyFrame(1f, new Vector3(width, 0, 0), v.Compositor.CreateLinearEasingFunction());
        a.Duration = Tokens.Ms("MotionShimmer");
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation", a);
    }

    /// <summary>Continuous rotation while syncing.</summary>
    public static void Spin(FrameworkElement element, bool spinning)
    {
        var v = VisualOf(element);
        Center(v, element);
        v.StopAnimation("RotationAngleInDegrees");
        if (!spinning || !Enabled)
        {
            v.RotationAngleInDegrees = 0;
            return;
        }

        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 0f);
        a.InsertKeyFrame(1f, 360f, v.Compositor.CreateLinearEasingFunction());
        a.Duration = Tokens.Ms("MotionSpin");
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("RotationAngleInDegrees", a);
    }

    /// <summary>Toast progress bar draining from full to empty (scaleX 1 → 0).</summary>
    public static void Drain(UIElement bar, TimeSpan duration)
    {
        var v = VisualOf(bar);
        v.CenterPoint = Vector3.Zero;
        v.StopAnimation("Scale");
        v.Scale = Vector3.One;
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, Vector3.One);
        a.InsertKeyFrame(1f, new Vector3(0, 1, 1), v.Compositor.CreateLinearEasingFunction());
        a.Duration = duration;
        v.StartAnimation("Scale", a);
    }

    /// <summary>Horizontal shake for invalid input.</summary>
    public static void Shake(UIElement element)
    {
        if (!Enabled) return;
        var v = VisualOf(element);
        var a = v.Compositor.CreateVector3KeyFrameAnimation();
        float[] offsets = [0, -8, 7, -5, 3, 0];
        for (var i = 0; i < offsets.Length; i++)
            a.InsertKeyFrame(i / (float)(offsets.Length - 1), new Vector3(offsets[i], 0, 0));
        a.Duration = Tokens.Ms("MotionSlow");
        v.StartAnimation("Translation", a);
    }
}
