namespace TATAPP.Core.Workflow;

/// <summary>
/// A platform-owned reference to an imported image. The token is deliberately opaque:
/// Android can persist a content URI or private-copy identifier without putting image
/// bytes or platform objects into saved instance state.
/// </summary>
public sealed record WorkflowSource(
    string Token,
    string DisplayName,
    PhotoFileFormat Format,
    int PixelWidth,
    int PixelHeight)
{
    public WorkflowSource Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Token);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        if (!Enum.IsDefined(Format)) throw new ArgumentOutOfRangeException(nameof(Format));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PixelHeight);
        _ = checked((long)PixelWidth * PixelHeight);
        return this;
    }
}

/// <summary>
/// Compact, platform-neutral anatomical state. Heavy meshes, textures and bitmaps are
/// always reconstructed from this state and the shared catalogs.
/// </summary>
public sealed record AnatomicalWorkflowState(
    AnatomicalSex Sex,
    BodyRegionKind Region,
    double HeightCentimeters,
    double SkinToneValue,
    double RotationDegrees,
    double CameraDistance,
    bool ReducedMotion)
{
    public static AnatomicalWorkflowState Default { get; } = new(
        AnatomicalDefaults.Sex,
        AnatomicalDefaults.Region,
        AnatomicalDefaults.HeightCentimeters,
        AnatomicalDefaults.SkinToneValue,
        PreferredRotation(AnatomicalDefaults.Region),
        AnatomicalCameraFraming.OverviewDistance,
        false);

    public AnatomicalWorkflowState Validate()
    {
        if (!Enum.IsDefined(Sex)) throw new ArgumentOutOfRangeException(nameof(Sex));
        _ = BodyRegionCatalog.Get(Region);
        if (!double.IsFinite(HeightCentimeters) || HeightCentimeters is < 140 or > 200)
            throw new ArgumentOutOfRangeException(nameof(HeightCentimeters));
        if (!double.IsFinite(SkinToneValue) || SkinToneValue is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SkinToneValue));
        if (!double.IsFinite(RotationDegrees) || RotationDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(RotationDegrees));
        if (!double.IsFinite(CameraDistance) || CameraDistance is < 3.8 or > 32)
            throw new ArgumentOutOfRangeException(nameof(CameraDistance));
        return this;
    }

    public static double PreferredRotation(BodyRegionKind region) =>
        AnatomicalDefaults.PreferredRotation(region);

    internal string DescriptionCacheKey => FormattableString.Invariant(
        $"g{AnatomicalGeometryCatalog.GeometryRevision}:{Sex}:{Region}:{HeightCentimeters:0.###}:{SkinToneValue:0.###}:{RotationDegrees:0.###}:{CameraDistance:0.###}:{ReducedMotion}");
}

/// <summary>
/// The complete compact state needed to rebuild the editor after activity recreation
/// or process death. Generation is monotonic and fences asynchronous results.
/// </summary>
public sealed record WorkflowState(
    int SchemaVersion,
    long Generation,
    WorkflowSource? Source,
    TattooStageKind Stage,
    AnatomicalWorkflowState Anatomy,
    bool OfflineAiEnabled)
{
    public const int CurrentSchemaVersion = 1;

    public static WorkflowState Initial { get; } = new(
        CurrentSchemaVersion,
        0,
        null,
        TattooStageKind.Original,
        AnatomicalWorkflowState.Default,
        false);

    public TattooStage CurrentStage => TattooStageNavigator.Get(Stage);
    public int StageNumber => TattooStageNavigator.IndexOf(Stage) + 1;
    public bool HasSource => Source is not null;

    public WorkflowState ValidateForRestore()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"Workflow-state schema {SchemaVersion} is not supported.");
        ArgumentOutOfRangeException.ThrowIfNegative(Generation);
        Source?.Validate();
        _ = TattooStageNavigator.Get(Stage);
        ArgumentNullException.ThrowIfNull(Anatomy);
        Anatomy.Validate();
        if (Source is null && Stage != TattooStageKind.Original)
            throw new InvalidDataException("A workflow without a source image must remain at Original image.");
        if (Source is null && OfflineAiEnabled)
            throw new InvalidDataException("Offline AI cannot be enabled without a source image.");
        return this;
    }
}

public readonly record struct WorkflowWorkToken(long Generation)
{
    public bool IsCurrent(WorkflowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Generation == state.Generation;
    }
}

public readonly record struct WorkflowTransition(WorkflowState State, bool Applied);

