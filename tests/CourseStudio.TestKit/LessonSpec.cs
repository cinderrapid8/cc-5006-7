namespace CourseStudio.TestKit;

/// <summary>
/// The single source of truth for the synthetic lesson used by the compositing
/// acceptance tests. The media generator renders exactly these content functions;
/// the reference compositor evaluates the same functions to derive expected pixels.
/// Nothing here calls into the product: compositing/timing expectations are derived
/// independently in <see cref="ReferenceCompositor"/>.
///
/// Every source frame carries a machine-readable time marker: a 16-bit strip of
/// vertical bars encoding the absolute source frame index. Reading the strip back
/// from a decoded composite proves which source frame sits at which timeline
/// position, without OCR.
/// </summary>
public static class LessonSpec
{
    public const int Fps = 10;

    public const int CanvasWidth = 1280;
    public const int CanvasHeight = 720;
    public const string ProjectBackgroundHex = "#101014";
    public const string ClipBackgroundHex = "#7A2A1E"; // letterbox bars of screen clips

    // ---- source geometry ----
    public const int ScreenAWidth = 1920;
    public const int ScreenAHeight = 1080;
    public const int ScreenAFrames = 60; // 6s

    public const int ScreenBWidth = 1024; // 4:3: input size change mid-lesson
    public const int ScreenBHeight = 768;
    public const int ScreenBFrames = 60; // 6s

    public const int CameraWidth = 640;
    public const int CameraHeight = 480;
    public const int CameraFrames = 120; // 12s

    // ---- timeline layout (seconds) ----
    public const double ScreenAStart = 0.0;  // covers 0..6
    public const double ScreenBStart = 6.0;  // covers 6..12 (contiguous)
    public const double CameraStart = 1.0;   // covers 1..12
    public const double CameraLength = 11.0;
    public const double AudioStart = 0.5;    // covers 0.5..12
    public const double AudioLength = 11.5;
    public const double TimelineSeconds = 12.0;
    public const int TimelineFrames = (int)(TimelineSeconds * Fps); // 120

    // ---- PIP defaults of the lesson ----
    public const double PipWidthRatio = 0.25;
    public const int PipMargin = 24;

    // ---- colors ----
    public static readonly (int R, int G, int B) ScreenABackground = (18, 22, 30);
    public static readonly (int R, int G, int B) ScreenBBackground = (24, 30, 20);
    public static readonly (int R, int G, int B) EdgeBorder = (60, 66, 80);
    public static readonly (int R, int G, int B) StripOn = (240, 240, 240);
    public static readonly (int R, int G, int B) StripOff = (50, 50, 50);
    public static readonly (int R, int G, int B) CameraStripOn = (250, 250, 250);
    public static readonly (int R, int G, int B) CameraStripOff = (20, 20, 20);
    public static readonly (int R, int G, int B) CursorDot = (255, 210, 60);
    public static readonly (int R, int G, int B) UnderPipA = (200, 40, 170); // magenta
    public static readonly (int R, int G, int B) UnderPipB = (40, 200, 200); // cyan
    public static readonly (int R, int G, int B) CameraBorder = (240, 240, 240);

    /// <summary>Camera block color per source second; 12 entries cycle over the 12s source.</summary>
    public static readonly (int R, int G, int B)[] CameraPalette =
    {
        (180, 60, 90), (90, 160, 70), (70, 110, 200), (200, 140, 50),
        (150, 80, 190), (60, 180, 180), (210, 90, 150), (120, 120, 120),
        (200, 200, 70), (90, 200, 130), (170, 110, 60), (80, 90, 160),
    };

    // ---- time-marker strip layout (source pixels) ----
    public const int StripBits = 16;
    public const int ScreenStripX = 24;      // first bar left edge
    public const int ScreenStripY = 12;      // strip top
    public const int ScreenStripBarWidth = 20;
    public const int ScreenStripBarPitch = 28;
    public const int ScreenStripBarHeight = 40;

