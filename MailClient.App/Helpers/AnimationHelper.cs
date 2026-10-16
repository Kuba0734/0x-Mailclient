using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace MailClient.App.Helpers;

/// <summary>Small set of composition-based animations used throughout the UI.</summary>
public static class AnimationHelper
{
    private static Visual VisualOf(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    private static CompositionEasingFunction EaseOut(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    /// <summary>Fades the element in while sliding it from an offset to its final position.</summary>
    public static void FadeSlideIn(UIElement element, float fromX = 0, float fromY = 16,
        int durationMs = 420, int delayMs = 0, float fromScale = 1f)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var ease = EaseOut(c);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var delay = TimeSpan.FromMilliseconds(delayMs);

        var opacity = c.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, ease);
        opacity.Duration = duration;
        opacity.DelayTime = delay;
        opacity.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        var translation = c.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0f, new Vector3(fromX, fromY, 0));
        translation.InsertKeyFrame(1f, Vector3.Zero, ease);
        translation.Duration = duration;
        translation.DelayTime = delay;
        translation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation", translation);

        if (Math.Abs(fromScale - 1f) > 0.001f)
        {
            if (element is FrameworkElement fe)
                visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);

            var scale = c.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new Vector3(fromScale, fromScale, 1));
            scale.InsertKeyFrame(1f, Vector3.One, ease);
            scale.Duration = duration;
            scale.DelayTime = delay;
            scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Scale", scale);
        }
    }

    /// <summary>Fades/slides the element out and completes when the animation finished.</summary>
    public static Task FadeSlideOut(UIElement element, float toX = 0, float toY = 16,
        int durationMs = 220, float toScale = 1f)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var tcs = new TaskCompletionSource();
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);

        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(0.84f, 0f));
        var duration = TimeSpan.FromMilliseconds(durationMs);

        var opacity = c.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f, ease);
        opacity.Duration = duration;

        var translation = c.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(1f, new Vector3(toX, toY, 0), ease);
        translation.Duration = duration;

        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation", translation);

        if (Math.Abs(toScale - 1f) > 0.001f)
        {
            if (element is FrameworkElement fe)
                visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            var scale = c.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1f, new Vector3(toScale, toScale, 1), ease);
            scale.Duration = duration;
            visual.StartAnimation("Scale", scale);
        }

        batch.End();
        batch.Completed += (_, _) => tcs.TrySetResult();
        return tcs.Task;
    }

    /// <summary>Resets composition properties changed by the out-animations.</summary>
    public static void Reset(UIElement element)
    {
        var visual = VisualOf(element);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.StopAnimation("Scale");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Scale = Vector3.One;
    }

    /// <summary>Springy scale on pointer hover/press.</summary>
    public static void AttachHoverScale(UIElement element, float hoverScale = 1.03f, float pressedScale = 0.97f)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;

        void AnimateTo(float s)
        {
            if (element is FrameworkElement fe)
                visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            var spring = c.CreateSpringVector3Animation();
            spring.FinalValue = new Vector3(s, s, 1);
            spring.DampingRatio = 0.6f;
            spring.Period = TimeSpan.FromMilliseconds(50);
            visual.StartAnimation("Scale", spring);
        }

        element.PointerEntered += (_, _) => AnimateTo(hoverScale);
        element.PointerExited += (_, _) => AnimateTo(1f);
        element.PointerCanceled += (_, _) => AnimateTo(1f);
        element.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => AnimateTo(pressedScale)), true);
        element.AddHandler(UIElement.PointerReleasedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => AnimateTo(hoverScale)), true);
    }

    /// <summary>Continuous rotation (e.g. sync icon).</summary>
    public static void SetSpinning(FrameworkElement element, bool spinning)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);

        if (spinning)
        {
            var rotation = c.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(0f, 0f);
            rotation.InsertKeyFrame(1f, 360f, c.CreateLinearEasingFunction());
            rotation.Duration = TimeSpan.FromMilliseconds(900);
            rotation.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngleInDegrees", rotation);
        }
        else
        {
            visual.StopAnimation("RotationAngleInDegrees");
            var settle = c.CreateScalarKeyFrameAnimation();
            settle.InsertKeyFrame(1f, 360f * MathF.Ceiling(visual.RotationAngleInDegrees / 360f), EaseOut(c));
            settle.Duration = TimeSpan.FromMilliseconds(350);
            visual.StartAnimation("RotationAngleInDegrees", settle);
        }
    }

    /// <summary>Gentle endless up/down floating (empty-state illustrations).</summary>
    public static void StartFloating(UIElement element, float amplitude = 6f, int periodMs = 3200)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var anim = c.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(0f, new Vector3(0, -amplitude, 0));
        anim.InsertKeyFrame(0.5f, new Vector3(0, amplitude, 0), ease);
        anim.InsertKeyFrame(1f, new Vector3(0, -amplitude, 0), ease);
        anim.Duration = TimeSpan.FromMilliseconds(periodMs);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", anim);
    }

    /// <summary>Endless opacity pulse (skeleton loading placeholders).</summary>
    public static void StartPulse(UIElement element, float min = 0.45f, int periodMs = 1300)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var anim = c.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, min);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(periodMs);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    public static void StopPulse(UIElement element)
    {
        var visual = VisualOf(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }

    /// <summary>Small "pop" used for badges and toggled icons.</summary>
    public static void Pop(FrameworkElement element, float from = 0.6f)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var spring = c.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(from, from, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.45f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        visual.StartAnimation("Scale", spring);
    }

    /// <summary>Horizontal shake to signal invalid input.</summary>
    public static void Shake(UIElement element)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var anim = c.CreateVector3KeyFrameAnimation();
        float[] offsets = [0, -10, 9, -7, 5, -3, 0];
        for (var i = 0; i < offsets.Length; i++)
            anim.InsertKeyFrame(i / (float)(offsets.Length - 1), new Vector3(offsets[i], 0, 0));
        anim.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Translation", anim);
    }

    /// <summary>Show/hide animations that run automatically when Visibility changes.</summary>
    public static void EnableImplicitFade(UIElement element, float slideY = 8f, int durationMs = 250)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var c = visual.Compositor;
        var duration = TimeSpan.FromMilliseconds(durationMs);

        var showOpacity = c.CreateScalarKeyFrameAnimation();
        showOpacity.Target = "Opacity";
        showOpacity.InsertKeyFrame(0f, 0f);
        showOpacity.InsertKeyFrame(1f, 1f);
        showOpacity.Duration = duration;

        var showTranslation = c.CreateVector3KeyFrameAnimation();
        showTranslation.Target = "Translation";
        showTranslation.InsertKeyFrame(0f, new Vector3(0, slideY, 0));
        showTranslation.InsertKeyFrame(1f, Vector3.Zero, EaseOut(c));
        showTranslation.Duration = duration;

        var show = c.CreateAnimationGroup();
        show.Add(showOpacity);
        show.Add(showTranslation);

        var hideOpacity = c.CreateScalarKeyFrameAnimation();
        hideOpacity.Target = "Opacity";
        hideOpacity.InsertKeyFrame(1f, 0f);
        hideOpacity.Duration = TimeSpan.FromMilliseconds(durationMs * 0.6);

        ElementCompositionPreview.SetImplicitShowAnimation(element, show);
        ElementCompositionPreview.SetImplicitHideAnimation(element, hideOpacity);
    }
}
