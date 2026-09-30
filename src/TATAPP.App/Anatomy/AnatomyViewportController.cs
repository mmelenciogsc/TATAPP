using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using TATAPP.App.Imaging;
using TATAPP.Core;

namespace TATAPP.App.Anatomy;

/// <summary>
/// Owns the interactive WPF 3D mannequin. Geometry is generated locally so the
/// anatomy preview has no online dependency or opaque third-party model asset.
/// </summary>
internal sealed class AnatomyViewportController
{
    private readonly Viewport3D viewport;
    private readonly Action<BodyRegionKind> regionTapped;
    private readonly Model3DGroup scene = new();
    private readonly Model3DGroup body = new();
    private readonly Dictionary<GeometryModel3D, BodyRegionKind> hitRegions = [];
    private readonly List<GeometryModel3D> skinModels = [];
    private readonly PerspectiveCamera camera;
    private readonly AxisAngleRotation3D rotation = new(new Vector3D(0, 1, 0), 0);
    private readonly ScaleTransform3D bodyScale = new(1, 1, 1);
    private GeometryModel3D? torsoModel;
    private GeometryModel3D? placementModel;
    private BitmapSource? tattooTexture;
    private Point dragStart;
    private double dragStartAngle;
    private bool dragged;
    private AnatomicalSex sex = AnatomicalDefaults.Sex;
    private BodyRegionKind selectedRegion = AnatomicalDefaults.Region;
    private double heightCentimeters = AnatomicalDefaults.HeightCentimeters;
    private double skinToneValue = AnatomicalDefaults.SkinToneValue;
    private bool showTattoo;
    private double cameraDistance = AnatomicalCameraFraming.OverviewDistance;
    private int cameraAnimationGeneration;

    public event Action? PlacementFocusCompleted;

