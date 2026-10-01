namespace TATAPP.Core;

public enum TattooStageKind
{
    Original,
    ColorFade,
    Grayscale,
    Binarized,
    LineArt,
    ThickOutline,
    MediumOutline,
    FineOutline,
    AnatomicalFineOutline,
    AnatomicalMediumOutline,
    AnatomicalThickOutline,
    AnatomicalLineArt,
    AnatomicalBinarized,
    AnatomicalGrayscale,
    AnatomicalColorFade,
    AnatomicalFullColor,
}

public sealed record TattooStage(
    TattooStageKind Kind,
    string Name,
    string Description,
    double RepresentativeSliderValue,
    double SourceImageSliderValue,
    bool IsAnatomicalPlacement);

public static class TattooStageCatalog
{
    private static readonly TattooStage Original = Image(TattooStageKind.Original, "Original image",
        "Original color, texture, and detail.", 0, 0);
    private static readonly TattooStage ColorFade = Image(TattooStageKind.ColorFade, "Colors fading",
        "Color gradually fades while texture and detail remain visible.", 7, 12);
    private static readonly TattooStage Grayscale = Image(TattooStageKind.Grayscale, "Grayscale",
        "The image is fully grayscale and beginning to simplify.", 14, 23);
    private static readonly TattooStage Binarized = Image(TattooStageKind.Binarized, "Binarized stencil",
        "Tones reduce toward printable black and white regions.", 21, 35);
    private static readonly TattooStage LineArt = Image(TattooStageKind.LineArt, "Line art",
        "Detail is represented primarily by dark lines on white.", 28, 48);
    private static readonly TattooStage ThickOutline = Image(TattooStageKind.ThickOutline, "Thick outline",
        "The strongest printable outline uses its thickest stroke.", 35, 62);
    private static readonly TattooStage MediumOutline = Image(TattooStageKind.MediumOutline, "Medium outline",
        "The printable outline uses a balanced medium stroke.", 43, 78);
    private static readonly TattooStage FineOutline = Image(TattooStageKind.FineOutline, "Fine outline",
        "The printable outline uses its thinnest detailed stroke.", 51, 91);

    private static readonly TattooStage AnatomicalFineOutline = Placement(
        TattooStageKind.AnatomicalFineOutline, "Placement — fine outline",
        "The thinnest outline is previewed as tattoo ink wrapping around the selected body surface.", 59, 91);
    private static readonly TattooStage AnatomicalMediumOutline = Placement(
        TattooStageKind.AnatomicalMediumOutline, "Placement — medium outline",
        "A medium-weight outline is previewed as tattoo ink wrapping around the selected body surface.", 65, 78);
    private static readonly TattooStage AnatomicalThickOutline = Placement(
        TattooStageKind.AnatomicalThickOutline, "Placement — thick outline",
        "The thickest outline is previewed as tattoo ink wrapping around the selected body surface.", 71, 62);
    private static readonly TattooStage AnatomicalLineArt = Placement(
        TattooStageKind.AnatomicalLineArt, "Placement — line art",
        "The detailed line-art rendering is previewed as tattoo ink on the selected body surface.", 77, 48);
    private static readonly TattooStage AnatomicalBinarized = Placement(
        TattooStageKind.AnatomicalBinarized, "Placement — black and white",
        "The printable black-and-white rendering is previewed as tattoo ink on the selected body surface.", 83, 35);
    private static readonly TattooStage AnatomicalGrayscale = Placement(
        TattooStageKind.AnatomicalGrayscale, "Placement — grayscale",
        "The grayscale design is previewed as shaded tattoo ink wrapping around the selected body surface.", 89, 23);
    private static readonly TattooStage AnatomicalColorFade = Placement(
        TattooStageKind.AnatomicalColorFade, "Placement — returning color",
        "Partially restored color, texture, and detail are previewed as tattoo ink on the selected body surface.", 95, 12);
    private static readonly TattooStage AnatomicalFullColor = Placement(
        TattooStageKind.AnatomicalFullColor, "Placement — full color",
        "The full original color, texture, and detail are previewed as tattoo ink wrapping around the selected body surface.", 100, 0);

    public static IReadOnlyList<TattooStage> All { get; } =
    [
        Original,
        ColorFade,
        Grayscale,
        Binarized,
        LineArt,
        ThickOutline,
        MediumOutline,
        FineOutline,
        AnatomicalFineOutline,
        AnatomicalMediumOutline,
        AnatomicalThickOutline,
        AnatomicalLineArt,
        AnatomicalBinarized,
        AnatomicalGrayscale,
        AnatomicalColorFade,
        AnatomicalFullColor,
    ];

    public static IReadOnlyList<TattooStage> ImageStages { get; } =
        All.Where(stage => !stage.IsAnatomicalPlacement).ToArray();

    public static IReadOnlyList<TattooStage> AnatomicalStages { get; } =
        All.Where(stage => stage.IsAnatomicalPlacement).ToArray();

    public static TattooStage FromSlider(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        value = Math.Clamp(value, 0, 100);
        if (value < 0.5) return Original;
        if (value < 11) return ColorFade;
        if (value < 18) return Grayscale;
        if (value < 25) return Binarized;
        if (value < 32) return LineArt;
        if (value < 40) return ThickOutline;
        if (value < 48) return MediumOutline;
        if (value < 55) return FineOutline;
        if (value < 62) return AnatomicalFineOutline;
        if (value < 68) return AnatomicalMediumOutline;
        if (value < 74) return AnatomicalThickOutline;
        if (value < 80) return AnatomicalLineArt;
        if (value < 86) return AnatomicalBinarized;
        if (value < 92) return AnatomicalGrayscale;
        if (value < 98) return AnatomicalColorFade;
        return AnatomicalFullColor;
    }

    public static ImageFrame RenderDesign(ImageFrame source, TattooStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stage);
        return TattooImageProcessor.Render(source, stage.SourceImageSliderValue, cancellationToken);
    }

    private static TattooStage Image(TattooStageKind kind, string name, string description,
        double representativeSliderValue, double sourceImageSliderValue) =>
        new(kind, name, description, representativeSliderValue, sourceImageSliderValue, false);

    private static TattooStage Placement(TattooStageKind kind, string name, string description,
        double representativeSliderValue, double sourceImageSliderValue) =>
        new(kind, name, description, representativeSliderValue, sourceImageSliderValue, true);
}
