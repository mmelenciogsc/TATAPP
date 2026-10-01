namespace TATAPP.Core;

/// <summary>
/// Produces a deterministic continuum from the original image to a fine tattoo outline.
/// Later stages intentionally use black marks on opaque white for thermal stencil printing.
/// </summary>
public static class TattooImageProcessor
{
    public static ImageFrame Render(ImageFrame source, double sliderValue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(sliderValue)) throw new ArgumentOutOfRangeException(nameof(sliderValue));
        sliderValue = Math.Clamp(sliderValue, 0, 100);
        if (sliderValue <= 0) return source.Clone();
        var luma = CreateLuminance(source, cancellationToken);
        if (sliderValue <= 20)
            return FadeToGrayscale(source, luma, sliderValue / 20d, cancellationToken);

        var threshold = OtsuThreshold(luma);
        var binary = CreateBinary(luma, threshold);
        if (sliderValue <= 36)
            return FromSingleChannel(source, Blend(luma, binary, (sliderValue - 20d) / 16d), cancellationToken);

        var blurred = BoxBlur(luma, source.Width, source.Height, cancellationToken);
        var (lineArt, edgeMask) = CreateEdges(blurred, binary, source.Width, source.Height, cancellationToken);
        if (sliderValue <= 52)
            return FromSingleChannel(source, Blend(binary, lineArt, (sliderValue - 36d) / 16d), cancellationToken);

