using TATAPP.Core;
using TATAPP.Core.Workflow;

namespace TATAPP.AndroidApp;

internal sealed record AndroidPhotoDocument(
    string Id,
    string PrivatePath,
    string? OriginalUri,
    string DisplayName,
    string MimeType,
    PhotoFileFormat Format,
    int PixelWidth,
    int PixelHeight,
    ImageFrame Preview);

internal readonly record struct ExportSnapshot(
    long DocumentGeneration,
    TattooStage Stage,
    double SliderValue,
    PhotoFileFormat OutputFormat,
    bool UsesFallback,
    AnatomicalWorkflowState Anatomy,
    double AnatomyFocusProgress,
    string? AnatomicalSnapshotPath);

internal static class ProductLinks
{
    public const string BlackWidowTattoo = "https://www.facebook.com/grayscaleconsultants";
}
