using System.IO;
using TATAPP.Core;

namespace TATAPP.App.Imaging;

internal sealed record PhotoDocument(string SourcePath, PhotoFileFormatInfo Format, ImageFrame FullResolution,
    ImageFrame Preview)
{
    public string DisplayName => Path.GetFileName(SourcePath);
}
