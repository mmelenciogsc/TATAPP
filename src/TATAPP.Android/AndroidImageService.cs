using Android.App;
using Android.Content;
using Android.Database;
using Android.Graphics;
using Android.Media;
using Android.Net;
using Android.Provider;
using TATAPP.Core;
using Uri = Android.Net.Uri;

namespace TATAPP.AndroidApp;

internal interface IAndroidImageService
{
    Task<AndroidPhotoDocument> ImportAsync(Uri source, CancellationToken cancellationToken);
    Task<AndroidPhotoDocument> OpenPrivateAsync(string path, string? originalUri, string displayName,
        CancellationToken cancellationToken);
    Task<(ImageFrame Frame, bool Downsampled)> DecodeForExportAsync(AndroidPhotoDocument document,
        CancellationToken cancellationToken);
    Bitmap ToBitmap(ImageFrame frame);
    Task SaveAsync(ImageFrame frame, Uri destination, PhotoFileFormat format, CancellationToken cancellationToken);
}

internal sealed class AndroidImageService(Context context) : IAndroidImageService
{
    private const long MaximumCompressedBytes = 128L * 1024 * 1024;
    private const long MaximumDecodedPixels = 32_000_000;
    private const int MaximumPreviewDimension = 1600;
    private readonly Context context = context.ApplicationContext ?? context;

    public Task<AndroidPhotoDocument> ImportAsync(Uri source, CancellationToken cancellationToken) =>
        Task.Run(() => Import(source, cancellationToken), cancellationToken);

    public Task<AndroidPhotoDocument> OpenPrivateAsync(string path, string? originalUri, string displayName,
        CancellationToken cancellationToken) => Task.Run(() =>
        OpenPrivate(path, originalUri, displayName, cancellationToken), cancellationToken);

    public Task<(ImageFrame Frame, bool Downsampled)> DecodeForExportAsync(AndroidPhotoDocument document,
        CancellationToken cancellationToken) => Task.Run(() =>
        DecodeForExport(document, cancellationToken), cancellationToken);

    public Bitmap ToBitmap(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var colors = new int[checked(frame.Width * frame.Height)];
        for (var pixel = 0; pixel < colors.Length; pixel++)
        {
            var offset = pixel * 4;
            colors[pixel] = Color.Argb(frame.Pixels[offset + 3], frame.Pixels[offset + 2],
                frame.Pixels[offset + 1], frame.Pixels[offset]);
        }
        var bitmap = Bitmap.CreateBitmap(frame.Width, frame.Height, Bitmap.Config.Argb8888!)
            ?? throw new InvalidDataException("Android could not allocate the preview bitmap.");
        bitmap.SetPixels(colors, 0, frame.Width, 0, 0, frame.Width, frame.Height);
        return bitmap;
    }

