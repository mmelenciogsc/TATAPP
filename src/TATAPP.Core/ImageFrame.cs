namespace TATAPP.Core;

/// <summary>
/// A tightly packed BGRA32 image that keeps processing independent from the UI framework.
/// </summary>
public sealed class ImageFrame
{
    public ImageFrame(int width, int height, byte[] pixels, double dpiX = 96, double dpiY = 96)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        var expected = checked(width * height * 4);
        if (pixels.Length != expected)
            throw new ArgumentException($"Expected {expected} BGRA32 bytes, but received {pixels.Length}.", nameof(pixels));
        if (!double.IsFinite(dpiX) || dpiX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiX));
        if (!double.IsFinite(dpiY) || dpiY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiY));

        Width = width;
        Height = height;
        Pixels = pixels;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => checked(Width * 4);
    public byte[] Pixels { get; }
    public double DpiX { get; }
    public double DpiY { get; }

    public ImageFrame Clone() => new(Width, Height, (byte[])Pixels.Clone(), DpiX, DpiY);
}