        var outlineProgress = (sliderValue - 52d) / 43d;
        var radius = (int)Math.Round((1d - outlineProgress) * 4d, MidpointRounding.AwayFromZero);
        var outline = DilateEdges(edgeMask, source.Width, source.Height, radius, cancellationToken);
        return FromEdgeMask(source, outline, cancellationToken);
    }

    private static byte[] CreateLuminance(ImageFrame source, CancellationToken cancellationToken)
    {
        var result = new byte[checked(source.Width * source.Height)];
        for (var pixel = 0; pixel < result.Length; pixel++)
        {
            if ((pixel & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = pixel * 4;
            var alpha = source.Pixels[offset + 3];
            var blue = CompositeOnWhite(source.Pixels[offset], alpha);
            var green = CompositeOnWhite(source.Pixels[offset + 1], alpha);
            var red = CompositeOnWhite(source.Pixels[offset + 2], alpha);
            result[pixel] = (byte)((54 * red + 183 * green + 19 * blue + 128) >> 8);
        }
        return result;
    }

    private static byte CompositeOnWhite(byte channel, byte alpha) =>
        (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

    private static ImageFrame FadeToGrayscale(ImageFrame source, byte[] luma, double amount,
        CancellationToken cancellationToken)
    {
        var pixels = new byte[source.Pixels.Length];
        for (var pixel = 0; pixel < luma.Length; pixel++)
        {
            if ((pixel & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = pixel * 4;
            var alpha = source.Pixels[offset + 3];
            var gray = luma[pixel];
            pixels[offset] = Lerp(CompositeOnWhite(source.Pixels[offset], alpha), gray, amount);
            pixels[offset + 1] = Lerp(CompositeOnWhite(source.Pixels[offset + 1], alpha), gray, amount);
            pixels[offset + 2] = Lerp(CompositeOnWhite(source.Pixels[offset + 2], alpha), gray, amount);
            pixels[offset + 3] = 255;
        }
        return new ImageFrame(source.Width, source.Height, pixels, source.DpiX, source.DpiY);
    }

    private static byte[] Blend(byte[] first, byte[] second, double amount)
    {
        var result = new byte[first.Length];
        for (var index = 0; index < result.Length; index++) result[index] = Lerp(first[index], second[index], amount);
        return result;
    }

    private static ImageFrame FromSingleChannel(ImageFrame source, byte[] values, CancellationToken cancellationToken)
    {
        var pixels = new byte[source.Pixels.Length];
        for (var pixel = 0; pixel < values.Length; pixel++)
        {
            if ((pixel & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var offset = pixel * 4;
            pixels[offset] = values[pixel];
            pixels[offset + 1] = values[pixel];
            pixels[offset + 2] = values[pixel];
            pixels[offset + 3] = 255;
        }
        return new ImageFrame(source.Width, source.Height, pixels, source.DpiX, source.DpiY);
    }

    private static ImageFrame FromEdgeMask(ImageFrame source, bool[] edges, CancellationToken cancellationToken)
    {
        var pixels = new byte[source.Pixels.Length];
        for (var pixel = 0; pixel < edges.Length; pixel++)
        {
            if ((pixel & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = edges[pixel] ? (byte)0 : (byte)255;
            var offset = pixel * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        return new ImageFrame(source.Width, source.Height, pixels, source.DpiX, source.DpiY);
    }

    private static int OtsuThreshold(byte[] values)
    {
        Span<int> histogram = stackalloc int[256];
        foreach (var value in values) histogram[value]++;
        long sum = 0;
        for (var index = 0; index < histogram.Length; index++) sum += (long)index * histogram[index];

        long backgroundSum = 0;
        var backgroundWeight = 0;
        var bestVariance = -1d;
        var bestThreshold = 128;
        for (var threshold = 0; threshold < 256; threshold++)
        {
            backgroundWeight += histogram[threshold];
            if (backgroundWeight == 0) continue;
            var foregroundWeight = values.Length - backgroundWeight;
            if (foregroundWeight == 0) break;
            backgroundSum += (long)threshold * histogram[threshold];
            var backgroundMean = backgroundSum / (double)backgroundWeight;
            var foregroundMean = (sum - backgroundSum) / (double)foregroundWeight;
            var difference = backgroundMean - foregroundMean;
            var variance = backgroundWeight * (double)foregroundWeight * difference * difference;
            if (variance <= bestVariance) continue;
            bestVariance = variance;
            bestThreshold = threshold;
        }
        return Math.Clamp(bestThreshold, 48, 216);
    }

    private static byte[] CreateBinary(byte[] luma, int threshold)
    {
        var result = new byte[luma.Length];
        for (var index = 0; index < result.Length; index++) result[index] = luma[index] <= threshold ? (byte)0 : (byte)255;
        return result;
    }

    private static byte[] BoxBlur(byte[] source, int width, int height, CancellationToken cancellationToken)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                var count = 0;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var sampleY = Math.Clamp(y + offsetY, 0, height - 1);
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var sampleX = Math.Clamp(x + offsetX, 0, width - 1);
                        sum += source[sampleY * width + sampleX];
                        count++;
                    }
                }
                result[y * width + x] = (byte)(sum / count);
            }
        }
        return result;
    }

    private static (byte[] LineArt, bool[] EdgeMask) CreateEdges(byte[] luma, byte[] binary, int width, int height,
        CancellationToken cancellationToken)
    {
        var lineArt = Enumerable.Repeat((byte)255, luma.Length).ToArray();
        var edgeMask = new bool[luma.Length];
        if (width < 3 || height < 3) return (lineArt, edgeMask);

        for (var y = 1; y < height - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;
                var topLeft = luma[index - width - 1];
                var top = luma[index - width];
                var topRight = luma[index - width + 1];
                var left = luma[index - 1];
                var right = luma[index + 1];
                var bottomLeft = luma[index + width - 1];
                var bottom = luma[index + width];
                var bottomRight = luma[index + width + 1];
                var gradientX = -topLeft + topRight - 2 * left + 2 * right - bottomLeft + bottomRight;
                var gradientY = -topLeft - 2 * top - topRight + bottomLeft + 2 * bottom + bottomRight;
                var magnitude = Math.Min(255, (Math.Abs(gradientX) + Math.Abs(gradientY)) / 4);
                lineArt[index] = (byte)(255 - Math.Clamp((magnitude - 12) * 2, 0, 255));
                var binaryBoundary = binary[index] != binary[index - 1] || binary[index] != binary[index + 1]
                    || binary[index] != binary[index - width] || binary[index] != binary[index + width];
                edgeMask[index] = magnitude >= 36 || binaryBoundary;
            }
        }
        return (lineArt, edgeMask);
    }

    private static bool[] DilateEdges(bool[] source, int width, int height, int radius,
        CancellationToken cancellationToken)
    {
        if (radius <= 0) return (bool[])source.Clone();
        var horizontal = new bool[source.Length];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = y * width;
            var count = 0;
            for (var sampleX = 0; sampleX <= Math.Min(radius, width - 1); sampleX++)
                if (source[row + sampleX]) count++;
            for (var x = 0; x < width; x++)
            {
                horizontal[row + x] = count > 0;
                var removeX = x - radius;
                if (removeX >= 0 && source[row + removeX]) count--;
                var addX = x + radius + 1;
                if (addX < width && source[row + addX]) count++;
            }
        }

        var result = new bool[source.Length];
        for (var x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = 0;
            for (var sampleY = 0; sampleY <= Math.Min(radius, height - 1); sampleY++)
                if (horizontal[sampleY * width + x]) count++;
            for (var y = 0; y < height; y++)
            {
                result[y * width + x] = count > 0;
                var removeY = y - radius;
                if (removeY >= 0 && horizontal[removeY * width + x]) count--;
                var addY = y + radius + 1;
                if (addY < height && horizontal[addY * width + x]) count++;
            }
        }
        return result;
    }

    private static byte Lerp(byte first, byte second, double amount) =>
        (byte)Math.Clamp((int)Math.Round(first + (second - first) * Math.Clamp(amount, 0, 1)), 0, 255);
}