    public Task SaveAsync(ImageFrame frame, Uri destination, PhotoFileFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(destination);
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var output = context.ContentResolver?.OpenOutputStream(destination, "wt")
                ?? throw new IOException("The selected Android document cannot be opened for writing.");
            using var bitmap = ToBitmap(frame);
            var compressFormat = format switch
            {
                PhotoFileFormat.Jpeg => Bitmap.CompressFormat.Jpeg!,
                PhotoFileFormat.Png => Bitmap.CompressFormat.Png!,
                _ => throw new NotSupportedException("Android exports this source format as a non-destructive PNG copy."),
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (!bitmap.Compress(compressFormat, format == PhotoFileFormat.Jpeg ? 95 : 100, output))
                throw new IOException("Android's image encoder did not complete the export.");
            cancellationToken.ThrowIfCancellationRequested();
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private AndroidPhotoDocument Import(Uri source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var displayName = ReadDisplayName(source);
        var declaredMime = context.ContentResolver?.GetType(source);
        var imports = System.IO.Path.Combine(context.FilesDir?.AbsolutePath
            ?? throw new IOException("App-private storage is unavailable."), "imports");
        Directory.CreateDirectory(imports);
        var temporary = System.IO.Path.Combine(imports, $".{Guid.NewGuid():N}.partial");
        string? destination = null;
        try
        {
            using (var input = context.ContentResolver?.OpenInputStream(source)
                   ?? throw new IOException("The selected photo cannot be opened."))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                CopyBounded(input, output, cancellationToken);

            var format = DetectFormat(temporary);
            ValidateDeclaredMime(declaredMime, format);
            var extension = PhotoFileFormats.Get(format).PreferredExtension;
            destination = System.IO.Path.Combine(imports, $"{Guid.NewGuid():N}{extension}");
            File.Move(temporary, destination);
            return OpenPrivate(destination, source.ToString(), displayName, cancellationToken);
        }
        catch
        {
            if (destination is not null) TryDelete(destination);
            throw;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static AndroidPhotoDocument OpenPrivate(string path, string? originalUri, string displayName,
        CancellationToken cancellationToken)
    {
        var absolute = System.IO.Path.GetFullPath(path);
        if (!File.Exists(absolute)) throw new FileNotFoundException("The imported photo is no longer available.", absolute);
        var format = DetectFormat(absolute);
        var bounds = ReadBounds(absolute);
        ValidateDimensions(bounds.Width, bounds.Height);
        using var decoded = DecodeOriented(absolute, CalculateSampleSize(bounds.Width, bounds.Height,
            MaximumPreviewDimension), MaximumPreviewDimension, cancellationToken);
        var preview = ToImageFrame(decoded, cancellationToken);
        return new AndroidPhotoDocument(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(absolute + new FileInfo(absolute).Length))).ToLowerInvariant(),
            absolute,
            originalUri,
            SanitizeDisplayName(displayName, format),
            MimeFor(format),
            format,
            bounds.Width,
            bounds.Height,
            preview);
    }

    private (ImageFrame Frame, bool Downsampled) DecodeForExport(AndroidPhotoDocument document,
        CancellationToken cancellationToken)
    {
        var activityManager = (ActivityManager?)context.GetSystemService(Context.ActivityService);
        var memoryClassBytes = Math.Max(128L, activityManager?.MemoryClass ?? 128) * 1024L * 1024L;
        var estimatedWorkingBytes = checked((long)document.PixelWidth * document.PixelHeight * 20L);
        if (estimatedWorkingBytes > memoryClassBytes * 45L / 100L)
            return (document.Preview.Clone(), true);

        using var bitmap = DecodeOriented(document.PrivatePath, 1, int.MaxValue, cancellationToken);
        return (ToImageFrame(bitmap, cancellationToken), false);
    }

    private static void CopyBounded(System.IO.Stream input, System.IO.Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > MaximumCompressedBytes)
                throw new InvalidDataException("The selected file exceeds TATAPP's 128-megabyte compressed-input limit.");
            output.Write(buffer, 0, read);
        }
        output.Flush();
    }

    private static (int Width, int Height) ReadBounds(string path)
    {
        var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        _ = BitmapFactory.DecodeFile(path, options);
        if (options.OutWidth <= 0 || options.OutHeight <= 0)
            throw new InvalidDataException("The selected file is corrupt or uses an image encoding Android cannot decode.");
        var orientation = ReadOrientation(path);
        return orientation is 5 or 6 or 7 or 8
            ? (options.OutHeight, options.OutWidth)
            : (options.OutWidth, options.OutHeight);
    }

    private static void ValidateDimensions(int width, int height)
    {
        var pixels = checked((long)width * height);
        if (pixels > MaximumDecodedPixels)
            throw new InvalidDataException(
                $"This image contains {pixels:N0} pixels. TATAPP's safe decoded-image limit is {MaximumDecodedPixels:N0} pixels.");
    }

    private static int CalculateSampleSize(int width, int height, int maximumDimension)
    {
        var sample = 1;
        while (Math.Max(width / sample, height / sample) > maximumDimension && sample <= 32) sample *= 2;
        return sample;
    }

    private static Bitmap DecodeOriented(string path, int sampleSize, int maximumDimension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new BitmapFactory.Options
        {
            InPreferredConfig = Bitmap.Config.Argb8888,
            InSampleSize = Math.Max(1, sampleSize),
        };
        using var decoded = BitmapFactory.DecodeFile(path, options)
            ?? throw new InvalidDataException("Android could not decode the selected image.");
        cancellationToken.ThrowIfCancellationRequested();
        var orientation = ReadOrientation(path);
        using var matrix = OrientationMatrix(orientation);
        var oriented = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)
            ?? throw new InvalidDataException("Android could not normalize the photo orientation.");

