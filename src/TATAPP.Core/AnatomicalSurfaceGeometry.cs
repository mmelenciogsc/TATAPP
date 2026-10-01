using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TATAPP.Core;

/// <summary>
/// Platform-neutral coordinates used by the generated anatomical mesh. The
/// reference mannequin is expressed in the same model space as the Windows
/// renderer and is scaled from the repository's 163-centimeter baseline.
/// </summary>
public readonly record struct AnatomicalPoint3(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}

/// <summary>Normalized tattoo coordinates. U runs left-to-right and V top-to-bottom.</summary>
public readonly record struct AnatomicalSurfacePoint(double U, double V)
{
    public bool IsNormalized => double.IsFinite(U) && double.IsFinite(V) &&
                                U is >= 0 and <= 1 && V is >= 0 and <= 1;
}

public readonly record struct AnatomicalVertex(
    AnatomicalPoint3 Position,
    AnatomicalPoint3 Normal,
    AnatomicalSurfacePoint SurfacePoint);

public readonly record struct AnatomicalBounds3(AnatomicalPoint3 Minimum, AnatomicalPoint3 Maximum)
{
    public AnatomicalPoint3 Center => new(
        (Minimum.X + Maximum.X) / 2,
        (Minimum.Y + Maximum.Y) / 2,
        (Minimum.Z + Maximum.Z) / 2);
}

/// <summary>
/// Immutable indexed triangles consumable by Android Canvas/OpenGL, WPF, or a
/// deterministic software renderer without taking a dependency on platform graphics types.
/// </summary>
public sealed class AnatomicalTriangleMesh
{
    internal AnatomicalTriangleMesh(ImmutableArray<AnatomicalVertex> vertices,
        ImmutableArray<int> triangleIndices)
    {
        if (vertices.IsDefaultOrEmpty) throw new ArgumentException("A mesh requires vertices.", nameof(vertices));
        if (triangleIndices.IsDefaultOrEmpty || triangleIndices.Length % 3 != 0)
            throw new ArgumentException("A mesh requires complete indexed triangles.", nameof(triangleIndices));
        if (vertices.Any(vertex => !vertex.Position.IsFinite || !vertex.Normal.IsFinite ||
                                   !vertex.SurfacePoint.IsNormalized))
            throw new ArgumentException("Mesh coordinates must be finite and texture coordinates normalized.",
                nameof(vertices));
        if (triangleIndices.Any(index => index < 0 || index >= vertices.Length))
            throw new ArgumentException("A triangle index lies outside the vertex collection.",
                nameof(triangleIndices));

        Vertices = vertices;
        TriangleIndices = triangleIndices;
        Bounds = CalculateBounds(vertices);
    }

    public ImmutableArray<AnatomicalVertex> Vertices { get; }
    public ImmutableArray<int> TriangleIndices { get; }
    public AnatomicalBounds3 Bounds { get; }

    private static AnatomicalBounds3 CalculateBounds(ImmutableArray<AnatomicalVertex> vertices)
    {
        var first = vertices[0].Position;
        var minX = first.X;
        var minY = first.Y;
        var minZ = first.Z;
        var maxX = first.X;
        var maxY = first.Y;
        var maxZ = first.Z;
        foreach (var vertex in vertices.AsSpan()[1..])
        {
            minX = Math.Min(minX, vertex.Position.X);
            minY = Math.Min(minY, vertex.Position.Y);
            minZ = Math.Min(minZ, vertex.Position.Z);
            maxX = Math.Max(maxX, vertex.Position.X);
            maxY = Math.Max(maxY, vertex.Position.Y);
            maxZ = Math.Max(maxZ, vertex.Position.Z);
        }
        return new(new(minX, minY, minZ), new(maxX, maxY, maxZ));
    }
}

public enum AnatomicalBodySegmentKind
{
    Head,
    Neck,
    Torso,
    Hips,
    LeftShoulder,
    LeftUpperArm,
    LeftForearm,
    LeftWrist,
    LeftHand,
    RightShoulder,
    RightUpperArm,
    RightForearm,
    RightWrist,
    RightHand,
    LeftThigh,
    LeftCalf,
    LeftFoot,
    RightThigh,
    RightCalf,
    RightFoot,
}

public enum AnatomicalLaterality
{
    Center,
    Left,
    Right,
}

public enum AnatomicalSurfaceSide
{
    Front,
    Back,
    Inner,
    Outer,
    Circumferential,
    FrontOuter,
    BackOuter,
}

