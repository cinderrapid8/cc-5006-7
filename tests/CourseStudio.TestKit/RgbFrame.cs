namespace CourseStudio.TestKit;

/// <summary>Minimal RGB24 bitmap used to render synthetic source frames and read decoded ones.</summary>
public sealed class RgbFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Raw { get; }

    public RgbFrame(int width, int height)
    {
        Width = width;
        Height = height;
        Raw = new byte[width * height * 3];
    }

    private RgbFrame(int width, int height, byte[] raw)
    {
        Width = width;
        Height = height;
        Raw = raw;
    }

    public (byte R, byte G, byte B) Get(int x, int y)
    {
        var o = (y * Width + x) * 3;
        return (Raw[o], Raw[o + 1], Raw[o + 2]);
    }

    public static RgbFrame Wrap(byte[] raw, int width, int height)
    {
        if (raw.Length != width * height * 3)
            throw new ArgumentException($"Expected {width * height * 3} bytes, got {raw.Length}.");
        return new RgbFrame(width, height, raw);
    }
}