        // Drawing into an ARGB_8888 target normalizes supported embedded color
        // profiles to Android's display color space and removes hardware-backed storage.
        var normalized = Bitmap.CreateBitmap(oriented.Width, oriented.Height, Bitmap.Config.Argb8888!)
            ?? throw new InvalidDataException("Android could not allocate a color-normalized bitmap.");
        using var canvas = new Canvas(normalized);
        canvas.DrawBitmap(oriented, 0, 0, null);
        var largest = Math.Max(normalized.Width, normalized.Height);
        if (largest > maximumDimension)
        {
            var ratio = maximumDimension / (double)largest;
            var width = Math.Max(1, (int)Math.Floor(normalized.Width * ratio));
            var height = Math.Max(1, (int)Math.Floor(normalized.Height * ratio));
            var scaled = Bitmap.CreateScaledBitmap(normalized, width, height, true)
                ?? throw new InvalidDataException("Android could not create the bounded preview bitmap.");
            normalized.Dispose();
            return scaled;
        }
        return normalized;
    }

    private static ImageFrame ToImageFrame(Bitmap bitmap, CancellationToken cancellationToken)
    {
        var colors = new int[checked(bitmap.Width * bitmap.Height)];
        bitmap.GetPixels(colors, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        var pixels = new byte[checked(colors.Length * 4)];
        for (var pixel = 0; pixel < colors.Length; pixel++)
        {
            if ((pixel & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var color = colors[pixel];
            var offset = pixel * 4;
            pixels[offset] = (byte)Color.GetBlueComponent(color);
            pixels[offset + 1] = (byte)Color.GetGreenComponent(color);
            pixels[offset + 2] = (byte)Color.GetRedComponent(color);
            pixels[offset + 3] = (byte)Color.GetAlphaComponent(color);
        }
        return new ImageFrame(bitmap.Width, bitmap.Height, pixels);
    }

#pragma warning disable CS0618
    private static int ReadOrientation(string path)
    {
        try
        {
            using var exif = new ExifInterface(path);
            return exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Android.Media.Orientation.Normal);
        }
        catch (IOException)
        {
            return (int)Android.Media.Orientation.Normal;
        }
    }
#pragma warning restore CS0618

    private static Matrix OrientationMatrix(int orientation)
    {
        var matrix = new Matrix();
        switch ((Android.Media.Orientation)orientation)
        {
            case Android.Media.Orientation.FlipHorizontal: matrix.SetScale(-1, 1); break;
            case Android.Media.Orientation.Rotate180: matrix.SetRotate(180); break;
            case Android.Media.Orientation.FlipVertical: matrix.SetScale(1, -1); break;
            case Android.Media.Orientation.Transpose: matrix.SetScale(-1, 1); matrix.PostRotate(90); break;
            case Android.Media.Orientation.Rotate90: matrix.SetRotate(90); break;
            case Android.Media.Orientation.Transverse: matrix.SetScale(-1, 1); matrix.PostRotate(270); break;
            case Android.Media.Orientation.Rotate270: matrix.SetRotate(270); break;
        }
        return matrix;
    }

    private string ReadDisplayName(Uri uri)
    {
        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            return System.IO.Path.GetFileName(uri.Path) ?? "photo";
        using var cursor = context.ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor is not null && cursor.MoveToFirst())
        {
            var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
            if (index >= 0 && !cursor.IsNull(index)) return cursor.GetString(index) ?? "photo";
        }
        return "photo";
    }

    private static PhotoFileFormat DetectFormat(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var count = stream.Read(header);
        if (count >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff) return PhotoFileFormat.Jpeg;
        if (count >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return PhotoFileFormat.Png;
        if (count >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return PhotoFileFormat.Gif;
        if (count >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M') return PhotoFileFormat.Bmp;
        if (count >= 4 && ((header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 42 && header[3] == 0) ||
                           (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0 && header[3] == 42)))
            return PhotoFileFormat.Tiff;
        throw new InvalidDataException("The selected file signature is not JPEG, PNG, BMP, TIFF, or GIF.");
    }

    private static string MimeFor(PhotoFileFormat format) => format switch
    {
        PhotoFileFormat.Jpeg => "image/jpeg",
        PhotoFileFormat.Png => "image/png",
        PhotoFileFormat.Bmp => "image/bmp",
        PhotoFileFormat.Tiff => "image/tiff",
        PhotoFileFormat.Gif => "image/gif",
        _ => "application/octet-stream",
    };

    private static void ValidateDeclaredMime(string? declaredMime, PhotoFileFormat detectedFormat)
    {
        if (string.IsNullOrWhiteSpace(declaredMime) ||
            string.Equals(declaredMime, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return;
        if (!declaredMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected document is not declared as an image by its provider.");

        var accepted = detectedFormat switch
        {
            PhotoFileFormat.Jpeg => new[] { "image/jpeg", "image/jpg", "image/pjpeg" },
            PhotoFileFormat.Png => new[] { "image/png", "image/x-png" },
            PhotoFileFormat.Bmp => new[] { "image/bmp", "image/x-bmp", "image/x-ms-bmp" },
            PhotoFileFormat.Tiff => new[] { "image/tiff", "image/tif", "image/x-tiff" },
            PhotoFileFormat.Gif => new[] { "image/gif" },
            _ => Array.Empty<string>(),
        };
        if (!accepted.Contains(declaredMime, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The provider reports {declaredMime}, but the file signature is {MimeFor(detectedFormat)}.");
    }

    internal static string SanitizeDisplayName(string? value, PhotoFileFormat format)
    {
        var name = System.IO.Path.GetFileName(value ?? string.Empty);
        var safe = new string(name.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? $"tattoo-design{PhotoFileFormats.Get(format).PreferredExtension}" : safe;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (Java.Lang.SecurityException) { }
    }
}