public sealed record AnatomicalBodySegment(
    AnatomicalBodySegmentKind Kind,
    AnatomicalTriangleMesh Mesh,
    ImmutableArray<BodyRegionKind> SelectableRegions);

/// <summary>
/// A distinct selectable tattoo surface. Its vertices carry normalized texture
/// coordinates, while camera and orientation metadata keep visual and accessible
/// selection paths synchronized.
/// </summary>
public sealed record AnatomicalPlacementSurface(
    AnatomicalSex Sex,
    BodyRegionKind Region,
    AnatomicalLaterality Laterality,
    AnatomicalSurfaceSide Side,
    AnatomicalTriangleMesh Mesh,
    AnatomicalCameraFrame Camera,
    double PreferredRotationDegrees,
    double DefaultTattooScale)
{
    public string GeometryCacheKey => FormattableString.Invariant(
        $"anatomy-geometry-v{AnatomicalGeometryCatalog.GeometryRevision}:{Sex}:{Region}");
}

public readonly record struct AnatomicalRenderPose(
    double UniformScale,
    double RotationDegrees,
    double CameraDistance);

public sealed class AnatomicalGeometryModel
{
    private readonly FrozenDictionary<BodyRegionKind, AnatomicalPlacementSurface> placementsByRegion;

    internal AnatomicalGeometryModel(AnatomicalSex sex,
        ImmutableArray<AnatomicalBodySegment> bodySegments,
        ImmutableArray<AnatomicalPlacementSurface> placementSurfaces)
    {
        Sex = sex;
        BodySegments = bodySegments;
        PlacementSurfaces = placementSurfaces;
        placementsByRegion = placementSurfaces.ToFrozenDictionary(surface => surface.Region);
    }

    public AnatomicalSex Sex { get; }
    public ImmutableArray<AnatomicalBodySegment> BodySegments { get; }
    public ImmutableArray<AnatomicalPlacementSurface> PlacementSurfaces { get; }

    public AnatomicalPlacementSurface GetPlacement(BodyRegionKind region) =>
        placementsByRegion.TryGetValue(region, out var placement)
            ? placement
            : throw new ArgumentOutOfRangeException(nameof(region));
}

/// <summary>
/// Authoritative generated mannequin and tattoo-placement geometry. Values match
/// the established Windows renderer, while the output is free of WPF/Android types.
/// </summary>
public static class AnatomicalGeometryCatalog
{
    public const int GeometryRevision = 1;

    private static readonly Lazy<AnatomicalGeometryModel> Male =
        new(() => Build(AnatomicalSex.Male), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<AnatomicalGeometryModel> Female =
        new(() => Build(AnatomicalSex.Female), LazyThreadSafetyMode.ExecutionAndPublication);

    public static AnatomicalGeometryModel ForSex(AnatomicalSex sex) => sex switch
    {
        AnatomicalSex.Male => Male.Value,
        AnatomicalSex.Female => Female.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(sex)),
    };