    public AnatomyViewportController(Viewport3D viewport, Action<BodyRegionKind> regionTapped)
    {
        this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        this.regionTapped = regionTapped ?? throw new ArgumentNullException(nameof(regionTapped));
        // WPF's perspective FieldOfView is horizontal. The anatomy pane is
        // intentionally wide, so this distance keeps the full figure visible
        // even at the maximum body-size setting.
        camera = new PerspectiveCamera(new Point3D(0, 0.2, cameraDistance),
            new Vector3D(0, 0, -cameraDistance), new Vector3D(0, 1, 0), 45);
        viewport.Camera = camera;
        scene.Children.Add(new AmbientLight(Color.FromRgb(82, 82, 92)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.8, -1, -1.4)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(150, 168, 210), new Vector3D(1, 0.2, 0.6)));
        scene.Children.Add(body);
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
        viewport.MouseMove += Viewport_MouseMove;
        viewport.MouseLeftButtonUp += Viewport_MouseLeftButtonUp;
        viewport.MouseWheel += Viewport_MouseWheel;
        BuildBody();
    }

    public AnatomicalSex Sex => sex;
    public BodyRegionKind SelectedRegion => selectedRegion;
    public double RotationDegrees => rotation.Angle;
    public double ZoomPercent => AnatomicalCameraFraming.OverviewDistance / cameraDistance * 100d;
    public string PlacementFramingDescription =>
        $"The view first establishes {AnatomicalCameraFraming.ForRegion(selectedRegion).ContextDescription}, " +
        "then smoothly moves closer to center the tattoo.";

    public void SetSex(AnatomicalSex value)
    {
        if (sex == value) return;
        sex = value;
        BuildBody();
    }

    public void SetHeight(double centimeters)
    {
        heightCentimeters = Math.Clamp(centimeters, 140, 200);
        var scale = heightCentimeters / AnatomicalDefaults.HeightCentimeters;
        bodyScale.ScaleX = scale;
        bodyScale.ScaleY = scale;
        bodyScale.ScaleZ = scale;
    }

    public void SetSkinTone(double value)
    {
        skinToneValue = Math.Clamp(value, 0, 100);
        var material = CreateSkinMaterial();
        foreach (var model in skinModels)
        {
            model.Material = material;
            model.BackMaterial = material;
        }
    }

    public void SelectRegion(BodyRegionKind value, bool orientToRegion)
    {
        selectedRegion = value;
        if (orientToRegion)
        {
            rotation.Angle = BodyRegionCatalog.Get(value).PreferredView switch
            {
                AnatomicalView.Back => 180,
                AnatomicalView.Left => -62,
                AnatomicalView.Right => 62,
                _ => 0,
            };
        }
        RebuildPlacementSurface();
    }

    public void RotateBy(double degrees)
    {
        rotation.Angle = NormalizeDegrees(rotation.Angle + degrees);
    }

    public void ZoomBy(double distanceDelta)
    {
        var target = StopCameraAnimationAtCurrentFrame();
        cameraDistance = Math.Clamp(cameraDistance + distanceDelta, 3.8, 32);
        SetCamera(target, cameraDistance);
    }

    /// <summary>
    /// Starts a placement stage with useful surrounding anatomy, pauses long
    /// enough to establish location, then smoothly moves toward the tattoo.
    /// </summary>
    public void AnimatePlacementFocus()
    {
        var plan = AnatomicalCameraFraming.ForRegion(selectedRegion);
        var scale = heightCentimeters / AnatomicalDefaults.HeightCentimeters;
        var contextTarget = WorldPoint(plan.ContextFocusX, plan.ContextFocusY);
        var detailTarget = WorldPoint(plan.DetailFocusX, plan.DetailFocusY);
        var contextDistance = plan.ContextDistance * scale;
        var detailDistance = plan.DetailDistance * scale;
        var contextPosition = CameraPosition(contextTarget, contextDistance);
        var detailPosition = CameraPosition(detailTarget, detailDistance);
        var contextDirection = contextTarget - contextPosition;
        var detailDirection = detailTarget - detailPosition;

        StopCameraAnimation();
        SetCamera(contextTarget, contextDistance);
        var generation = ++cameraAnimationGeneration;
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(2350));
        var beginTime = TimeSpan.FromMilliseconds(450);
        var positionAnimation = new Point3DAnimation(contextPosition, detailPosition, duration)
        {
            BeginTime = beginTime,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        var directionAnimation = new Vector3DAnimation(contextDirection, detailDirection, duration)
        {
            BeginTime = beginTime,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        positionAnimation.Completed += (_, _) =>
        {
            if (generation != cameraAnimationGeneration) return;
            camera.Position = detailPosition;
            camera.LookDirection = detailDirection;
            camera.BeginAnimation(ProjectionCamera.PositionProperty, null);
            camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, null);
            cameraDistance = detailDistance;
            PlacementFocusCompleted?.Invoke();
        };
        camera.BeginAnimation(ProjectionCamera.PositionProperty, positionAnimation);
        camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, directionAnimation);
    }

    public void FocusSelectedRegionInstant()
    {
        var plan = AnatomicalCameraFraming.ForRegion(selectedRegion);
        var scale = heightCentimeters / AnatomicalDefaults.HeightCentimeters;
        StopCameraAnimation();
        SetCamera(WorldPoint(plan.DetailFocusX, plan.DetailFocusY), plan.DetailDistance * scale);
    }

    public void ShowOverview()
    {
        StopCameraAnimation();
        var scale = heightCentimeters / AnatomicalDefaults.HeightCentimeters;
        SetCamera(new Point3D(0, 0.2 * scale, 0), AnatomicalCameraFraming.OverviewDistance * scale);
    }

    private Point3D WorldPoint(double x, double y) =>
        body.Transform.Transform(new Point3D(x, y, 0));

    private static Point3D CameraPosition(Point3D target, double distance) =>
        new(target.X, target.Y, target.Z + distance);

    private void SetCamera(Point3D target, double distance)
    {
        cameraDistance = distance;
        camera.Position = CameraPosition(target, distance);
        camera.LookDirection = target - camera.Position;
    }

    private Point3D StopCameraAnimationAtCurrentFrame()
    {
        var position = camera.Position;
        var target = position + camera.LookDirection;
        cameraDistance = Math.Max(0.1, (position - target).Length);
        StopCameraAnimation();
        camera.Position = position;
        camera.LookDirection = target - position;
        return target;
    }

    private void StopCameraAnimation()
    {
        cameraAnimationGeneration++;
        camera.BeginAnimation(ProjectionCamera.PositionProperty, null);
        camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, null);
    }

    public void SetTattoo(ImageFrame? source, bool visible)
    {
        tattooTexture = source is null
            ? null
            : PhotoCodec.ToBitmapSource(TattooInkTexture.Create(source));
        showTattoo = visible && tattooTexture is not null;
        RebuildPlacementSurface();
    }

    public void SetTattooVisible(bool visible)
    {
        showTattoo = visible && tattooTexture is not null;
        RebuildPlacementSurface();
    }

    private void BuildBody()
    {
        body.Children.Clear();
        hitRegions.Clear();
        skinModels.Clear();
        placementModel = null;

        var shoulderX = sex == AnatomicalSex.Male ? 1.03 : 0.91;
        var torsoRadiusX = sex == AnatomicalSex.Male ? 0.92 : 0.79;
        var torsoRadiusY = 1.34;
        var torsoRadiusZ = sex == AnatomicalSex.Male ? 0.48 : 0.52;
        var hipRadiusX = sex == AnatomicalSex.Male ? 0.70 : 0.82;

        AddSkin(CreateEllipsoid(new Point3D(0, 3.55, 0), new Vector3D(0.43, 0.55, 0.40)), null);
        AddSkin(CreateCylinder(new Point3D(0, 2.93, 0), 0.25, 0.23, 0.42), null);
        torsoModel = AddSkin(CreateEllipsoid(new Point3D(0, 1.55, 0),
            new Vector3D(torsoRadiusX, torsoRadiusY, torsoRadiusZ)), BodyRegionKind.FullChestAndAbdomen);
        AddSkin(CreateEllipsoid(new Point3D(0, 0.05, 0), new Vector3D(hipRadiusX, 0.62, 0.47)), null);

        AddSkin(CreateEllipsoid(new Point3D(shoulderX, 2.30, 0), new Vector3D(0.34, 0.36, 0.34)),
            BodyRegionKind.LeftShoulder);
        AddSkin(CreateCylinder(new Point3D(shoulderX, 1.52, 0), 0.28, 0.27, 1.35),
            BodyRegionKind.LeftOuterUpperArm);
        AddSkin(CreateCylinder(new Point3D(shoulderX + 0.02, 0.20, 0), 0.22, 0.21, 1.22),
            BodyRegionKind.LeftOuterForearm);
        AddSkin(CreateCylinder(new Point3D(shoulderX + 0.02, -0.52, 0), 0.18, 0.18, 0.20),
            BodyRegionKind.LeftWrist);
        AddSkin(CreateEllipsoid(new Point3D(shoulderX + 0.02, -0.78, 0.03), new Vector3D(0.22, 0.35, 0.16)),
            BodyRegionKind.LeftWrist);

        AddSkin(CreateEllipsoid(new Point3D(-shoulderX, 2.30, 0), new Vector3D(0.34, 0.36, 0.34)),
            BodyRegionKind.RightShoulder);
        AddSkin(CreateCylinder(new Point3D(-shoulderX, 1.52, 0), 0.28, 0.27, 1.35),
            BodyRegionKind.RightOuterUpperArm);
        AddSkin(CreateCylinder(new Point3D(-shoulderX - 0.02, 0.20, 0), 0.22, 0.21, 1.22),
            BodyRegionKind.RightOuterForearm);
        AddSkin(CreateCylinder(new Point3D(-shoulderX - 0.02, -0.52, 0), 0.18, 0.18, 0.20),
            BodyRegionKind.RightWrist);
        AddSkin(CreateEllipsoid(new Point3D(-shoulderX - 0.02, -0.78, 0.03), new Vector3D(0.22, 0.35, 0.16)),
            BodyRegionKind.RightWrist);

        AddSkin(CreateCylinder(new Point3D(0.42, -1.03, 0), 0.38, 0.36, 1.70), BodyRegionKind.LeftThigh);
        AddSkin(CreateCylinder(new Point3D(0.42, -2.57, 0), 0.27, 0.25, 1.40), BodyRegionKind.LeftCalf);
        AddSkin(CreateEllipsoid(new Point3D(0.42, -3.42, 0.15), new Vector3D(0.29, 0.20, 0.48)), null);
        AddSkin(CreateCylinder(new Point3D(-0.42, -1.03, 0), 0.38, 0.36, 1.70), BodyRegionKind.RightThigh);
        AddSkin(CreateCylinder(new Point3D(-0.42, -2.57, 0), 0.27, 0.25, 1.40), BodyRegionKind.RightCalf);
        AddSkin(CreateEllipsoid(new Point3D(-0.42, -3.42, 0.15), new Vector3D(0.29, 0.20, 0.48)), null);

        var transform = new Transform3DGroup();
        transform.Children.Add(bodyScale);
        transform.Children.Add(new RotateTransform3D(rotation));
        body.Transform = transform;
        SetHeight(heightCentimeters);
        RebuildPlacementSurface();
    }

    private GeometryModel3D AddSkin(MeshGeometry3D mesh, BodyRegionKind? hitRegion)
    {
        var material = CreateSkinMaterial();
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        body.Children.Add(model);
        skinModels.Add(model);
        if (hitRegion is { } region) hitRegions.Add(model, region);
        return model;
    }

    private MaterialGroup CreateSkinMaterial()
    {
        var tone = AnatomicalDefaults.SkinToneFromSlider(skinToneValue);
        var diffuse = new SolidColorBrush(Color.FromRgb(tone.Red, tone.Green, tone.Blue));
        diffuse.Freeze();
        var shine = new SolidColorBrush(Color.FromArgb(48, 255, 244, 233));
        shine.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(diffuse));
        material.Children.Add(new SpecularMaterial(shine, 22));
        return material;
    }

    private void RebuildPlacementSurface()
    {
        if (placementModel is not null)
        {
            body.Children.Remove(placementModel);
            hitRegions.Remove(placementModel);
        }
        var mesh = CreatePlacementMesh(selectedRegion);
        var material = CreatePlacementMaterial();
        placementModel = new GeometryModel3D(mesh, material) { BackMaterial = material };
        body.Children.Add(placementModel);
        hitRegions[placementModel] = selectedRegion;
    }

    private DiffuseMaterial CreatePlacementMaterial()
    {
        Brush brush;
        if (showTattoo && tattooTexture is not null)
        {
            brush = new ImageBrush(tattooTexture)
            {
                Stretch = Stretch.Uniform,
                TileMode = TileMode.None,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Opacity = 0.94,
            };
        }
        else
        {
            brush = new SolidColorBrush(Color.FromArgb(118, 139, 71, 180));
        }
        brush.Freeze();
        return new DiffuseMaterial(brush);
    }

    private MeshGeometry3D CreatePlacementMesh(BodyRegionKind region)
    {
        var leftX = sex == AnatomicalSex.Male ? 1.03 : 0.91;
        var torsoX = sex == AnatomicalSex.Male ? 0.92 : 0.79;
        var torsoZ = sex == AnatomicalSex.Male ? 0.48 : 0.52;
        return region switch
        {
            BodyRegionKind.LeftOuterUpperArm => CreateCylinderPatch(new Point3D(leftX, 1.52, 0), 0.288, 0.278, 1.22, -1.15, 1.15),
            BodyRegionKind.LeftInnerUpperArm => CreateCylinderPatch(new Point3D(leftX, 1.52, 0), 0.288, 0.278, 1.08, Math.PI - 1.0, Math.PI + 1.0),
            BodyRegionKind.RightOuterUpperArm => CreateCylinderPatch(new Point3D(-leftX, 1.52, 0), 0.288, 0.278, 1.22, Math.PI - 1.15, Math.PI + 1.15),
            BodyRegionKind.RightInnerUpperArm => CreateCylinderPatch(new Point3D(-leftX, 1.52, 0), 0.288, 0.278, 1.08, -1.0, 1.0),
            BodyRegionKind.LeftOuterForearm => CreateCylinderPatch(new Point3D(leftX + 0.02, 0.20, 0), 0.228, 0.218, 1.04, -1.15, 1.15),
            BodyRegionKind.LeftInnerForearm => CreateCylinderPatch(new Point3D(leftX + 0.02, 0.20, 0), 0.228, 0.218, 0.92, Math.PI - 1.0, Math.PI + 1.0),
            BodyRegionKind.RightOuterForearm => CreateCylinderPatch(new Point3D(-leftX - 0.02, 0.20, 0), 0.228, 0.218, 1.04, Math.PI - 1.15, Math.PI + 1.15),
            BodyRegionKind.RightInnerForearm => CreateCylinderPatch(new Point3D(-leftX - 0.02, 0.20, 0), 0.228, 0.218, 0.92, -1.0, 1.0),
            BodyRegionKind.LeftWrist => CreateCylinderPatch(new Point3D(leftX + 0.02, -0.52, 0), 0.188, 0.188, 0.18, -Math.PI, Math.PI),
            BodyRegionKind.RightWrist => CreateCylinderPatch(new Point3D(-leftX - 0.02, -0.52, 0), 0.188, 0.188, 0.18, -Math.PI, Math.PI),
            BodyRegionKind.UpperLeftChest => CreateTorsoPatch(0.05, torsoX * 0.76, 1.45, 2.30, true, torsoX, torsoZ),
            BodyRegionKind.UpperRightChest => CreateTorsoPatch(-torsoX * 0.76, -0.05, 1.45, 2.30, true, torsoX, torsoZ),
            BodyRegionKind.FullUpperChest => CreateTorsoPatch(-torsoX * 0.70, torsoX * 0.70, 1.30, 2.32, true, torsoX, torsoZ),
            BodyRegionKind.FullChestAndAbdomen => CreateTorsoPatch(-torsoX * 0.60, torsoX * 0.60, 0.35, 2.22, true, torsoX, torsoZ),
            BodyRegionKind.FullUpperBack => CreateTorsoPatch(-torsoX * 0.70, torsoX * 0.70, 1.25, 2.35, false, torsoX, torsoZ),
            BodyRegionKind.FullBack => CreateTorsoPatch(-torsoX * 0.62, torsoX * 0.62, 0.28, 2.35, false, torsoX, torsoZ),
            BodyRegionKind.LeftShoulder => CreateEllipsoid(new Point3D(leftX, 2.30, 0), new Vector3D(0.348, 0.368, 0.348)),
            BodyRegionKind.RightShoulder => CreateEllipsoid(new Point3D(-leftX, 2.30, 0), new Vector3D(0.348, 0.368, 0.348)),
            BodyRegionKind.LeftThigh => CreateCylinderPatch(new Point3D(0.42, -1.03, 0), 0.388, 0.368, 1.35, -0.25, Math.PI + 0.25),
            BodyRegionKind.RightThigh => CreateCylinderPatch(new Point3D(-0.42, -1.03, 0), 0.388, 0.368, 1.35, -0.25, Math.PI + 0.25),
            BodyRegionKind.LeftCalf => CreateCylinderPatch(new Point3D(0.42, -2.57, 0), 0.278, 0.258, 1.12, Math.PI * 0.35, Math.PI * 1.65),
            BodyRegionKind.RightCalf => CreateCylinderPatch(new Point3D(-0.42, -2.57, 0), 0.278, 0.258, 1.12, Math.PI * 0.35, Math.PI * 1.65),
            _ => throw new ArgumentOutOfRangeException(nameof(region)),
        };
    }

    private static MeshGeometry3D CreateTorsoPatch(double minX, double maxX, double minY, double maxY,
        bool front, double torsoRadiusX, double torsoRadiusZ) =>
        CreateEllipsoidSurfacePatch(new Point3D(0, 1.55, 0),
            new Vector3D(torsoRadiusX * 1.012, 1.34 * 1.012, torsoRadiusZ * 1.025),
            minX, maxX, minY, maxY, front);

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(viewport);
        dragStartAngle = rotation.Angle;
        dragged = false;
        viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !viewport.IsMouseCaptured) return;
        var current = e.GetPosition(viewport);
        var difference = current.X - dragStart.X;
        if (Math.Abs(difference) < 3) return;
        dragged = true;
        rotation.Angle = NormalizeDegrees(dragStartAngle + difference * 0.45);
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(viewport);
        viewport.ReleaseMouseCapture();
        if (dragged) return;
        if (VisualTreeHelper.HitTest(viewport, point) is not RayMeshGeometry3DHitTestResult result ||
            result.ModelHit is not GeometryModel3D model)
            return;
        if (!TryResolveTappedRegion(model, result.PointHit, out var region))
            return;
        SelectRegion(region, orientToRegion: false);
        regionTapped(region);
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? -1.5 : 1.5);
        e.Handled = true;
    }

    private bool TryResolveTappedRegion(GeometryModel3D model, Point3D worldPoint, out BodyRegionKind region)
    {
        if (!hitRegions.TryGetValue(model, out region) && !ReferenceEquals(model, torsoModel)) return false;
        if (ReferenceEquals(model, placementModel)) return true;

        var localPoint = body.Transform.Inverse?.Transform(worldPoint) ?? worldPoint;
        if (ReferenceEquals(model, torsoModel))
        {
            var viewingBack = Math.Cos(rotation.Angle * Math.PI / 180d) < 0;
            if (viewingBack)
                region = localPoint.Y >= 1.3 ? BodyRegionKind.FullUpperBack : BodyRegionKind.FullBack;
            else if (localPoint.Y >= 1.35)
                region = localPoint.X >= 0 ? BodyRegionKind.UpperLeftChest : BodyRegionKind.UpperRightChest;
            else
                region = BodyRegionKind.FullChestAndAbdomen;
            return true;
        }

        var armCenter = sex == AnatomicalSex.Male ? 1.03 : 0.91;
        region = region switch
        {
            BodyRegionKind.LeftOuterUpperArm when localPoint.X < armCenter => BodyRegionKind.LeftInnerUpperArm,
            BodyRegionKind.RightOuterUpperArm when localPoint.X > -armCenter => BodyRegionKind.RightInnerUpperArm,
            BodyRegionKind.LeftOuterForearm when localPoint.X < armCenter + 0.02 => BodyRegionKind.LeftInnerForearm,
            BodyRegionKind.RightOuterForearm when localPoint.X > -armCenter - 0.02 => BodyRegionKind.RightInnerForearm,
            _ => region,
        };
        return true;
    }

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        return value > 180 ? value - 360 : value < -180 ? value + 360 : value;
    }

    private static MeshGeometry3D CreateEllipsoid(Point3D center, Vector3D radius, int slices = 28, int stacks = 18)
    {
        var mesh = new MeshGeometry3D();
        for (var stack = 0; stack <= stacks; stack++)
        {
            var latitude = Math.PI * stack / stacks;
            var ring = Math.Sin(latitude);
            var normalizedY = Math.Cos(latitude);
            for (var slice = 0; slice <= slices; slice++)
            {
                var longitude = 2 * Math.PI * slice / slices;
                var normalizedX = ring * Math.Cos(longitude);
                var normalizedZ = ring * Math.Sin(longitude);
                mesh.Positions.Add(new Point3D(center.X + radius.X * normalizedX,
                    center.Y + radius.Y * normalizedY, center.Z + radius.Z * normalizedZ));
                var normal = new Vector3D(normalizedX / radius.X, normalizedY / radius.Y,
                    normalizedZ / radius.Z);
                normal.Normalize();
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point(slice / (double)slices, stack / (double)stacks));
            }
        }
        AddGridTriangles(mesh, slices, stacks);
        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateCylinder(Point3D center, double radiusX, double radiusZ, double height,
        int segments = 28) => CreateCylinderPatch(center, radiusX, radiusZ, height, -Math.PI, Math.PI, segments);

    private static MeshGeometry3D CreateCylinderPatch(Point3D center, double radiusX, double radiusZ,
        double height, double startAngle, double endAngle, int segments = 28)
    {
        var mesh = new MeshGeometry3D();
        for (var row = 0; row <= 1; row++)
        {
            var y = center.Y + (row == 0 ? height / 2 : -height / 2);
            for (var segment = 0; segment <= segments; segment++)
            {
                var amount = segment / (double)segments;
                var angle = startAngle + (endAngle - startAngle) * amount;
                var normal = new Vector3D(Math.Cos(angle) / radiusX, 0, Math.Sin(angle) / radiusZ);
                normal.Normalize();
                mesh.Positions.Add(new Point3D(center.X + radiusX * Math.Cos(angle), y,
                    center.Z + radiusZ * Math.Sin(angle)));
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point(amount, row));
            }
        }
        AddGridTriangles(mesh, segments, 1);
        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateEllipsoidSurfacePatch(Point3D center, Vector3D radius,
        double minX, double maxX, double minY, double maxY, bool front, int columns = 20, int rows = 20)
    {
        var mesh = new MeshGeometry3D();
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
                var z = center.Z + direction * radius.Z * zFactor;
                var normal = new Vector3D(normalizedX / radius.X, normalizedY / radius.Y,
                    direction * zFactor / radius.Z);
                normal.Normalize();
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }
        AddGridTriangles(mesh, columns, rows);
        mesh.Freeze();
        return mesh;
    }

    private static void AddGridTriangles(MeshGeometry3D mesh, int columns, int rows)
    {
        var stride = columns + 1;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var topLeft = row * stride + column;
            var topRight = topLeft + 1;
            var bottomLeft = topLeft + stride;
            var bottomRight = bottomLeft + 1;
            mesh.TriangleIndices.Add(topLeft);
            mesh.TriangleIndices.Add(bottomLeft);
            mesh.TriangleIndices.Add(topRight);
            mesh.TriangleIndices.Add(topRight);
            mesh.TriangleIndices.Add(bottomLeft);
            mesh.TriangleIndices.Add(bottomRight);
        }
    }
}
