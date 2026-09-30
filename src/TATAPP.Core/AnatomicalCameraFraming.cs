namespace TATAPP.Core;

/// <summary>
/// Defines the wide context frame and close inspection frame used to explain
/// how a design is placed on a selected anatomical surface.
/// </summary>
public readonly record struct AnatomicalCameraFrame(
    double ContextFocusX,
    double ContextFocusY,
    double ContextDistance,
    double DetailFocusX,
    double DetailFocusY,
    double DetailDistance,
    string ContextDescription);

public static class AnatomicalCameraFraming
{
    public const double OverviewDistance = 25;

    public static AnatomicalCameraFrame ForRegion(BodyRegionKind region) => region switch
    {
        BodyRegionKind.LeftOuterUpperArm or BodyRegionKind.LeftInnerUpperArm =>
            new(0.52, 2.18, 11.4, 0.91, 1.57, 5.4,
                "the head, neck, left shoulder, upper chest, and left upper arm"),
        BodyRegionKind.RightOuterUpperArm or BodyRegionKind.RightInnerUpperArm =>
            new(-0.52, 2.18, 11.4, -0.91, 1.57, 5.4,
                "the head, neck, right shoulder, upper chest, and right upper arm"),
        BodyRegionKind.LeftOuterForearm or BodyRegionKind.LeftInnerForearm =>
            new(0.58, 1.22, 11.8, 0.94, 0.18, 5.0,
                "the left shoulder, upper arm, elbow, forearm, and wrist"),
        BodyRegionKind.RightOuterForearm or BodyRegionKind.RightInnerForearm =>
            new(-0.58, 1.22, 11.8, -0.94, 0.18, 5.0,
                "the right shoulder, upper arm, elbow, forearm, and wrist"),
        BodyRegionKind.LeftWrist =>
            new(0.54, 0.72, 11.5, 0.96, -0.52, 4.5,
                "the left elbow, forearm, wrist, and hand"),
        BodyRegionKind.RightWrist =>
            new(-0.54, 0.72, 11.5, -0.96, -0.52, 4.5,
                "the right elbow, forearm, wrist, and hand"),
        BodyRegionKind.UpperLeftChest =>
            new(0.28, 2.35, 10.6, 0.34, 1.88, 4.9,
                "the head, neck, left shoulder, left upper arm, and upper-left chest"),
        BodyRegionKind.UpperRightChest =>
            new(-0.28, 2.35, 10.6, -0.34, 1.88, 4.9,
                "the head, neck, right shoulder, right upper arm, and upper-right chest"),
        BodyRegionKind.FullUpperChest or BodyRegionKind.FullUpperBack =>
            new(0, 2.26, 10.8, 0, 1.87, 5.3,
                "the head, neck, both shoulders, upper arms, and upper torso"),
        BodyRegionKind.FullChestAndAbdomen or BodyRegionKind.FullBack =>
            new(0, 1.56, 12.0, 0, 1.28, 6.2,
                "the shoulders, chest, abdomen, and upper hips"),
        BodyRegionKind.LeftShoulder =>
            new(0.48, 2.56, 10.5, 0.88, 2.30, 4.7,
                "the head, neck, upper chest, left shoulder, and upper arm"),
        BodyRegionKind.RightShoulder =>
            new(-0.48, 2.56, 10.5, -0.88, 2.30, 4.7,
                "the head, neck, upper chest, right shoulder, and upper arm"),
        BodyRegionKind.LeftThigh =>
            new(0.28, -0.05, 12.8, 0.39, -1.05, 6.2,
                "the lower torso, hips, and left thigh through the knee"),
        BodyRegionKind.RightThigh =>
            new(-0.28, -0.05, 12.8, -0.39, -1.05, 6.2,
                "the lower torso, hips, and right thigh through the knee"),
        BodyRegionKind.LeftCalf =>
            new(0.27, -1.55, 12.6, 0.40, -2.55, 5.5,
                "the left thigh, knee, calf, ankle, and foot"),
        BodyRegionKind.RightCalf =>
            new(-0.27, -1.55, 12.6, -0.40, -2.55, 5.5,
                "the right thigh, knee, calf, ankle, and foot"),
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };
}
