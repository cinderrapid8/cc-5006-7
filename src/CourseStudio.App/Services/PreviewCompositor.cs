using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CourseStudio.Core.Models;
using CourseStudio.Core.Rendering;

namespace CourseStudio.App.Services;

/// <summary>
/// Multi-layer preview: screen layer first, camera PIP composited on top, with the
/// camera layer omittable via ShowCamera (preview-only switch) or per-clip Hidden.
/// Frames are extracted at the source's native pixels and only scaled at draw time,
/// so the preview cache never feeds the exporter.
/// </summary>
public sealed class PreviewCompositor
{
    private readonly FrameExtractor _extractor;
    private readonly Dictionary<string, Bitmap> _bitmapCache = new();

    public PreviewCompositor(FrameExtractor extractor) => _extractor = extractor;

    public bool ShowCamera { get; set; } = true;

    public async Task RenderAsync(DrawingContext ctx, CourseProject project, TimeCode projectTime,
        Rect viewport, CancellationToken ct)
    {
        var scale = Math.Min(viewport.Width / project.CanvasWidth, viewport.Height / project.CanvasHeight);
        var drawW = project.CanvasWidth * scale;
        var drawH = project.CanvasHeight * scale;
        var offsetX = viewport.X + (viewport.Width - drawW) / 2;
        var offsetY = viewport.Y + (viewport.Height - drawH) / 2;

        ctx.FillRectangle(new SolidColorBrush(ParseColor(project.BackgroundHex)), viewport);

        var screenHit = FrameExtractor.ResolveAt(project.ScreenClips, projectTime);
        if (screenHit is { } sh)
        {
            var source = project.FindSource(sh.clip.SourceId);
            if (source is not null)
            {
                var bmp = await GetBitmap(source.FilePath, sh.sourcePosition, ct);
                DrawLayer(ctx, bmp, project, source, ((ScreenClip)sh.clip).ResizeMode,
                    new Rect(offsetX, offsetY, drawW, drawH));
            }
        }

        if (ShowCamera)
        {
            var camHit = FrameExtractor.ResolveAt(project.CameraClips, projectTime);
            if (camHit is { } ch && ch.clip is CameraClip { Hidden: false } cam)
            {
                var source = project.FindSource(cam.SourceId);
                if (source is not null)
                {
                    var bmp = await GetBitmap(source.FilePath, ch.sourcePosition, ct);
                    var pipW = drawW * project.Pip.WidthRatio;
                    var pipH = pipW * (bmp.Size.Height / bmp.Size.Width);
                    var margin = project.Pip.Margin * scale;
                    var (px, py) = project.Pip.Corner switch
                    {
                        PipCorner.TopLeft => (offsetX + margin, offsetY + margin),
                        PipCorner.TopRight => (offsetX + drawW - pipW - margin, offsetY + margin),
                        PipCorner.BottomLeft => (offsetX + margin, offsetY + drawH - pipH - margin),
                        _ => (offsetX + drawW - pipW - margin, offsetY + drawH - pipH - margin)
                    };
                    ctx.DrawImage(bmp, new Rect(0, 0, bmp.Size.Width, bmp.Size.Height),
                        new Rect(px, py, pipW, pipH));
                }
            }
        }
    }

    private async Task<Bitmap> GetBitmap(string path, TimeCode sourcePosition, CancellationToken ct)
    {
        var key = $"{path}|{sourcePosition.Micros}";
        if (_bitmapCache.TryGetValue(key, out var cached)) return cached;
        var png = await _extractor.ExtractFrameAsync(path, sourcePosition, maxWidth: 960, ct);
        using var ms = new MemoryStream(png);
        var bmp = new Bitmap(ms);
        _bitmapCache[key] = bmp;
        return bmp;
    }

    private static void DrawLayer(DrawingContext ctx, Bitmap bmp, CourseProject project,
        MediaSource source, ResizeMode mode, Rect target)
    {
        var sourceAspect = (double)source.NaturalWidth / source.NaturalHeight;
        var targetAspect = target.Width / target.Height;

        Rect dest;
        switch (mode)
        {
            case ResizeMode.Fit:
                dest = sourceAspect > targetAspect
                    ? new Rect(target.X, target.Y + (target.Height - target.Width / sourceAspect) / 2,
                        target.Width, target.Width / sourceAspect)
                    : new Rect(target.X + (target.Width - target.Height * sourceAspect) / 2, target.Y,
                        target.Height * sourceAspect, target.Height);
                ctx.DrawImage(bmp, new Rect(0, 0, bmp.Size.Width, bmp.Size.Height), dest);
                break;

            case ResizeMode.Fill:
            {
                dest = target;
                var srcAspect = sourceAspect;
                Rect src;
                if (srcAspect > targetAspect)
                {
                    var cropW = bmp.Size.Height * targetAspect;
                    src = new Rect((bmp.Size.Width - cropW) / 2, 0, cropW, bmp.Size.Height);
                }
                else
                {
                    var cropH = bmp.Size.Width / targetAspect;
                    src = new Rect(0, (bmp.Size.Height - cropH) / 2, bmp.Size.Width, cropH);
                }
                ctx.DrawImage(bmp, src, dest);
                break;
            }

            case ResizeMode.Original:
            {
                var scale = target.Width / project.CanvasWidth;
                var w = source.NaturalWidth * scale;
                var h = source.NaturalHeight * scale;
                dest = new Rect(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2,
                    Math.Min(w, target.Width), Math.Min(h, target.Height));
                var src = new Rect(0, 0,
                    Math.Min(bmp.Size.Width, bmp.Size.Width * (dest.Width / w)),
                    Math.Min(bmp.Size.Height, bmp.Size.Height * (dest.Height / h)));
                ctx.DrawImage(bmp, src, dest);
                break;
            }
        }
    }

    public void Invalidate() => _bitmapCache.Clear();

    private static Color ParseColor(string hex)
    {
        return Color.TryParse(hex, out var c) ? c : Colors.Black;
    }
}
