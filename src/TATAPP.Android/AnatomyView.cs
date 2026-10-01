using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Accessibility;
using TATAPP.Core;
using TATAPP.Core.Workflow;

namespace TATAPP.AndroidApp;

/// <summary>
/// Locally renders the repository-defined mannequin and placement surfaces. The
/// companion native region picker is the authoritative non-visual interaction path.
/// </summary>
internal sealed class AnatomyView : View
{
    private readonly Paint skinPaint = new(PaintFlags.AntiAlias);
    private readonly Paint selectionPaint = new(PaintFlags.AntiAlias);
    private readonly Paint tattooPaint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private readonly Dictionary<BodyRegionKind, ProjectedSurface> hitTargets = [];
    private Bitmap? tattoo;
    private AnatomicalWorkflowState state = AnatomicalWorkflowState.Default;
    private bool placementVisible;
    private float downX;
    private float lastX;
    private bool dragging;
    private float focusProgress;
    private ValueAnimator? focusAnimator;

    public event Action<BodyRegionKind>? RegionTapped;
    public event Action<double>? RotationChanged;

    public AnatomyView(Context context) : base(context)
    {
        SetMinimumHeight(Dp(330));
        Focusable = true;
        Clickable = false;
        ImportantForAccessibility = ImportantForAccessibility.Yes;
        selectionPaint.SetStyle(Paint.Style.Stroke);
        selectionPaint.StrokeWidth = Dp(5);
        selectionPaint.Color = ResolveColor(global::Android.Resource.Attribute.ColorAccent, Color.Magenta);
        ContentDescription = DescribeState();
    }

    public AnatomicalWorkflowState State => state;
    public float FocusProgress => focusProgress;

    public void SetState(AnatomicalWorkflowState value, bool animateFocus = false)
    {
        var pose = AnatomicalGeometryCatalog.ResolvePose(value.HeightCentimeters,
            value.RotationDegrees, value.CameraDistance);
        state = value with
        {
            HeightCentimeters = pose.UniformScale * AnatomicalDefaults.HeightCentimeters,
            SkinToneValue = Math.Clamp(value.SkinToneValue, 0, 100),
            RotationDegrees = pose.RotationDegrees,
            CameraDistance = pose.CameraDistance
        };
        ContentDescription = DescribeState();
        if (animateFocus && placementVisible) AnimatePlacementFocus();
        else Invalidate();
    }

    /// <summary>
    /// Takes ownership of a placement-ready Android bitmap. Expensive shared
    /// texture generation and bitmap conversion are deliberately performed by
    /// the render worker before this UI-only state swap.
    /// </summary>
    public void SetPreparedTattoo(Bitmap? preparedTattoo, bool visible)
    {
        var replacement = visible ? preparedTattoo : null;
        if (!visible) preparedTattoo?.Dispose();
        var previous = tattoo;
        tattoo = replacement;
        placementVisible = visible && replacement is not null;
        previous?.Dispose();
        ContentDescription = DescribeState();
        if (!placementVisible)
        {
            StopAnimation();
            focusProgress = 0;
            Invalidate();
        }
    }

    public void RotateBy(double degrees)
    {
        ApplyRotation(degrees, notifySettled: true);
    }

    public void ZoomBy(double percent)
    {
        StopAnimation();
        focusProgress = 0;
        state = state with { CameraDistance = Math.Clamp(state.CameraDistance - percent * 0.12, 3.8, 32) };
        ContentDescription = DescribeState();
        Invalidate();
    }

