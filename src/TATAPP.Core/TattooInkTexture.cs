namespace TATAPP.Core;

/// <summary>
/// Removes a light design-sheet background and prepares color or black marks as
/// translucent ink. The resulting BGRA frame can be mapped around a 3D surface.
/// </summary>
public static class TattooInkTexture
{
    public static ImageFrame Create(ImageFrame source, double opacity = 0.82,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        opacity = Math.Clamp(opacity, 0, 1);
        var background = EstimateSheetBackground(source);
        var pixels = new byte[source.Pixels.Length];
        for (var offset = 0; offset < source.Pixels.Length; offset += 4)
        {
            if ((offset & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var sourceAlpha = source.Pixels[offset + 3] / 255d;
            var blue = source.Pixels[offset];
            var green = source.Pixels[offset + 1];
            var red = source.Pixels[offset + 2];
            var colorDistance = Math.Max(Math.Abs(red - background.Red),
                Math.Max(Math.Abs(green - background.Green), Math.Abs(blue - background.Blue)));
            var backgroundLuma = (54 * background.Red + 183 * background.Green + 19 * background.Blue) / 256d;
            var pixelLuma = (54 * red + 183 * green + 19 * blue) / 256d;
            var darkness = Math.Max(0, backgroundLuma - pixelLuma);
            var inkStrength = Math.Max(colorDistance, darkness);
            var inkAlpha = Math.Clamp((inkStrength - 12) / 128d, 0, 1) * opacity * sourceAlpha;

            pixels[offset] = (byte)Math.Round(blue * 0.72);
            pixels[offset + 1] = (byte)Math.Round(green * 0.72);
            pixels[offset + 2] = (byte)Math.Round(red * 0.72);
            pixels[offset + 3] = (byte)Math.Round(inkAlpha * 255);
        }
        return new ImageFrame(source.Width, source.Height, pixels, source.DpiX, source.DpiY);
    }

    private static (byte Red, byte Green, byte Blue) EstimateSheetBackground(ImageFrame source)
    {
        var red = new List<byte>();
        var green = new List<byte>();
        var blue = new List<byte>();
        var stepX = Math.Max(1, source.Width / 128);
        var stepY = Math.Max(1, source.Height / 128);
        for (var x = 0; x < source.Width; x += stepX)
        {
            AddSample(x, 0);
            AddSample(x, source.Height - 1);
        }
        for (var y = stepY; y < source.Height - 1; y += stepY)
        {
            AddSample(0, y);
            AddSample(source.Width - 1, y);
        }
        if (red.Count == 0) return (255, 255, 255);
        red.Sort();
        green.Sort();
        blue.Sort();
        var middle = red.Count / 2;
        return (red[middle], green[middle], blue[middle]);

        void AddSample(int x, int y)
        {
            var offset = (y * source.Width + x) * 4;
            if (source.Pixels[offset + 3] < 128) return;
            blue.Add(source.Pixels[offset]);
            green.Add(source.Pixels[offset + 1]);
            red.Add(source.Pixels[offset + 2]);
        }
    }
}
