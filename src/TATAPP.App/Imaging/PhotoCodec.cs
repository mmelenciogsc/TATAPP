using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TATAPP.App.OfflineAI;
using TATAPP.Core;

namespace TATAPP.App.Imaging;

public static class PhotoCodec
{
    private const int MaximumPreviewDimension = 1600;
    public const long MaximumDecodedPixels = 32_000_000;
    private const ulong WindowsAndAssistiveTechnologyReserveBytes = 2UL * 1_073_741_824;

    internal static PhotoDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var absolutePath = Path.GetFullPath(path);
        var format = PhotoFileFormats.FromPath(absolutePath);
        using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1_048_576, FileOptions.SequentialScan);
        // Inspect dimensions before asking WPF to materialize all decoded
        // pixels. This prevents a malformed or enormous image from exhausting
        // the desktop before the normal preview bounds can take effect.
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The selected file contains no image frame.");
        var memory = OfflineAiHardwareDetector.DetectMemory();
        EnsureImageDimensionsAreSafe(decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight,
            memory.Available, preparingDerivedImage: false);
        var oriented = ApplyExifOrientation(decoder.Frames[0]);
        var full = ToImageFrame(oriented);

        BitmapSource previewSource = oriented;
        var largest = Math.Max(oriented.PixelWidth, oriented.PixelHeight);
        if (largest > MaximumPreviewDimension)
        {
            var scale = MaximumPreviewDimension / (double)largest;
            var transformed = new TransformedBitmap(oriented, new ScaleTransform(scale, scale));
            transformed.Freeze();
            previewSource = transformed;
        }
        return new PhotoDocument(absolutePath, format, full, ToImageFrame(previewSource));
    }

    public static void EnsureImageDimensionsAreSafe(int width, int height, ulong availableMemoryBytes,
        bool preparingDerivedImage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var pixels = checked((long)width * height);
        if (pixels > MaximumDecodedPixels)
            throw new InvalidDataException(
                $"This image contains {pixels:N0} pixels. TATAPP's memory-safe limit is " +
                $"{MaximumDecodedPixels:N0} pixels; resize the isolated artwork and try again.");

        // Loading commonly needs a decoder surface plus BGRA pixels. Derived
        // line/outline work also owns luminance, blur, edge and output buffers.
        var bytesPerPixel = preparingDerivedImage ? 16UL : 8UL;
        var workingBytes = checked((ulong)pixels * bytesPerPixel);
        var required = checked(WindowsAndAssistiveTechnologyReserveBytes + workingBytes);
        if (availableMemoryBytes < required)
            throw new InvalidDataException(
                $"TATAPP stopped before allocating this {width} by {height} image because " +
                $"{availableMemoryBytes / 1_073_741_824d:0.0} gigabytes are available. " +
                $"At least {required / 1_073_741_824d:0.0} gigabytes are required so Windows and the screenreader retain a two-gigabyte reserve.");
    }

    public static void EnsureProcessingHeadroom(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var memory = OfflineAiHardwareDetector.DetectMemory();
        EnsureImageDimensionsAreSafe(frame.Width, frame.Height, memory.Available,
            preparingDerivedImage: true);
    }

    public static BitmapSource ToBitmapSource(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, frame.DpiX, frame.DpiY,
            PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static string ToPngBase64(ImageFrame frame, int maximumDimension)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDimension);
        BitmapSource source = ToBitmapSource(frame);
        var longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest > maximumDimension)
        {
            var scale = maximumDimension / (double)longest;
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            resized.Freeze();
            source = resized;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public static void Save(ImageFrame frame, string destination, PhotoFileFormat format)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var absolutePath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var partial = absolutePath + ".partial";
        try
        {
            var bitmap = ToBitmapSource(frame);
            BitmapEncoder encoder = format switch
            {
                PhotoFileFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = 95 },
                PhotoFileFormat.Png => new PngBitmapEncoder(),
                PhotoFileFormat.Bmp => new BmpBitmapEncoder(),
                PhotoFileFormat.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
                PhotoFileFormat.Gif => new GifBitmapEncoder(),
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            BitmapSource encodedSource = bitmap;
            if (format == PhotoFileFormat.Jpeg)
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
                converted.Freeze();
                encodedSource = converted;
            }
            encoder.Frames.Add(BitmapFrame.Create(encodedSource));
            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                       1_048_576, FileOptions.WriteThrough))
                encoder.Save(stream);
            File.Move(partial, absolutePath, true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    public static ImageFrame ToImageFrame(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        return new ImageFrame(converted.PixelWidth, converted.PixelHeight, pixels,
            PositiveDpi(converted.DpiX), PositiveDpi(converted.DpiY));
    }

    private static BitmapSource ApplyExifOrientation(BitmapFrame frame)
    {
        var orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new ScaleTransform(-1, 1), new RotateTransform(90)),
            6 => new RotateTransform(90),
            7 => Group(new ScaleTransform(-1, 1), new RotateTransform(270)),
            8 => new RotateTransform(270),
            _ => null,
        };
        if (transform is null)
        {
            frame.Freeze();
            return frame;
        }
        var result = new TransformedBitmap(frame, transform);
        result.Freeze();
        return result;
    }

    private static ushort ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.GetQuery(query) is ushort value) return value;
            }
            catch (NotSupportedException) { }
        }
        return 1;
    }

    private static TransformGroup Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms) group.Children.Add(transform);
        group.Freeze();
        return group;
    }

    private static double PositiveDpi(double value) => double.IsFinite(value) && value > 0 ? value : 96;
}
