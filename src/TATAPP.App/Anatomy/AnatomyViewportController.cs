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
            : PhotoCodec.ToBitmapSource(TattooInkTexture.CreatePlacementTexture(source));
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

        foreach (var segment in AnatomicalGeometryCatalog.ForSex(sex).BodySegments)
        {
            BodyRegionKind? hitRegion = segment.Kind == AnatomicalBodySegmentKind.Torso
                ? BodyRegionKind.FullChestAndAbdomen
                : segment.SelectableRegions.IsDefaultOrEmpty
                    ? null
                    : segment.SelectableRegions[0];
            var model = AddSkin(WpfAnatomicalMeshAdapter.Create(segment.Mesh), hitRegion);
            if (segment.Kind == AnatomicalBodySegmentKind.Torso) torsoModel = model;
        }

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
                // Core has already performed the scale-1 uniform-contain fit in
                // a transparent square, so both renderers consume one UV policy.
                Stretch = Stretch.Fill,
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
        => WpfAnatomicalMeshAdapter.Create(AnatomicalGeometryCatalog.ForSex(sex).GetPlacement(region).Mesh);

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

}
