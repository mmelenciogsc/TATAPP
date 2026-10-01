using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using Android.Widget;
using System.Runtime.Versioning;
using TATAPP.Core;

namespace TATAPP.AndroidApp;

internal sealed class StageSeekBar : SeekBar
{
    private static int PreviousStageActionId => Resource.Id.action_previous_stage;
    private static int NextStageActionId => Resource.Id.action_next_stage;

    public event Action<int>? SemanticProgressRequested;

    public StageSeekBar(Context context) : base(context)
    {
        Max = 100;
        Progress = 0;
        // Keep a small measurement cushion because some framework themes trim one
        // physical pixel from SeekBar's reported accessibility bounds.
        SetMinimumHeight((int)Math.Ceiling(52 * (Resources?.DisplayMetrics?.Density ?? 1)));
        ContentDescription = "Visual development stage";
        SetAccessibilityDelegate(new StageAccessibilityDelegate(this));
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null) return;
        var stage = TattooStageCatalog.FromSlider(Progress);
        var index = StageIndex(stage);
        info.ContentDescription = $"Visual development stage. Stage {index} of {TattooStageCatalog.All.Count}: {stage.Name}. {Progress} percent.";
        if (index > 1)
            info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(PreviousStageActionId, "Previous stage"));
        if (index < TattooStageCatalog.All.Count)
            info.AddAction(new AccessibilityNodeInfo.AccessibilityAction(NextStageActionId, "Next stage"));
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) SetStateDescription(this,
            $"Stage {index} of {TattooStageCatalog.All.Count}, {stage.Name}, {Progress} percent");
    }

    private bool HandleAccessibilityAction(global::Android.Views.Accessibility.Action action)
    {
        var index = StageIndex(TattooStageCatalog.FromSlider(Progress));
        if (((int)action == PreviousStageActionId || action == global::Android.Views.Accessibility.Action.ScrollBackward) && index > 1)
        {
            SemanticProgressRequested?.Invoke(AdjacentProgress(-1));
            return true;
        }
        if (((int)action == NextStageActionId || action == global::Android.Views.Accessibility.Action.ScrollForward) && index < TattooStageCatalog.All.Count)
        {
            SemanticProgressRequested?.Invoke(AdjacentProgress(1));
            return true;
        }
        return false;
    }

    private int AdjacentProgress(int delta)
    {
        var current = TattooStageCatalog.FromSlider(Progress);
        var index = StageIndex(current) - 1;
        var next = TattooStageCatalog.All[Math.Clamp(index + delta, 0, TattooStageCatalog.All.Count - 1)];
        return (int)Math.Round(next.RepresentativeSliderValue);
    }

    private static int StageIndex(TattooStage stage)
    {
        for (var index = 0; index < TattooStageCatalog.All.Count; index++)
            if (TattooStageCatalog.All[index].Kind == stage.Kind) return index + 1;
        return 1;
    }

    [SupportedOSPlatform("android30.0")]
    private static void SetStateDescription(global::Android.Views.View view, string description) => view.StateDescription = description;

    private sealed class StageAccessibilityDelegate(StageSeekBar owner) : AccessibilityDelegate
    {
        public override bool PerformAccessibilityAction(global::Android.Views.View? host,
            global::Android.Views.Accessibility.Action action, Bundle? args) =>
            owner.HandleAccessibilityAction(action) || (host is not null && base.PerformAccessibilityAction(host, action, args));
    }
}