    /// <summary>
    /// Produces a safe render transform. Non-finite values are rejected and finite
    /// values are clamped to the same interaction limits as the workflow state.
    /// </summary>
    public static AnatomicalRenderPose ResolvePose(double heightCentimeters,
        double rotationDegrees, double cameraDistance)
    {
        if (!double.IsFinite(heightCentimeters)) throw new ArgumentOutOfRangeException(nameof(heightCentimeters));
        if (!double.IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        if (!double.IsFinite(cameraDistance)) throw new ArgumentOutOfRangeException(nameof(cameraDistance));
        return new(
            Math.Clamp(heightCentimeters, 140, 200) / AnatomicalDefaults.HeightCentimeters,
            NormalizeDegrees(rotationDegrees),
            Math.Clamp(cameraDistance, 3.8, 32));
    }

    /// <summary>Applies the WPF-compatible scale-then-Y-rotation model transform.</summary>
    public static AnatomicalPoint3 Transform(AnatomicalPoint3 point, AnatomicalRenderPose pose)
    {
        if (!point.IsFinite) throw new ArgumentOutOfRangeException(nameof(point));
        if (!double.IsFinite(pose.UniformScale) || pose.UniformScale <= 0 ||
            !double.IsFinite(pose.RotationDegrees) || !double.IsFinite(pose.CameraDistance))
            throw new ArgumentOutOfRangeException(nameof(pose));
        var radians = pose.RotationDegrees * Math.PI / 180;
        var x = point.X * pose.UniformScale;
        var y = point.Y * pose.UniformScale;
        var z = point.Z * pose.UniformScale;
        return new(
            x * Math.Cos(radians) + z * Math.Sin(radians),
            y,
            -x * Math.Sin(radians) + z * Math.Cos(radians));
    }

    private static AnatomicalGeometryModel Build(AnatomicalSex sex)
    {
        if (!Enum.IsDefined(sex)) throw new ArgumentOutOfRangeException(nameof(sex));
        var shoulderX = sex == AnatomicalSex.Male ? 1.03 : 0.91;
        var torsoRadiusX = sex == AnatomicalSex.Male ? 0.92 : 0.79;
        var torsoRadiusZ = sex == AnatomicalSex.Male ? 0.48 : 0.52;
        var hipRadiusX = sex == AnatomicalSex.Male ? 0.70 : 0.82;

        var body = ImmutableArray.CreateBuilder<AnatomicalBodySegment>(20);
        AddEllipsoid(AnatomicalBodySegmentKind.Head, new(0, 3.55, 0), new(0.43, 0.55, 0.40));
        AddCylinder(AnatomicalBodySegmentKind.Neck, new(0, 2.93, 0), 0.25, 0.23, 0.42);
        AddEllipsoid(AnatomicalBodySegmentKind.Torso, new(0, 1.55, 0),
            new(torsoRadiusX, 1.34, torsoRadiusZ),
            BodyRegionKind.UpperLeftChest, BodyRegionKind.UpperRightChest,
            BodyRegionKind.FullUpperChest, BodyRegionKind.FullChestAndAbdomen,
            BodyRegionKind.FullUpperBack, BodyRegionKind.FullBack);
        AddEllipsoid(AnatomicalBodySegmentKind.Hips, new(0, 0.05, 0), new(hipRadiusX, 0.62, 0.47));

        AddEllipsoid(AnatomicalBodySegmentKind.LeftShoulder, new(shoulderX, 2.30, 0), new(0.34, 0.36, 0.34),
            BodyRegionKind.LeftShoulder);
        AddCylinder(AnatomicalBodySegmentKind.LeftUpperArm, new(shoulderX, 1.52, 0), 0.28, 0.27, 1.35,
            BodyRegionKind.LeftOuterUpperArm, BodyRegionKind.LeftInnerUpperArm);
        AddCylinder(AnatomicalBodySegmentKind.LeftForearm, new(shoulderX + 0.02, 0.20, 0), 0.22, 0.21, 1.22,
            BodyRegionKind.LeftOuterForearm, BodyRegionKind.LeftInnerForearm);
        AddCylinder(AnatomicalBodySegmentKind.LeftWrist, new(shoulderX + 0.02, -0.52, 0), 0.18, 0.18, 0.20,
            BodyRegionKind.LeftWrist);
        AddEllipsoid(AnatomicalBodySegmentKind.LeftHand, new(shoulderX + 0.02, -0.78, 0.03),
            new(0.22, 0.35, 0.16), BodyRegionKind.LeftWrist);

        AddEllipsoid(AnatomicalBodySegmentKind.RightShoulder, new(-shoulderX, 2.30, 0), new(0.34, 0.36, 0.34),
            BodyRegionKind.RightShoulder);
        AddCylinder(AnatomicalBodySegmentKind.RightUpperArm, new(-shoulderX, 1.52, 0), 0.28, 0.27, 1.35,
            BodyRegionKind.RightOuterUpperArm, BodyRegionKind.RightInnerUpperArm);
        AddCylinder(AnatomicalBodySegmentKind.RightForearm, new(-shoulderX - 0.02, 0.20, 0), 0.22, 0.21, 1.22,
            BodyRegionKind.RightOuterForearm, BodyRegionKind.RightInnerForearm);
        AddCylinder(AnatomicalBodySegmentKind.RightWrist, new(-shoulderX - 0.02, -0.52, 0), 0.18, 0.18, 0.20,
            BodyRegionKind.RightWrist);
        AddEllipsoid(AnatomicalBodySegmentKind.RightHand, new(-shoulderX - 0.02, -0.78, 0.03),
            new(0.22, 0.35, 0.16), BodyRegionKind.RightWrist);

        AddCylinder(AnatomicalBodySegmentKind.LeftThigh, new(0.42, -1.03, 0), 0.38, 0.36, 1.70,
            BodyRegionKind.LeftThigh);
        AddCylinder(AnatomicalBodySegmentKind.LeftCalf, new(0.42, -2.57, 0), 0.27, 0.25, 1.40,
            BodyRegionKind.LeftCalf);
        AddEllipsoid(AnatomicalBodySegmentKind.LeftFoot, new(0.42, -3.42, 0.15), new(0.29, 0.20, 0.48));
        AddCylinder(AnatomicalBodySegmentKind.RightThigh, new(-0.42, -1.03, 0), 0.38, 0.36, 1.70,
            BodyRegionKind.RightThigh);
        AddCylinder(AnatomicalBodySegmentKind.RightCalf, new(-0.42, -2.57, 0), 0.27, 0.25, 1.40,
            BodyRegionKind.RightCalf);
        AddEllipsoid(AnatomicalBodySegmentKind.RightFoot, new(-0.42, -3.42, 0.15), new(0.29, 0.20, 0.48));

        var placements = BodyRegionCatalog.All.Select(region => CreatePlacement(
            sex, region, shoulderX, torsoRadiusX, torsoRadiusZ)).ToImmutableArray();
        return new(sex, body.MoveToImmutable(), placements);

        void AddEllipsoid(AnatomicalBodySegmentKind kind, AnatomicalPoint3 center, AnatomicalPoint3 radius,
            params BodyRegionKind[] regions) => body.Add(new(kind, CreateEllipsoid(center, radius),
            regions.ToImmutableArray()));

        void AddCylinder(AnatomicalBodySegmentKind kind, AnatomicalPoint3 center,
            double radiusX, double radiusZ, double height, params BodyRegionKind[] regions) =>
            body.Add(new(kind, CreateCylinderPatch(center, radiusX, radiusZ, height, -Math.PI, Math.PI),
                regions.ToImmutableArray()));
    }

    private static AnatomicalPlacementSurface CreatePlacement(AnatomicalSex sex,
        BodyRegionDefinition definition, double shoulderX, double torsoRadiusX, double torsoRadiusZ)
    {
        var region = definition.Kind;
        var mesh = region switch
        {
            BodyRegionKind.LeftOuterUpperArm => CreateCylinderPatch(new(shoulderX, 1.52, 0), 0.288, 0.278, 1.22, -1.15, 1.15),
            BodyRegionKind.LeftInnerUpperArm => CreateCylinderPatch(new(shoulderX, 1.52, 0), 0.288, 0.278, 1.08, Math.PI - 1, Math.PI + 1),
            BodyRegionKind.RightOuterUpperArm => CreateCylinderPatch(new(-shoulderX, 1.52, 0), 0.288, 0.278, 1.22, Math.PI - 1.15, Math.PI + 1.15),
            BodyRegionKind.RightInnerUpperArm => CreateCylinderPatch(new(-shoulderX, 1.52, 0), 0.288, 0.278, 1.08, -1, 1),
            BodyRegionKind.LeftOuterForearm => CreateCylinderPatch(new(shoulderX + 0.02, 0.20, 0), 0.228, 0.218, 1.04, -1.15, 1.15),
            BodyRegionKind.LeftInnerForearm => CreateCylinderPatch(new(shoulderX + 0.02, 0.20, 0), 0.228, 0.218, 0.92, Math.PI - 1, Math.PI + 1),
            BodyRegionKind.RightOuterForearm => CreateCylinderPatch(new(-shoulderX - 0.02, 0.20, 0), 0.228, 0.218, 1.04, Math.PI - 1.15, Math.PI + 1.15),
            BodyRegionKind.RightInnerForearm => CreateCylinderPatch(new(-shoulderX - 0.02, 0.20, 0), 0.228, 0.218, 0.92, -1, 1),
            BodyRegionKind.LeftWrist => CreateCylinderPatch(new(shoulderX + 0.02, -0.52, 0), 0.188, 0.188, 0.18, -Math.PI, Math.PI),
            BodyRegionKind.RightWrist => CreateCylinderPatch(new(-shoulderX - 0.02, -0.52, 0), 0.188, 0.188, 0.18, -Math.PI, Math.PI),
            BodyRegionKind.UpperLeftChest => CreateTorsoPatch(0.05, torsoRadiusX * 0.76, 1.45, 2.30, true, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.UpperRightChest => CreateTorsoPatch(-torsoRadiusX * 0.76, -0.05, 1.45, 2.30, true, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.FullUpperChest => CreateTorsoPatch(-torsoRadiusX * 0.70, torsoRadiusX * 0.70, 1.30, 2.32, true, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.FullChestAndAbdomen => CreateTorsoPatch(-torsoRadiusX * 0.60, torsoRadiusX * 0.60, 0.35, 2.22, true, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.FullUpperBack => CreateTorsoPatch(-torsoRadiusX * 0.70, torsoRadiusX * 0.70, 1.25, 2.35, false, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.FullBack => CreateTorsoPatch(-torsoRadiusX * 0.62, torsoRadiusX * 0.62, 0.28, 2.35, false, torsoRadiusX, torsoRadiusZ),
            BodyRegionKind.LeftShoulder => CreateEllipsoid(new(shoulderX, 2.30, 0), new(0.348, 0.368, 0.348)),
            BodyRegionKind.RightShoulder => CreateEllipsoid(new(-shoulderX, 2.30, 0), new(0.348, 0.368, 0.348)),
            BodyRegionKind.LeftThigh => CreateCylinderPatch(new(0.42, -1.03, 0), 0.388, 0.368, 1.35, -0.25, Math.PI + 0.25),
            BodyRegionKind.RightThigh => CreateCylinderPatch(new(-0.42, -1.03, 0), 0.388, 0.368, 1.35, -0.25, Math.PI + 0.25),
            BodyRegionKind.LeftCalf => CreateCylinderPatch(new(0.42, -2.57, 0), 0.278, 0.258, 1.12, Math.PI * 0.35, Math.PI * 1.65),
            BodyRegionKind.RightCalf => CreateCylinderPatch(new(-0.42, -2.57, 0), 0.278, 0.258, 1.12, Math.PI * 0.35, Math.PI * 1.65),
            _ => throw new ArgumentOutOfRangeException(nameof(definition)),
        };
        return new(sex, region, Laterality(region), SurfaceSide(region), mesh,
            AnatomicalCameraFraming.ForRegion(region), AnatomicalDefaults.PreferredRotation(region),
            Math.Clamp(definition.DefaultTattooScalePercent / 100, 0, 1));
    }

    private static AnatomicalTriangleMesh CreateTorsoPatch(double minX, double maxX, double minY, double maxY,
        bool front, double torsoRadiusX, double torsoRadiusZ) =>
        CreateEllipsoidPatch(new(0, 1.55, 0), new(torsoRadiusX * 1.012, 1.34 * 1.012, torsoRadiusZ * 1.025),
            minX, maxX, minY, maxY, front);

    private static AnatomicalTriangleMesh CreateEllipsoid(AnatomicalPoint3 center, AnatomicalPoint3 radius,
        int slices = 28, int stacks = 18)
    {
        var vertices = ImmutableArray.CreateBuilder<AnatomicalVertex>((slices + 1) * (stacks + 1));
        for (var stack = 0; stack <= stacks; stack++)
        {
            var latitude = Math.PI * stack / stacks;
            var ring = Math.Sin(latitude);
            var normalizedY = Math.Cos(latitude);
            for (var slice = 0; slice <= slices; slice++)
            {
                var u = slice / (double)slices;
                var v = stack / (double)stacks;
                var longitude = 2 * Math.PI * u;
                var normalizedX = ring * Math.Cos(longitude);
                var normalizedZ = ring * Math.Sin(longitude);
                vertices.Add(new(
                    new(center.X + radius.X * normalizedX, center.Y + radius.Y * normalizedY,
                        center.Z + radius.Z * normalizedZ),
                    Normalize(normalizedX / radius.X, normalizedY / radius.Y, normalizedZ / radius.Z),
                    new(u, v)));
            }
        }
        return CreateGrid(vertices.MoveToImmutable(), slices, stacks);
    }

    private static AnatomicalTriangleMesh CreateCylinderPatch(AnatomicalPoint3 center,
        double radiusX, double radiusZ, double height, double startAngle, double endAngle, int segments = 28)
    {
        var vertices = ImmutableArray.CreateBuilder<AnatomicalVertex>((segments + 1) * 2);
        for (var row = 0; row <= 1; row++)
        {
            var y = center.Y + (row == 0 ? height / 2 : -height / 2);
            for (var segment = 0; segment <= segments; segment++)
            {
                var u = segment / (double)segments;
                var angle = startAngle + (endAngle - startAngle) * u;
                vertices.Add(new(
                    new(center.X + radiusX * Math.Cos(angle), y, center.Z + radiusZ * Math.Sin(angle)),
                    Normalize(Math.Cos(angle) / radiusX, 0, Math.Sin(angle) / radiusZ),
                    new(u, row)));
            }
        }
        return CreateGrid(vertices.MoveToImmutable(), segments, 1);
    }

    private static AnatomicalTriangleMesh CreateEllipsoidPatch(AnatomicalPoint3 center, AnatomicalPoint3 radius,
        double minX, double maxX, double minY, double maxY, bool front, int columns = 20, int rows = 20)
    {
        var vertices = ImmutableArray.CreateBuilder<AnatomicalVertex>((columns + 1) * (rows + 1));
        var direction = front ? 1d : -1d;
        for (var row = 0; row <= rows; row++)
        {
            var v = row / (double)rows;
            var y = maxY + (minY - maxY) * v;
            for (var column = 0; column <= columns; column++)
            {
                var u = column / (double)columns;
                var x = minX + (maxX - minX) * u;
                var normalizedX = (x - center.X) / radius.X;
                var normalizedY = (y - center.Y) / radius.Y;
                var zFactor = Math.Sqrt(Math.Max(0.025, 1 - normalizedX * normalizedX - normalizedY * normalizedY));
                vertices.Add(new(new(x, y, center.Z + direction * radius.Z * zFactor),
                    Normalize(normalizedX / radius.X, normalizedY / radius.Y, direction * zFactor / radius.Z),
                    new(u, v)));
            }
        }
        return CreateGrid(vertices.MoveToImmutable(), columns, rows);
    }

    private static AnatomicalTriangleMesh CreateGrid(ImmutableArray<AnatomicalVertex> vertices,
        int columns, int rows)
    {
        var indices = ImmutableArray.CreateBuilder<int>(checked(columns * rows * 6));
        var stride = columns + 1;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var topLeft = row * stride + column;
                var topRight = topLeft + 1;
                var bottomLeft = topLeft + stride;
                var bottomRight = bottomLeft + 1;
                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);
                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        return new(vertices, indices.MoveToImmutable());
    }

    private static AnatomicalPoint3 Normalize(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        if (!double.IsFinite(length) || length <= 0) throw new InvalidDataException("A surface normal is invalid.");
        return new(x / length, y / length, z / length);
    }

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        return value > 180 ? value - 360 : value < -180 ? value + 360 : value;
    }

    private static AnatomicalLaterality Laterality(BodyRegionKind region) => region switch
    {
        BodyRegionKind.LeftOuterUpperArm or BodyRegionKind.LeftInnerUpperArm or
            BodyRegionKind.LeftOuterForearm or BodyRegionKind.LeftInnerForearm or
            BodyRegionKind.LeftWrist or BodyRegionKind.UpperLeftChest or BodyRegionKind.LeftShoulder or
            BodyRegionKind.LeftThigh or BodyRegionKind.LeftCalf => AnatomicalLaterality.Left,
        BodyRegionKind.RightOuterUpperArm or BodyRegionKind.RightInnerUpperArm or
            BodyRegionKind.RightOuterForearm or BodyRegionKind.RightInnerForearm or
            BodyRegionKind.RightWrist or BodyRegionKind.UpperRightChest or BodyRegionKind.RightShoulder or
            BodyRegionKind.RightThigh or BodyRegionKind.RightCalf => AnatomicalLaterality.Right,
        _ => AnatomicalLaterality.Center,
    };

    private static AnatomicalSurfaceSide SurfaceSide(BodyRegionKind region) => region switch
    {
        BodyRegionKind.LeftOuterUpperArm or BodyRegionKind.RightOuterUpperArm or
            BodyRegionKind.LeftOuterForearm or BodyRegionKind.RightOuterForearm or
            BodyRegionKind.LeftShoulder or BodyRegionKind.RightShoulder => AnatomicalSurfaceSide.Outer,
        BodyRegionKind.LeftInnerUpperArm or BodyRegionKind.RightInnerUpperArm or
            BodyRegionKind.LeftInnerForearm or BodyRegionKind.RightInnerForearm => AnatomicalSurfaceSide.Inner,
        BodyRegionKind.LeftWrist or BodyRegionKind.RightWrist => AnatomicalSurfaceSide.Circumferential,
        BodyRegionKind.UpperLeftChest or BodyRegionKind.UpperRightChest or
            BodyRegionKind.FullUpperChest or BodyRegionKind.FullChestAndAbdomen => AnatomicalSurfaceSide.Front,
        BodyRegionKind.FullUpperBack or BodyRegionKind.FullBack => AnatomicalSurfaceSide.Back,
        BodyRegionKind.LeftThigh or BodyRegionKind.RightThigh => AnatomicalSurfaceSide.FrontOuter,
        BodyRegionKind.LeftCalf or BodyRegionKind.RightCalf => AnatomicalSurfaceSide.BackOuter,
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };
}
