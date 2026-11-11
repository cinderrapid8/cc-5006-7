using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CourseStudio.App.ViewModels;
using CourseStudio.Core.Models;

namespace CourseStudio.App.Views;

/// <summary>
/// Custom timeline: three locked-capable tracks, clips drawn at their timeline
/// positions, mute regions and the ripple range overlaid. Clicking a clip selects
/// it; clicking the ruler moves the playhead. Pauses between clips show as gaps.
/// </summary>
public sealed class TimelineControl : Control
{
    private const int RulerHeight = 28;
    private const int TrackHeight = 54;
    private const int LabelWidth = 78;

    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<TimelineControl, MainWindowViewModel?>(nameof(ViewModel));

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private double PixelsPerSecond => 60 * (ViewModel?.PreviewScale ?? 1);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            if (change.OldValue is MainWindowViewModel old) old.ProjectChanged -= OnProjectChanged;
            if (change.NewValue is MainWindowViewModel @new) @new.ProjectChanged += OnProjectChanged;
            InvalidateVisual();
        }
    }

    private void OnProjectChanged() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var vm = ViewModel;
        if (vm is null) return;
        var duration = Math.Max(10, vm.TimelineDurationMicros / 1_000_000.0);
        var right = LabelWidth + duration * PixelsPerSecond + 40;
        Width = right;
        Height = RulerHeight + TrackHeight * 3 + 12;

        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1B1B20")), new Rect(0, 0, Bounds.Width, Bounds.Height));
        DrawRuler(ctx, duration);
        DrawTrack(ctx, TrackKind.Screen, vm.Project.ScreenClips, RulerHeight,
            (SolidColorBrush)Application.Current!.FindResource("TrackScreenBrush")!, vm);
        DrawTrack(ctx, TrackKind.Camera, vm.Project.CameraClips, RulerHeight + TrackHeight,
            (SolidColorBrush)Application.Current!.FindResource("TrackCameraBrush")!, vm);
        DrawTrack(ctx, TrackKind.Audio, vm.Project.AudioClips, RulerHeight + TrackHeight * 2,
            (SolidColorBrush)Application.Current!.FindResource("TrackAudioBrush")!, vm);
        DrawMuteRegions(ctx, vm);
        DrawRippleRange(ctx, vm);
        DrawPlayhead(ctx, vm);
    }

    private void DrawRuler(DrawingContext ctx, double durationSeconds)
    {
        var pen = new Pen(Brushes.DimGray, 1);
        for (var s = 0; s <= durationSeconds; s++)
        {
            var x = LabelWidth + s * PixelsPerSecond;
            ctx.DrawLine(pen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            if (s % 5 == 0)
            {
                var txt = new FormattedText($"{s}s",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default), 10, Brushes.LightGray);
                ctx.DrawText(txt, new Point(x + 2, 2));
            }
        }
    }

    private void DrawTrack<T>(DrawingContext ctx, TrackKind kind, IEnumerable<T> clips,
        double y, IBrush brush, MainWindowViewModel vm) where T : Clip
    {
        var label = kind.ToString();
        var lockMark = vm.Project.IsTrackLocked(kind) ? " 🔒" : "";
        var txt = new FormattedText(label + lockMark,
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 11, Brushes.White);
        ctx.DrawText(txt, new Point(6, y + TrackHeight / 2.0 - 8));

        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#24242B")), null,
            new Rect(LabelWidth, y, Bounds.Width - LabelWidth, TrackHeight - 4));

        foreach (var clip in clips)
        {
            var x = LabelWidth + clip.Start.TotalSeconds * PixelsPerSecond;
            var w = Math.Max(3, clip.Duration.TotalSeconds * PixelsPerSecond);
            var rect = new Rect(x, y + 4, w, TrackHeight - 12);

            IBrush clipBrush = brush;
            if (clip is CameraClip { Hidden: true })
                clipBrush = new SolidColorBrush(Color.Parse("#555560"));
            if (vm.SelectedRow?.Id == clip.Id)
                clipBrush = new SolidColorBrush(((SolidColorBrush)clipBrush).Color, 0.65);

            ctx.DrawRectangle(clipBrush, null, new RoundedRect(rect, 4));
            var labelText = new FormattedText(
                clip is CameraClip cc && cc.Hidden ? "hidden" : $"{clip.SourceIn.TotalSeconds:0.#}–{clip.SourceOut.TotalSeconds:0.#}s",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default), 10, Brushes.White)
            { MaxTextWidth = rect.Width - 8 };
            ctx.DrawText(labelText, new Point(rect.X + 4, rect.Y + 4));
        }
    }

    private void DrawMuteRegions(DrawingContext ctx, MainWindowViewModel vm)
    {
        var y = RulerHeight + TrackHeight * 2;
        foreach (var region in vm.Project.MuteRegions)
        {
            var x = LabelWidth + region.Start.TotalSeconds * PixelsPerSecond;
            var w = Math.Max(2, region.Duration.TotalSeconds * PixelsPerSecond);
            ctx.FillRectangle(new SolidColorBrush(Colors.OrangeRed, 0.35),
                new Rect(x, y + 4, w, TrackHeight - 12));
            var txt = new FormattedText("MUTE",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default), 10, Brushes.OrangeRed);
            ctx.DrawText(txt, new Point(x + 2, y + 6));
        }
    }

    private void DrawRippleRange(DrawingContext ctx, MainWindowViewModel vm)
    {
        if (vm.RangeEndMicros <= vm.RangeStartMicros) return;
        var x1 = LabelWidth + vm.RangeStartMicros / 1_000_000.0 * PixelsPerSecond;
        var x2 = LabelWidth + vm.RangeEndMicros / 1_000_000.0 * PixelsPerSecond;
        ctx.FillRectangle(new SolidColorBrush(Colors.Crimson, 0.22),
            new Rect(x1, RulerHeight, x2 - x1, TrackHeight * 3));
        ctx.DrawLine(new Pen(Brushes.Crimson, 1.5),
            new Point(x1, RulerHeight), new Point(x1, RulerHeight + TrackHeight * 3));
        ctx.DrawLine(new Pen(Brushes.Crimson, 1.5),
            new Point(x2, RulerHeight), new Point(x2, RulerHeight + TrackHeight * 3));
    }

    private void DrawPlayhead(DrawingContext ctx, MainWindowViewModel vm)
    {
        var x = LabelWidth + vm.PlayheadMicros / 1_000_000.0 * PixelsPerSecond;
        ctx.DrawLine(new Pen(Brushes.White, 1.5),
            new Point(x, 0), new Point(x, RulerHeight + TrackHeight * 3));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;
        var pos = e.GetPosition(this);

        if (pos.Y <= RulerHeight || pos.X >= LabelWidth)
        {
            var seconds = Math.Max(0, (pos.X - LabelWidth) / PixelsPerSecond);
            vm.PlayheadMicros = (long)(seconds * 1_000_000);
        }

        var hit = HitTestClip(pos);
        vm.SelectedRow = hit is null
            ? null
            : vm.Rows.FirstOrDefault(r => r.Id == hit.Id);
        InvalidateVisual();
    }

    private Clip? HitTestClip(Point pos)
    {
        var vm = ViewModel;
        if (vm is null || pos.X < LabelWidth) return null;
        var trackIndex = (int)((pos.Y - RulerHeight) / TrackHeight);
        var clips = trackIndex switch
        {
            0 => vm.Project.ScreenClips.Cast<Clip>(),
            1 => vm.Project.CameraClips.Cast<Clip>(),
            2 => vm.Project.AudioClips.Cast<Clip>(),
            _ => Enumerable.Empty<Clip>()
        };
        foreach (var clip in clips)
        {
            var x = LabelWidth + clip.Start.TotalSeconds * PixelsPerSecond;
            var w = Math.Max(3, clip.Duration.TotalSeconds * PixelsPerSecond);
            if (pos.X >= x && pos.X <= x + w) return clip;
        }
        return null;
    }
}