public static class TattooStageNavigator
{
    public static TattooStage Get(TattooStageKind kind) =>
        TattooStageCatalog.All.FirstOrDefault(stage => stage.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind));

    public static int IndexOf(TattooStageKind kind)
    {
        for (var index = 0; index < TattooStageCatalog.All.Count; index++)
            if (TattooStageCatalog.All[index].Kind == kind) return index;
        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    public static TattooStage Previous(TattooStageKind kind) =>
        TattooStageCatalog.All[Math.Max(0, IndexOf(kind) - 1)];

    public static TattooStage Next(TattooStageKind kind) =>
        TattooStageCatalog.All[Math.Min(TattooStageCatalog.All.Count - 1, IndexOf(kind) + 1)];

    public static TattooStage AtOneBasedIndex(int stageNumber)
    {
        if (stageNumber < 1 || stageNumber > TattooStageCatalog.All.Count)
            throw new ArgumentOutOfRangeException(nameof(stageNumber));
        return TattooStageCatalog.All[stageNumber - 1];
    }
}

/// <summary>
/// Pure transitions used by every UI input path. A stale expected generation is rejected,
/// and a no-op does not advance the generation or create an accessibility feedback loop.
/// </summary>
public static class WorkflowTransitions
{
    public static WorkflowWorkToken BeginWork(WorkflowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Generation);
    }

    public static bool CanPublish(WorkflowState state, WorkflowWorkToken token) => token.IsCurrent(state);

    public static WorkflowTransition SelectSource(WorkflowState state, long expectedGeneration,
        WorkflowSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Validate();
        return Apply(state, expectedGeneration, current => current with
        {
            Source = source,
            Stage = TattooStageKind.Original,
        }, forceChange: true);
    }

    public static WorkflowTransition ClearSource(WorkflowState state, long expectedGeneration) =>
        Apply(state, expectedGeneration, current => current with
        {
            Source = null,
            Stage = TattooStageKind.Original,
            OfflineAiEnabled = false,
        });

    public static WorkflowTransition SelectStage(WorkflowState state, long expectedGeneration,
        TattooStageKind stage)
    {
        _ = TattooStageNavigator.Get(stage);
        if (state.Source is null && stage != TattooStageKind.Original)
            return new(state, false);
        return Apply(state, expectedGeneration, current => current with { Stage = stage });
    }

    public static WorkflowTransition SelectStageFromSlider(WorkflowState state, long expectedGeneration,
        double sliderValue)
    {
        if (!double.IsFinite(sliderValue)) throw new ArgumentOutOfRangeException(nameof(sliderValue));
        return SelectStage(state, expectedGeneration, TattooStageCatalog.FromSlider(sliderValue).Kind);
    }

    public static WorkflowTransition PreviousStage(WorkflowState state, long expectedGeneration) =>
        SelectStage(state, expectedGeneration, TattooStageNavigator.Previous(state.Stage).Kind);

    public static WorkflowTransition NextStage(WorkflowState state, long expectedGeneration) =>
        SelectStage(state, expectedGeneration, TattooStageNavigator.Next(state.Stage).Kind);

    public static WorkflowTransition SetAnatomy(WorkflowState state, long expectedGeneration,
        AnatomicalWorkflowState anatomy)
    {
        ArgumentNullException.ThrowIfNull(anatomy);
        anatomy.Validate();
        return Apply(state, expectedGeneration, current => current with { Anatomy = anatomy });
    }

    public static WorkflowTransition SelectRegion(WorkflowState state, long expectedGeneration,
        BodyRegionKind region, bool orientToRegion)
    {
        _ = BodyRegionCatalog.Get(region);
        var anatomy = state.Anatomy with
        {
            Region = region,
            RotationDegrees = orientToRegion
                ? AnatomicalWorkflowState.PreferredRotation(region)
                : state.Anatomy.RotationDegrees,
        };
        return SetAnatomy(state, expectedGeneration, anatomy);
    }

    public static WorkflowTransition SetOfflineAiEnabled(WorkflowState state, long expectedGeneration,
        bool enabled)
    {
        if (enabled && state.Source is null)
            throw new InvalidOperationException("Offline AI cannot be enabled without a source image.");
        return Apply(state, expectedGeneration, current => current with { OfflineAiEnabled = enabled });
    }

    private static WorkflowTransition Apply(WorkflowState state, long expectedGeneration,
        Func<WorkflowState, WorkflowState> update, bool forceChange = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(update);
        state.ValidateForRestore();
        if (state.Generation != expectedGeneration) return new(state, false);

        var changed = update(state);
        if (!forceChange && changed == state) return new(state, false);
        changed = changed with { Generation = checked(state.Generation + 1) };
        changed.ValidateForRestore();
        return new(changed, true);
    }
}
