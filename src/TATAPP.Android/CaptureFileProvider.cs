using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Java.IO;
using Uri = Android.Net.Uri;

namespace TATAPP.AndroidApp;

[ContentProvider([Authority], Exported = false, GrantUriPermissions = true)]
public sealed class CaptureFileProvider : ContentProvider
{
    public const string Authority = "com.grayscaleconsultants.tatapp.files";

    public override bool OnCreate() => true;

    public override string GetType(Uri uri)
    {
        _ = Resolve(uri);
        return "image/jpeg";
    }

    public override ParcelFileDescriptor OpenFile(Uri uri, string mode)
    {
        var path = Resolve(uri);
        var flags = mode.Contains('w', StringComparison.Ordinal)
            ? ParcelFileMode.Create | ParcelFileMode.ReadWrite | ParcelFileMode.Truncate
            : ParcelFileMode.ReadOnly;
        return ParcelFileDescriptor.Open(new Java.IO.File(path), flags)
            ?? throw new Java.IO.FileNotFoundException("The camera output could not be opened.");
    }

    public override ICursor Query(Uri uri, string[]? projection, string? selection,
        string[]? selectionArgs, string? sortOrder)
    {
        var path = Resolve(uri);
        var requested = projection is { Length: > 0 }
            ? projection
            : [IOpenableColumns.DisplayName, IOpenableColumns.Size];
        var cursor = new MatrixCursor(requested);
        var row = cursor.NewRow();
        foreach (var column in requested)
        {
            if (column == IOpenableColumns.DisplayName) row?.Add(new Java.Lang.String(System.IO.Path.GetFileName(path)));
            else if (column == IOpenableColumns.Size) row?.Add(Java.Lang.Long.ValueOf(new FileInfo(path).Length));
            else row?.Add(null);
        }
        return cursor;
    }

    public override Uri? Insert(Uri uri, ContentValues? values) =>
        throw new NotSupportedException("Capture files cannot be inserted through this provider.");

    public override int Delete(Uri uri, string? selection, string[]? selectionArgs) =>
        throw new NotSupportedException("Capture files cannot be deleted through this provider.");

    public override int Update(Uri uri, ContentValues? values, string? selection,
        string[]? selectionArgs) =>
        throw new NotSupportedException("Capture files cannot be updated through this provider.");

    private string Resolve(Uri uri)
    {
        if (!string.Equals(uri.Authority, Authority, StringComparison.Ordinal) ||
            uri.PathSegments is not { Count: 2 } segments ||
            !string.Equals(segments[0], "capture", StringComparison.Ordinal))
            throw new Java.IO.FileNotFoundException("Invalid camera output URI.");
        var name = segments[1];
        if (name != System.IO.Path.GetFileName(name) || !name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            throw new Java.IO.FileNotFoundException("Invalid camera output filename.");
        var root = System.IO.Path.Combine(Context?.FilesDir?.AbsolutePath
            ?? throw new Java.IO.FileNotFoundException("App-private storage is unavailable."), "captures");
        Directory.CreateDirectory(root);
        return System.IO.Path.Combine(root, name);
    }
}
