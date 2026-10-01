namespace TATAPP.Core;

public enum AnatomicalSex
{
    Male,
    Female,
}

public enum AnatomicalView
{
    Front,
    Back,
    Left,
    Right,
}

public enum BodyRegionKind
{
    LeftOuterUpperArm,
    LeftInnerUpperArm,
    RightOuterUpperArm,
    RightInnerUpperArm,
    LeftOuterForearm,
    LeftInnerForearm,
    RightOuterForearm,
    RightInnerForearm,
    LeftWrist,
    RightWrist,
    UpperLeftChest,
    UpperRightChest,
    FullUpperChest,
    FullChestAndAbdomen,
    FullUpperBack,
    FullBack,
    LeftShoulder,
    RightShoulder,
    LeftThigh,
    RightThigh,
    LeftCalf,
    RightCalf,
}

public sealed record BodyRegionDefinition(
    BodyRegionKind Kind,
    string DisplayName,
    string AccessibleDescription,
    AnatomicalView PreferredView,
    double DefaultTattooScalePercent)
{
    public override string ToString() => DisplayName;
}

public static class BodyRegionCatalog
{
    public static BodyRegionKind DefaultRegion => BodyRegionKind.LeftOuterUpperArm;

    public static IReadOnlyList<BodyRegionDefinition> All { get; } =
    [
        new(BodyRegionKind.LeftOuterUpperArm, "Left upper arm — outer surface (deltoid to elbow)",
            "the outer surface of the left upper arm, from the deltoid to the elbow", AnatomicalView.Left, 62),
        new(BodyRegionKind.LeftInnerUpperArm, "Left upper arm — inner surface",
            "the inner surface of the left upper arm", AnatomicalView.Right, 54),
        new(BodyRegionKind.RightOuterUpperArm, "Right upper arm — outer surface (deltoid to elbow)",
            "the outer surface of the right upper arm, from the deltoid to the elbow", AnatomicalView.Right, 62),
        new(BodyRegionKind.RightInnerUpperArm, "Right upper arm — inner surface",
            "the inner surface of the right upper arm", AnatomicalView.Left, 54),
        new(BodyRegionKind.LeftOuterForearm, "Left forearm — outer surface",
            "the outer surface of the left forearm", AnatomicalView.Left, 50),
        new(BodyRegionKind.LeftInnerForearm, "Left forearm — inner surface",
            "the inner surface of the left forearm", AnatomicalView.Right, 46),
        new(BodyRegionKind.RightOuterForearm, "Right forearm — outer surface",
            "the outer surface of the right forearm", AnatomicalView.Right, 50),
        new(BodyRegionKind.RightInnerForearm, "Right forearm — inner surface",
            "the inner surface of the right forearm", AnatomicalView.Left, 46),
        new(BodyRegionKind.LeftWrist, "Left wrist", "the left wrist", AnatomicalView.Front, 34),
        new(BodyRegionKind.RightWrist, "Right wrist", "the right wrist", AnatomicalView.Front, 34),
        new(BodyRegionKind.UpperLeftChest, "Upper left chest", "the upper left chest", AnatomicalView.Front, 44),
        new(BodyRegionKind.UpperRightChest, "Upper right chest", "the upper right chest", AnatomicalView.Front, 44),
        new(BodyRegionKind.FullUpperChest, "Full upper chest", "the full upper chest", AnatomicalView.Front, 78),
        new(BodyRegionKind.FullChestAndAbdomen, "Full chest and abdomen", "the full chest and abdomen", AnatomicalView.Front, 88),
        new(BodyRegionKind.FullUpperBack, "Full upper back", "the full upper back", AnatomicalView.Back, 82),
        new(BodyRegionKind.FullBack, "Full back", "the full back", AnatomicalView.Back, 92),
        new(BodyRegionKind.LeftShoulder, "Left shoulder", "the left shoulder cap", AnatomicalView.Left, 46),
        new(BodyRegionKind.RightShoulder, "Right shoulder", "the right shoulder cap", AnatomicalView.Right, 46),
        new(BodyRegionKind.LeftThigh, "Left thigh", "the front and outer surface of the left thigh", AnatomicalView.Front, 62),
        new(BodyRegionKind.RightThigh, "Right thigh", "the front and outer surface of the right thigh", AnatomicalView.Front, 62),
        new(BodyRegionKind.LeftCalf, "Left calf", "the outer surface of the left calf", AnatomicalView.Back, 48),
        new(BodyRegionKind.RightCalf, "Right calf", "the outer surface of the right calf", AnatomicalView.Back, 48),
    ];

    public static BodyRegionDefinition Get(BodyRegionKind kind) =>
        All.First(region => region.Kind == kind);
}

public readonly record struct SkinTone(byte Red, byte Green, byte Blue, string Description);

public static class AnatomicalDefaults
{
    public const AnatomicalSex Sex = AnatomicalSex.Male;
    public const BodyRegionKind Region = BodyRegionKind.LeftOuterUpperArm;
    public const double HeightCentimeters = 163;
    public const double SkinToneValue = 55;

    public static double PreferredRotation(BodyRegionKind region) =>
        BodyRegionCatalog.Get(region).PreferredView switch
        {
            AnatomicalView.Back => 180,
            AnatomicalView.Left => -62,
            AnatomicalView.Right => 62,
            _ => 0,
        };

    public static string DescribeHeight(double value) =>
        Math.Abs(value - HeightCentimeters) < 0.5
            ? $"{HeightCentimeters:0} centimeters, average Filipino adult"
            : $"{Math.Round(value):0} centimeters";

    public static SkinTone SkinToneFromSlider(double value)
    {
        value = Math.Clamp(value, 0, 100);
        var anchors = new[]
        {
            (Position: 0d, Red: 244d, Green: 215d, Blue: 197d),
            (Position: 28d, Red: 221d, Green: 176d, Blue: 143d),
            (Position: 55d, Red: 185d, Green: 130d, Blue: 90d),
            (Position: 78d, Red: 133d, Green: 82d, Blue: 52d),
            (Position: 100d, Red: 82d, Green: 48d, Blue: 31d),
        };
        var upperIndex = Array.FindIndex(anchors, anchor => anchor.Position >= value);
        if (upperIndex <= 0) upperIndex = 1;
        var lower = anchors[upperIndex - 1];
        var upper = anchors[upperIndex];
        var amount = (value - lower.Position) / (upper.Position - lower.Position);
        var description = value switch
        {
            < 15 => "very light",
            < 40 => "light tan",
            < 68 => "light brown to medium tan, Filipino",
            < 88 => "medium brown",
            _ => "deep brown",
        };
        return new SkinTone(
            Interpolate(lower.Red, upper.Red, amount),
            Interpolate(lower.Green, upper.Green, amount),
            Interpolate(lower.Blue, upper.Blue, amount),
            description);
    }

    private static byte Interpolate(double first, double second, double amount) =>
        (byte)Math.Clamp((int)Math.Round(first + (second - first) * amount), 0, 255);
}