    public const int CameraStripX = 12;
    public const int CameraStripY = 14;
    public const int CameraStripBarWidth = 12;
    public const int CameraStripBarPitch = 20;
    public const int CameraStripBarHeight = 20;

    // ---- audio beeps ----
    public const int VoiceSampleRate = 44100;
    public const int BeepCount = 12; // beep k starts at source time k seconds
    public const double BeepSeconds = 0.25;
    public const double BeepAmplitude = 0.6;

    // ---- content functions (source pixel space) ----

    public static (int R, int G, int B) ScreenAContent(int frame, int x, int y)
    {
        if (x < 4 || y < 4 || x >= ScreenAWidth - 4 || y >= ScreenAHeight - 4) return EdgeBorder;
        if (TryStripBit(frame, x, y, ScreenStripX, ScreenStripY, ScreenStripBarWidth,
                ScreenStripBarPitch, ScreenStripBarHeight, out var on))
            return on ? StripOn : StripOff;
        // Marker under the default PIP zone: canvas (1000..1100, 500..600) at 2/3 scale.
        if (x is >= 1500 and < 1650 && y is >= 750 and < 900) return UnderPipA;
        var cx = 200 + (frame % Fps) * 40;
        const int cy = 600;
        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < 20 * 20) return CursorDot;
        return ScreenABackground;
    }

    public static (int R, int G, int B) ScreenBContent(int frame, int x, int y)
    {
        if (x < 4 || y < 4 || x >= ScreenBWidth - 4 || y >= ScreenBHeight - 4) return EdgeBorder;
        if (TryStripBit(frame, x, y, ScreenStripX, ScreenStripY, ScreenStripBarWidth,
                ScreenStripBarPitch, ScreenStripBarHeight, out var on))
            return on ? StripOn : StripOff;
        // Marker under the default PIP zone: canvas (1000..1100, 500..600) at 0.9375 scale
        // with a 160px horizontal pad offset.
        if (x is >= 896 and < 1002 && y is >= 533 and < 640) return UnderPipB;
        var cx = 100 + (frame % Fps) * 30;
        const int cy = 450;
        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < 14 * 14) return CursorDot;
        return ScreenBBackground;
    }

    public static (int R, int G, int B) CameraContent(int frame, int x, int y)
    {
        if (x < 10 || y < 10 || x >= CameraWidth - 10 || y >= CameraHeight - 10) return CameraBorder;
        if (TryStripBit(frame, x, y, CameraStripX, CameraStripY, CameraStripBarWidth,
                CameraStripBarPitch, CameraStripBarHeight, out var on))
            return on ? CameraStripOn : CameraStripOff;
        return CameraPalette[(frame / Fps) % CameraPalette.Length];
    }

    private static bool TryStripBit(int frame, int x, int y, int stripX, int stripY,
        int barWidth, int barPitch, int barHeight, out bool on)
    {
        on = false;
        if (y < stripY || y >= stripY + barHeight) return false;
        var rel = x - stripX;
        if (rel < 0 || rel >= StripBits * barPitch) return false;
        var bit = rel / barPitch;
        if (rel % barPitch >= barWidth) return false; // gap between bars: background shows through
        on = ((frame >> bit) & 1) == 1;
        return true;
    }

    /// <summary>Mono voice track: beep k starts exactly at source second k (440/880 Hz alternating).</summary>
    public static short VoiceSample(long sampleIndex)
    {
        var t = sampleIndex / (double)VoiceSampleRate;
        var k = (int)Math.Floor(t);
        if (k < 0 || k >= BeepCount) return 0;
        var local = t - k;
        if (local >= BeepSeconds) return 0;
        var freq = k % 2 == 0 ? 440.0 : 880.0;
        var attack = Math.Min(1.0, local / 0.005);
        var release = Math.Min(1.0, (BeepSeconds - local) / 0.005);
        var env = Math.Max(0.0, Math.Min(attack, release));
        var v = BeepAmplitude * Math.Sin(2 * Math.PI * freq * local) * env;
        return (short)Math.Clamp((int)Math.Round(v * 32767), short.MinValue, short.MaxValue);
    }
}
