namespace TATAPP.Core;

public enum PhotoFileFormat
{
    Jpeg,
    Png,
    Bmp,
    Tiff,
    Gif,
}

public sealed record PhotoFileFormatInfo(PhotoFileFormat Format, string DisplayName, string PreferredExtension,
    IReadOnlyList<string> Extensions)
{
    public string DialogFilter => $"{DisplayName} ({string.Join(';', Extensions.Select(value => $"*{value}"))})|{string.Join(';', Extensions.Select(value => $"*{value}"))}";
}

public static class PhotoFileFormats
{
    private static readonly PhotoFileFormatInfo[] Values =
    [
        new(PhotoFileFormat.Jpeg, "JPEG image", ".jpg", [".jpg", ".jpeg"]),
        new(PhotoFileFormat.Png, "PNG image", ".png", [".png"]),
        new(PhotoFileFormat.Bmp, "Bitmap image", ".bmp", [".bmp"]),
        new(PhotoFileFormat.Tiff, "TIFF image", ".tif", [".tif", ".tiff"]),
        new(PhotoFileFormat.Gif, "GIF image", ".gif", [".gif"]),
    ];

    public static IReadOnlyList<PhotoFileFormatInfo> Supported => Values;

    public static PhotoFileFormatInfo FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);
        return Values.FirstOrDefault(value => value.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"The image format '{extension}' is not supported. Choose JPEG, PNG, BMP, TIFF, or GIF.");
    }

    public static PhotoFileFormatInfo Get(PhotoFileFormat format) =>
        Values.Single(value => value.Format == format);

    public static string OpenDialogFilter =>
        "Supported photos (*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.gif|" +
        string.Join('|', Values.Select(value => value.DialogFilter));
}