    public Bitmap Capture(float? focusOverride = null)
    {
        var previousFocus = focusProgress;
        var previousHitTargets = hitTargets.ToArray();
        try
        {
            if (focusOverride is not null) focusProgress = Math.Clamp(focusOverride.Value, 0, 1);
            return CaptureScene();
        }
        finally
        {
            focusProgress = previousFocus;
            hitTargets.Clear();
            foreach (var pair in previousHitTargets) hitTargets.Add(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// Captures an AI-only placement without replacing or disposing the bitmap
    /// currently displayed to the user.
    /// </summary>
    public Bitmap CapturePlacement(Bitmap preparedTattoo, AnatomicalWorkflowState placementState,
        float placementFocus)
    {
        ArgumentNullException.ThrowIfNull(preparedTattoo);
        var previousTattoo = tattoo;
        var previousState = state;
        var previousPlacementVisible = placementVisible;
        var previousFocus = focusProgress;
        var previousHitTargets = hitTargets.ToArray();
        try
        {
            tattoo = preparedTattoo;
            state = placementState;
            placementVisible = true;
            focusProgress = Math.Clamp(placementFocus, 0, 1);
            return CaptureScene();
        }
        finally
        {
            tattoo = previousTattoo;
            state = previousState;
            placementVisible = previousPlacementVisible;
            focusProgress = previousFocus;
            hitTargets.Clear();
            foreach (var pair in previousHitTargets) hitTargets.Add(pair.Key, pair.Value);
        }
    }

    private Bitmap CaptureScene()
    {
        var width = Math.Max(1, Width);
        var height = Math.Max(1, Height);
        var result = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)
            ?? throw new InvalidDataException("Android could not allocate the anatomical export bitmap.");
        try
        {
            using var canvas = new Canvas(result);
            DrawScene(canvas, width, height);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        return true;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || !Enabled) return false;
        var accessibility = (AccessibilityManager?)Context?.GetSystemService(Context.AccessibilityService);
        if (accessibility?.IsTouchExplorationEnabled == true) return base.OnTouchEvent(e);
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                downX = lastX = e.GetX();
                dragging = false;
                Parent?.RequestDisallowInterceptTouchEvent(true);
                return true;
            case MotionEventActions.Move:
                var x = e.GetX();
                var delta = x - lastX;
                if (Math.Abs(x - downX) > Dp(6)) dragging = true;
                if (dragging) ApplyRotation(delta * 0.45, notifySettled: false);
                lastX = x;
                return true;
            case MotionEventActions.Up:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                if (dragging)
                {
                    RotationChanged?.Invoke(state.RotationDegrees);
                }
                else
                {
                    PerformClick();
                    var pointX = e.GetX();
                    var pointY = e.GetY();
                    var region = ResolveTappedRegion(pointX, pointY);
                    if (region is not null)
                    {
                        state = state with
                        {
                            Region = region.Value,
                            RotationDegrees = AnatomicalWorkflowState.PreferredRotation(region.Value),
                            CameraDistance = AnatomicalCameraFraming.ForRegion(region.Value).ContextDistance
                        };
                        ContentDescription = DescribeState();
                        RegionTapped?.Invoke(region.Value);
                        Invalidate();
                    }
                }
                return true;
            case MotionEventActions.Cancel:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                if (dragging) RotationChanged?.Invoke(state.RotationDegrees);
                return true;
            default:
                return base.OnTouchEvent(e);
        }
    }

    private void ApplyRotation(double degrees, bool notifySettled)
    {
        StopAnimation();
        state = state with { RotationDegrees = NormalizeDegrees(state.RotationDegrees + degrees) };
        ContentDescription = DescribeState();
        if (notifySettled) RotationChanged?.Invoke(state.RotationDegrees);
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        DrawScene(canvas, Width, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            tattoo?.Dispose();
            tattoo = null;
            skinPaint.Dispose();
            selectionPaint.Dispose();
            tattooPaint.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawScene(Canvas canvas, int width, int height)
    {
        var background = ResolveColor(global::Android.Resource.Attribute.ColorBackground, Color.White);
        canvas.DrawColor(background);
        if (width <= 0 || height <= 0) return;

        var camera = AnatomicalCameraFraming.ForRegion(state.Region);
        var cameraDistance = placementVisible && focusProgress > 0
            ? Lerp(camera.ContextDistance, camera.DetailDistance, focusProgress)
            : state.CameraDistance;
        var zoom = (float)(AnatomicalCameraFraming.OverviewDistance / cameraDistance);
        var meshScale = Math.Min(width / 3.45f, height / 8.2f) * zoom;
        var focusX = placementVisible ? Lerp(camera.ContextFocusX, camera.DetailFocusX, focusProgress) : 0;
        var focusY = placementVisible ? Lerp(camera.ContextFocusY, camera.DetailFocusY, focusProgress) : 0;
        var originX = width / 2f - (float)focusX * meshScale;
        var originY = height * 0.48f + (float)focusY * meshScale;

        skinPaint.SetStyle(Paint.Style.Fill);
        hitTargets.Clear();

        // Interaction and placement bounds come from the same shared mesh surfaces as
        // rendering, so overlapping inner/outer regions can be resolved by view angle.
        var geometry = AnatomicalGeometryCatalog.ForSex(state.Sex);
        var pose = AnatomicalGeometryCatalog.ResolvePose(state.HeightCentimeters,
            state.RotationDegrees, cameraDistance);
        var bodySegments = geometry.BodySegments
            .Select(segment => ProjectBody(segment, pose, originX, originY, meshScale,
                cameraDistance, AnatomicalDefaults.SkinToneFromSlider(state.SkinToneValue)))
            .OrderByDescending(segment => segment.AverageDepth)
            .ToArray();
        foreach (var segment in bodySegments) DrawBodySegment(canvas, segment);

        foreach (var surface in geometry.PlacementSurfaces)
        {
            if (RotationDistance(surface.PreferredRotationDegrees, state.RotationDegrees) > 100) continue;
            var projected = Project(surface.Mesh, pose, originX, originY, meshScale, cameraDistance);
            if (projected.Bounds.Width() >= Dp(12) && projected.Bounds.Height() >= Dp(12))
                hitTargets[surface.Region] = projected;
        }

        if (hitTargets.TryGetValue(state.Region, out var selectedSurface))
        {
            var selected = selectedSurface.Bounds;
            if (placementVisible && tattoo is not null)
            {
                DrawTattoo(canvas, selectedSurface);
                foreach (var occluder in bodySegments.Where(segment =>
                             segment.AverageDepth < selectedSurface.AverageDepth - 0.025 &&
                             !segment.SelectableRegions.Contains(state.Region)))
                    DrawBodySegment(canvas, occluder);
            }
            canvas.DrawRoundRect(selected, Dp(10), Dp(10), selectionPaint);
        }
    }

    private void DrawTattoo(Canvas canvas, ProjectedSurface surface)
    {
        if (tattoo is null) return;
        var mesh = AnatomicalGeometryCatalog.ForSex(state.Sex).GetPlacement(state.Region).Mesh;
        var meshWidth = mesh.Vertices.Select(vertex => vertex.SurfacePoint.U).Distinct().Count() - 1;
        var meshHeight = mesh.Vertices.Select(vertex => vertex.SurfacePoint.V).Distinct().Count() - 1;
        if (meshWidth <= 0 || meshHeight <= 0 ||
            (meshWidth + 1) * (meshHeight + 1) != mesh.Vertices.Length) return;
        var save = canvas.Save();
        tattooPaint.Alpha = 224;
        canvas.DrawBitmapMesh(tattoo, meshWidth, meshHeight, surface.Vertices, 0, null, 0, tattooPaint);
        tattooPaint.Alpha = 255;
        tattooPaint.Color = Color.White;
        canvas.DrawVertices(Canvas.VertexMode.Triangles!, surface.Vertices.Length,
            surface.Vertices, 0, null, 0, surface.ShadeColors, 0, surface.ShortTriangleIndices, 0,
            surface.ShortTriangleIndices.Length, tattooPaint);
        canvas.RestoreToCount(save);
    }

    private BodyRegionKind? ResolveTappedRegion(float x, float y)
    {
        var matches = hitTargets.Where(pair => pair.Value.Contains(x, y)).ToArray();
        if (matches.Length == 0)
            matches = hitTargets.Where(pair => pair.Value.ContainsMinimumTouchTarget(x, y, Dp(48)))
                .ToArray();
        if (matches.Length == 0) return null;
        return matches.OrderBy(pair => RotationDistance(
                AnatomicalWorkflowState.PreferredRotation(pair.Key), state.RotationDegrees))
            .ThenBy(pair => pair.Value.Bounds.Width() * pair.Value.Bounds.Height())
            .ThenBy(pair => pair.Key)
            .First().Key;
    }

    private void DrawBodySegment(Canvas canvas, ProjectedBodySegment segment)
    {
        if (segment.Indices.Length == 0) return;
        skinPaint.Color = Color.White;
        canvas.DrawVertices(Canvas.VertexMode.Triangles!, segment.Vertices.Length,
            segment.Vertices, 0, null, 0, segment.Colors, 0, segment.Indices, 0,
            segment.Indices.Length, skinPaint);
    }

    private static ProjectedBodySegment ProjectBody(AnatomicalBodySegment segment,
        AnatomicalRenderPose pose, float originX, float originY, float scale,
        double cameraDistance, SkinTone tone)
    {
        var vertices = new float[checked(segment.Mesh.Vertices.Length * 2)];
        var colors = new int[segment.Mesh.Vertices.Length];
        var rotatedNormalZ = new double[segment.Mesh.Vertices.Length];
        var radians = pose.RotationDegrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var depth = 0d;
        for (var index = 0; index < segment.Mesh.Vertices.Length; index++)
        {
            var vertex = segment.Mesh.Vertices[index];
            var point = AnatomicalGeometryCatalog.Transform(vertex.Position, pose);
            var perspective = (float)Math.Clamp(cameraDistance / (cameraDistance + point.Z), 0.55, 1.65);
            vertices[index * 2] = originX + (float)point.X * scale * perspective;
            vertices[index * 2 + 1] = originY - (float)point.Y * scale * perspective;
            depth += point.Z;

            var normalX = vertex.Normal.X * cosine + vertex.Normal.Z * sine;
            var normalZ = -vertex.Normal.X * sine + vertex.Normal.Z * cosine;
            rotatedNormalZ[index] = normalZ;
            var illumination = Math.Clamp(
                normalX * -0.32 + vertex.Normal.Y * 0.38 + normalZ * -0.86, 0, 1);
            var shade = 0.64 + illumination * 0.36;
            colors[index] = Color.Argb(255,
                (int)Math.Clamp(Math.Round(tone.Red * shade), 0, 255),
                (int)Math.Clamp(Math.Round(tone.Green * shade), 0, 255),
                (int)Math.Clamp(Math.Round(tone.Blue * shade), 0, 255)).ToArgb();
        }

        var indices = new List<short>(segment.Mesh.TriangleIndices.Length / 2);
        for (var index = 0; index < segment.Mesh.TriangleIndices.Length; index += 3)
        {
            var first = segment.Mesh.TriangleIndices[index];
            var second = segment.Mesh.TriangleIndices[index + 1];
            var third = segment.Mesh.TriangleIndices[index + 2];
            if ((rotatedNormalZ[first] + rotatedNormalZ[second] + rotatedNormalZ[third]) / 3 > 0.08)
                continue;
            indices.Add(checked((short)first));
            indices.Add(checked((short)second));
            indices.Add(checked((short)third));
        }
        return new(segment.SelectableRegions, vertices, colors, indices.ToArray(),
            depth / segment.Mesh.Vertices.Length);
    }

    private static ProjectedSurface Project(AnatomicalTriangleMesh mesh, AnatomicalRenderPose pose,
        float originX, float originY, float scale, double cameraDistance)
    {
        var vertices = new float[checked(mesh.Vertices.Length * 2)];
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        var depth = 0d;
        for (var index = 0; index < mesh.Vertices.Length; index++)
        {
            var vertex = mesh.Vertices[index];
            var point = AnatomicalGeometryCatalog.Transform(vertex.Position, pose);
            depth += point.Z;
            var perspective = (float)Math.Clamp(cameraDistance / (cameraDistance + point.Z), 0.55, 1.65);
            var x = originX + (float)point.X * scale * perspective;
            var y = originY - (float)point.Y * scale * perspective;
            vertices[index * 2] = x;
            vertices[index * 2 + 1] = y;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        var center = (left + right) / 2;
        var halfWidth = Math.Max(1, (right - left) / 2);
        var shadeColors = new int[mesh.Vertices.Length];
        for (var index = 0; index < mesh.Vertices.Length; index++)
        {
            var edge = Math.Abs(vertices[index * 2] - center) / halfWidth;
            shadeColors[index] = Color.Argb((int)Math.Round(Math.Clamp(edge, 0, 1) * 82), 0, 0, 0)
                .ToArgb();
        }
        var triangleIndices = mesh.TriangleIndices.ToArray();
        var shortTriangleIndices = triangleIndices.Select(index => checked((short)index)).ToArray();
        return new ProjectedSurface(new RectF(left, top, right, bottom), vertices,
            triangleIndices, shortTriangleIndices, shadeColors, depth / mesh.Vertices.Length);
    }

    private sealed record ProjectedBodySegment(IReadOnlyCollection<BodyRegionKind> SelectableRegions,
        float[] Vertices, int[] Colors, short[] Indices, double AverageDepth);

    private sealed record ProjectedSurface(RectF Bounds, float[] Vertices, int[] TriangleIndices,
        short[] ShortTriangleIndices, int[] ShadeColors, double AverageDepth)
    {
        public bool Contains(float x, float y)
        {
            if (!Bounds.Contains(x, y)) return false;
            for (var index = 0; index < TriangleIndices.Length; index += 3)
            {
                var first = TriangleIndices[index] * 2;
                var second = TriangleIndices[index + 1] * 2;
                var third = TriangleIndices[index + 2] * 2;
                if (PointInTriangle(x, y, Vertices[first], Vertices[first + 1],
                        Vertices[second], Vertices[second + 1], Vertices[third], Vertices[third + 1]))
                    return true;
            }
            return false;
        }

        public bool ContainsMinimumTouchTarget(float x, float y, float minimumSize)
        {
            var horizontalExpansion = Math.Max(0, (minimumSize - Bounds.Width()) / 2);
            var verticalExpansion = Math.Max(0, (minimumSize - Bounds.Height()) / 2);
            return x >= Bounds.Left - horizontalExpansion && x <= Bounds.Right + horizontalExpansion &&
                   y >= Bounds.Top - verticalExpansion && y <= Bounds.Bottom + verticalExpansion;
        }

        private static bool PointInTriangle(float px, float py, float ax, float ay,
            float bx, float by, float cx, float cy)
        {
            static float Sign(float x1, float y1, float x2, float y2, float x3, float y3) =>
                (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3);
            var first = Sign(px, py, ax, ay, bx, by);
            var second = Sign(px, py, bx, by, cx, cy);
            var third = Sign(px, py, cx, cy, ax, ay);
            return !(first < 0 || second < 0 || third < 0) ||
                   !(first > 0 || second > 0 || third > 0);
        }
    }

    private static double RotationDistance(double first, double second) =>
        Math.Abs(NormalizeDegrees(first - second));

    private static double Lerp(double start, double end, float amount) => start + (end - start) * amount;

    private void AnimatePlacementFocus()
    {
        StopAnimation();
        if (state.ReducedMotion)
        {
            focusProgress = 1;
            Invalidate();
            return;
        }
        focusProgress = 0;
        focusAnimator = ValueAnimator.OfFloat(0, 1)
            ?? throw new InvalidOperationException("Android could not create the placement animation.");
        focusAnimator.StartDelay = 450;
        focusAnimator.SetDuration(2350);
        focusAnimator.SetInterpolator(new Android.Views.Animations.AccelerateDecelerateInterpolator());
        focusAnimator.Update += AnimatorUpdated;
        focusAnimator.Start();
    }

    private void AnimatorUpdated(object? sender, ValueAnimator.AnimatorUpdateEventArgs e)
    {
        if (e.Animation.AnimatedValue is Java.Lang.Float value) focusProgress = value.FloatValue();
        Invalidate();
    }

    private void StopAnimation()
    {
        if (focusAnimator is null) return;
        focusAnimator.Update -= AnimatorUpdated;
        focusAnimator.Cancel();
        focusAnimator.Dispose();
        focusAnimator = null;
    }

    private string DescribeState()
    {
        var region = BodyRegionCatalog.Get(state.Region);
        var sex = state.Sex == AnatomicalSex.Male ? "male" : "female";
        var tone = AnatomicalDefaults.SkinToneFromSlider(state.SkinToneValue).Description;
        var placement = placementVisible ? " Tattoo placement preview active." : string.Empty;
        var visibility = RotationDistance(AnatomicalWorkflowState.PreferredRotation(state.Region),
            state.RotationDegrees) > 100
            ? " The selected surface is turned away; its placement is hidden until that side faces the viewer."
            : string.Empty;
        return $"Interactive {sex} anatomical preview. Selected region: {region.AccessibleDescription}. " +
               $"{AnatomicalDefaults.DescribeHeight(state.HeightCentimeters)}, {tone} complexion. " +
               $"Rotation {state.RotationDegrees:0} degrees, camera distance {state.CameraDistance:0.0}.{placement}{visibility} " +
               "Use the Body region picker for a complete TalkBack-accessible selection method.";
    }

    private Color ResolveColor(int attribute, Color fallback)
    {
        using var value = new global::Android.Util.TypedValue();
        return Context?.Theme?.ResolveAttribute(attribute, value, true) == true
            ? new Color(value.Data)
            : fallback;
    }

    private int Dp(float value) => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1));

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        return value > 180 ? value - 360 : value < -180 ? value + 360 : value;
    }
}
