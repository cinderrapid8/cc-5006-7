using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CourseStudio.App.ViewModels;
using CourseStudio.App.Services;
using CourseStudio.Core.Models;

namespace CourseStudio.App.Views;

/// <summary>
/// Draws the composited multi-layer preview (screen + camera PIP). The backing
/// bitmaps come from native-resolution source extraction and are only scaled for
/// the current viewport; the export path never reads them.
/// </summary>
public sealed class PreviewControl : Control
{
    private MainWindowViewModel? _vm;
    private bool _rendering;

    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<PreviewControl, MainWindowViewModel?>(nameof(ViewModel));

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewModelProperty) return;
        if (_vm is not null) _vm.ProjectChanged -= InvalidateVisual;
        _vm = ViewModel;
        if (_vm is not null) _vm.ProjectChanged += InvalidateVisual;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_vm is null) return;
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.FillRectangle(Brushes.Black, rect);
        ScheduleCompositeRender(context, rect);
    }

    private async void ScheduleCompositeRender(DrawingContext context, Rect viewport)
    {
        if (_rendering || _vm is null) return;
        _rendering = true;
        try
        {
            await _vm.Preview.RenderAsync(context, _vm.Project, _vm.Playhead, viewport, default);
            DrawPlayheadHud(context, viewport);
        }
        catch
        {
            // A missing/locked frame should not crash the UI; next change retries.
            var message = new FormattedText(
                "Preview frame unavailable",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                14, Brushes.OrangeRed)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = viewport.Width
            };
            context.DrawText(message, new Point(viewport.Width / 2, viewport.Height / 2));
        }
        finally
        {
            _rendering = false;
        }
    }

    private void DrawPlayheadHud(DrawingContext ctx, Rect viewport)
    {
        if (_vm is null) return;
        var text = new FormattedText(
            $"{_vm.Playhead.ToDisplay()} / {new TimeCode(_vm.TimelineDurationMicros).ToDisplay()}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            12, Brushes.White);
        ctx.DrawRectangle(new SolidColorBrush(Colors.Black, 0.55), null,
            new RoundedRect(new Rect(8, 8, text.Width + 16, text.Height + 8), 4));
        ctx.DrawText(text, new Point(16, 12));
    }
}
