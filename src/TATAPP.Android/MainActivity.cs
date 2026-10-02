using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using TATAPP.Core;
using TATAPP.Core.OfflineAI;
using TATAPP.Core.Workflow;
using Uri = Android.Net.Uri;

namespace TATAPP.AndroidApp;

[Activity(Label = "TATAPP — Tattoo Art Prepper", MainLauncher = true, Exported = true,
    Theme = "@style/AppTheme", LaunchMode = LaunchMode.SingleTop)]
public sealed class MainActivity : Activity, SeekBar.IOnSeekBarChangeListener
{
    private const int PickPhotoRequest = 1001;
    private const int TakePhotoRequest = 1002;
    private const int ExportRequest = 1003;
    private const int HeartbeatTestPulseCount = 3;
    private const int HeartbeatTestDurationMilliseconds =
        ProcessingHeartbeatWaveform.FirstPulseDelayMilliseconds +
        ((HeartbeatTestPulseCount - 1) * ProcessingHeartbeatWaveform.PulseCadenceMilliseconds) +
        ProcessingHeartbeatWaveform.DurationMilliseconds + 250;
    private readonly Dictionary<TattooStageKind, string> offlineDescriptions = [];
    private IAndroidImageService imageService = null!;
    private IAndroidMediaLauncher mediaLauncher = null!;
    private IAndroidDocumentExporter documentExporter = null!;
    private IAndroidExternalLinkLauncher externalLinkLauncher = null!;
    private IAndroidAccessibilityAnnouncer accessibilityAnnouncer = null!;
    private IAndroidAudioFeedback heartbeat = null!;
    private StageFrameCache stageCache = null!;
    private OfflineAiService offlineAi = null!;
    private AndroidPhotoDocument? document;
    private AnatomicalWorkflowState anatomyState = AnatomicalWorkflowState.Default;
    private CancellationTokenSource? activityCancellation;
    private CancellationTokenSource? importCancellation;
    private CancellationTokenSource? restoreCancellation;
    private CancellationTokenSource? renderCancellation;
    private CancellationTokenSource? aiCancellation;
    private CancellationTokenSource? heartbeatTestCancellation;
    private readonly object taskSynchronization = new();
    private readonly HashSet<Task> activeTasks = [];
    private long documentGeneration;
    private long importGeneration;
    private long restoreGeneration;
    private long renderGeneration;
    private long aiOperationGeneration;
    private long sectionNavigationGeneration;
    private long heartbeatTestGeneration;
    private string? pendingCameraPath;
    private string? pendingCameraUri;
    private ExportSnapshot? pendingExport;
    private Bitmap? displayedBitmap;
    private TattooStage displayedStage = TattooStageCatalog.All[0];
    private int displayedStageValue;
    private TattooStageKind? announcedStage;
    private TattooStageKind? pendingReadyAnnouncement;
    private Task? documentRestoreTask;
    private bool synchronizingControls;
    private bool workspaceVisible;
    private bool foreground;
    private bool stageSliderTracking;
    private bool aiPreprocessing;
    private bool aiWorkActive;
    private bool heartbeatTestActive;
    private bool interfaceBusy;
    private AlertDialog? activeDialog;
    private AlertDialog? workspaceNavigationDialog;
    private PendingWorkspaceNavigation? pendingWorkspaceNavigation;
    private BackInvokedCallback? backInvokedCallback;

    private ScrollView workspaceScroll = null!;
    private LinearLayout contentRoot = null!;
    private LinearLayout startPanel = null!;
    private LinearLayout workspacePanel = null!;
    private TextView imageActionsHeading = null!;
    private TextView stageSectionHeading = null!;
    private TextView bodySectionHeading = null!;
    private TextView offlineSectionHeading = null!;
    private TextView sourceText = null!;
    private ImageView preview = null!;
    private AnatomyView anatomy = null!;
    private TextView stageHeading = null!;
    private TextView stageDescription = null!;
    private TextView stageValue = null!;
    private StageSeekBar stageSlider = null!;
    private Button previousButton = null!;
    private Button nextButton = null!;
    private Button saveButton = null!;
    private Button takeButton = null!;
    private Button selectButton = null!;
    private Button blackWidowButton = null!;
    private Button stageNavigationButton = null!;
    private Button bodyNavigationButton = null!;
    private Button offlineNavigationButton = null!;
    private Button heartbeatTestButton = null!;
    private Spinner regionPicker = null!;
    private RadioButton maleButton = null!;
    private RadioButton femaleButton = null!;
    private SeekBar bodySizeSlider = null!;
    private TextView bodySizeValue = null!;
    private SeekBar skinToneSlider = null!;
    private TextView skinToneValue = null!;
    private TextView placementSummary = null!;
    private Switch reducedMotionSwitch = null!;
    private CheckBox offlineAiCheckBox = null!;
    private TextView offlineAiDescription = null!;
    private ProgressBar progress = null!;
    private TextView status = null!;
    private Button resumeButton = null!;
    private Button cancelWorkButton = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        activityCancellation = new CancellationTokenSource();
        var activityManager = (ActivityManager?)GetSystemService(ActivityService);
        var cacheBudget = Math.Min(48L * 1024 * 1024,
            Math.Max(12L * 1024 * 1024, (activityManager?.MemoryClass ?? 128) * 1024L * 1024L / 8));
        var services = ((TatappApplication?)Application)?.Services
            ?? throw new InvalidOperationException("The TATAPP service provider is unavailable.");
        imageService = services.GetRequiredService<IAndroidImageService>();
        mediaLauncher = services.GetRequiredService<IAndroidMediaLauncher>();
        documentExporter = services.GetRequiredService<IAndroidDocumentExporter>();
        externalLinkLauncher = services.GetRequiredService<IAndroidExternalLinkLauncher>();
        accessibilityAnnouncer = services.GetRequiredService<IAndroidAccessibilityAnnouncer>();
        heartbeat = services.GetRequiredService<IAndroidAudioFeedback>();
        stageCache = new StageFrameCache(cacheBudget);
        offlineAi = services.GetRequiredService<OfflineAiService>();
        BuildInterface();
        RestoreCompactState(savedInstanceState);
        RegisterBackCallback();
    }

    private void BuildInterface()
    {
        var scroll = workspaceScroll = new ScrollView(this)
        {
            FillViewport = true,
            Focusable = false,
            ImportantForAccessibility = ImportantForAccessibility.Auto,
        };
        var root = contentRoot = Stack(vertical: true);
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(24));
        root.SetOnApplyWindowInsetsListener(new RootInsetsListener(
            Dp(16), Dp(16), Dp(16), Dp(24)));
        scroll.AddView(root, MatchWrap());

        root.AddView(Heading("TATAPP", 30, true));
        root.AddView(Heading("Tattoo Art Prepper", 20, false));
        root.AddView(Label("Prepare an isolated design, then preview it naturally wrapped around an anatomical body surface."));
        imageActionsHeading = Heading("Image actions and workspace start", 20, false);
        root.AddView(imageActionsHeading);

        var actions = Stack(vertical: Resources?.Configuration?.Orientation != global::Android.Content.Res.Orientation.Landscape);
        actions.SetPadding(0, Dp(8), 0, Dp(8));
        takeButton = ActionButton("Take photo", "Opens the system camera without granting TATAPP camera access.");
        selectButton = ActionButton("Select photo", "Opens Android's system image picker.");
        saveButton = ActionButton("SAVE current look", "Exports exactly the visible semantic stage to a new document.");
        blackWidowButton = ActionButton("BLACK WIDOW TATTOO",
            "Opens the configured Facebook page in your default web browser after activation.");
        saveButton.Enabled = false;
        takeButton.Click += (_, _) => LaunchCamera();
        selectButton.Click += (_, _) => LaunchPicker();
        saveButton.Click += (_, _) => TrackTask(BeginExportAsync());
        blackWidowButton.Click += (_, _) => OpenBlackWidow();
        actions.AddView(takeButton, WeightedWrap(actions.Orientation));
        actions.AddView(selectButton, WeightedWrap(actions.Orientation));
        actions.AddView(saveButton, WeightedWrap(actions.Orientation));
        actions.AddView(blackWidowButton, WeightedWrap(actions.Orientation));
        root.AddView(actions);

        status = Label("Ready. Take or select a photo to begin.");
        status.ContentDescription = "Application status. Ready. Take or select a photo to begin.";
        status.AccessibilityLiveRegion = AccessibilityLiveRegion.Polite;
        status.SetPadding(0, Dp(8), 0, Dp(8));
        root.AddView(status);

        startPanel = Stack(true);
        startPanel.AddView(Heading("Start", 22, false));
        startPanel.AddView(Label("Take a new photograph or select a clear, isolated tattoo design. Ordinary editing stays on this device."));
        resumeButton = ActionButton("Resume workspace", "Returns to the current design without replacing it.");
        resumeButton.Visibility = ViewStates.Gone;
        resumeButton.Click += (_, _) =>
        {
            workspaceVisible = true;
            startPanel.Visibility = ViewStates.Gone;
            workspacePanel.Visibility = ViewStates.Visible;
            SetStatus("Workspace resumed.");
        };
        startPanel.AddView(resumeButton);
        root.AddView(startPanel);

        workspacePanel = Stack(true);
        workspacePanel.Visibility = ViewStates.Gone;
        workspacePanel.AddView(Heading("Workspace", 24, false));
        sourceText = Label("No photo loaded.");
        sourceText.ContentDescription = "Selected photo. No photo loaded.";
        workspacePanel.AddView(sourceText);
        workspacePanel.AddView(CreateWorkspaceNavigationButton());

        stageSectionHeading = Heading("Stage controls", 20, false);
        bodySectionHeading = Heading("Body placement", 20, false);
        offlineSectionHeading = Heading("Offline descriptions", 20, false);
        workspacePanel.AddView(stageSectionHeading);
        stageNavigationButton = CreateWorkspaceNavigationButton();
        workspacePanel.AddView(stageNavigationButton);
        stageHeading = Heading($"Stage 1 of {TattooStageCatalog.All.Count}: {TattooStageCatalog.All[0].Name}", 19, false);
        stageDescription = Label(TattooStageCatalog.All[0].Description);
        stageDescription.ImportantForAccessibility = ImportantForAccessibility.No;
        stageValue = Label("Value 0 percent");
        stageValue.ImportantForAccessibility = ImportantForAccessibility.No;
        workspacePanel.AddView(stageHeading);
        workspacePanel.AddView(stageDescription);
        stageSlider = new StageSeekBar(this);
        stageSlider.SetOnSeekBarChangeListener(this);
        stageSlider.SemanticProgressRequested += SetSemanticProgress;
        workspacePanel.AddView(stageSlider,
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(56)));
        workspacePanel.AddView(stageValue);
        var stageActions = Stack(false);
        previousButton = ActionButton("Previous Stage", "Moves to the previous significant visual stage.");
        nextButton = ActionButton("Next Stage", "Moves to the next significant visual stage.");
        previousButton.Click += (_, _) => MoveStage(-1);
        nextButton.Click += (_, _) => MoveStage(1);
        stageActions.AddView(previousButton, WeightedWrap(Orientation.Horizontal));
        stageActions.AddView(nextButton, WeightedWrap(Orientation.Horizontal));
        workspacePanel.AddView(stageActions);

        workspacePanel.AddView(bodySectionHeading);
        bodyNavigationButton = CreateWorkspaceNavigationButton();
        workspacePanel.AddView(bodyNavigationButton);
        var visualRow = Stack(Resources?.Configuration?.Orientation != global::Android.Content.Res.Orientation.Landscape);
        preview = new ImageView(this)
        {
            Focusable = false,
            ImportantForAccessibility = ImportantForAccessibility.No,
            ContentDescription = "Photo preview. No photo loaded.",
        };
        preview.SetAdjustViewBounds(true);
        preview.SetBackgroundColor(Color.Rgb(28, 26, 31));
        preview.SetScaleType(ImageView.ScaleType.FitCenter);
        anatomy = new AnatomyView(this);
        anatomy.RegionTapped += RegionTapped;
        anatomy.RotationChanged += degrees =>
        {
            anatomyState = anatomyState with { RotationDegrees = degrees };
            InvalidateOfflineDescriptions();
            AnnounceAnatomy();
        };
        if (visualRow.Orientation == Orientation.Horizontal)
        {
            visualRow.AddView(preview, new LinearLayout.LayoutParams(0, Dp(440), 1));
            visualRow.AddView(anatomy, new LinearLayout.LayoutParams(0, Dp(440), 1));
        }
        else
        {
            visualRow.AddView(preview, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(280)));
            visualRow.AddView(anatomy, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(390)));
        }
        var sexGroup = new RadioGroup(this) { Orientation = Orientation.Horizontal };
        maleButton = new RadioButton(this) { Text = "Male", Checked = true, Id = View.GenerateViewId() };
        femaleButton = new RadioButton(this) { Text = "Female", Id = View.GenerateViewId() };
        maleButton.SetMinimumHeight(Dp(48));
        femaleButton.SetMinimumHeight(Dp(48));
        maleButton.ContentDescription = "Male anatomical model";
        femaleButton.ContentDescription = "Female anatomical model";
        sexGroup.AddView(maleButton, new RadioGroup.LayoutParams(0, Wrap, 1));
        sexGroup.AddView(femaleButton, new RadioGroup.LayoutParams(0, Wrap, 1));
        sexGroup.CheckedChange += (_, args) =>
        {
            if (synchronizingControls) return;
            anatomyState = anatomyState with { Sex = args.CheckedId == femaleButton.Id ? AnatomicalSex.Female : AnatomicalSex.Male };
            ApplyAnatomyState(true, true);
        };
        workspacePanel.AddView(sexGroup);

        var regionLabel = Label("Body region");
        regionLabel.SetTypeface(regionLabel.Typeface, Android.Graphics.TypefaceStyle.Bold);
        workspacePanel.AddView(regionLabel);
        regionPicker = new Spinner(this) { ContentDescription = "Body region" };
        regionPicker.SetMinimumHeight(Dp(48));
        regionPicker.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            BodyRegionCatalog.All.Select(item => item.DisplayName).ToArray());
        regionPicker.ItemSelected += (_, args) =>
        {
            if (synchronizingControls || args.Position < 0 || args.Position >= BodyRegionCatalog.All.Count) return;
            var region = BodyRegionCatalog.All[args.Position];
            anatomyState = anatomyState with
            {
                Region = region.Kind,
                RotationDegrees = AnatomicalWorkflowState.PreferredRotation(region.Kind),
                CameraDistance = AnatomicalCameraFraming.ForRegion(region.Kind).ContextDistance,
            };
            ApplyAnatomyState(true, true);
        };
        workspacePanel.AddView(regionPicker);

        var modelActions = Stack(false);
        var rotateLeft = ActionButton("Rotate left", "Rotates the anatomical model fifteen degrees left.");
        var rotateRight = ActionButton("Rotate right", "Rotates the anatomical model fifteen degrees right.");
        var zoomIn = ActionButton("Zoom in", "Magnifies the selected body area.");
        var zoomOut = ActionButton("Zoom out", "Returns toward the whole-body view.");
        rotateLeft.Click += (_, _) => anatomy.RotateBy(-15);
        rotateRight.Click += (_, _) => anatomy.RotateBy(15);
        zoomIn.Click += (_, _) => { anatomy.ZoomBy(10); anatomyState = anatomy.State; InvalidateOfflineDescriptions(); AnnounceAnatomy(); };
        zoomOut.Click += (_, _) => { anatomy.ZoomBy(-10); anatomyState = anatomy.State; InvalidateOfflineDescriptions(); AnnounceAnatomy(); };
        modelActions.AddView(rotateLeft, WeightedWrap(Orientation.Horizontal));
        modelActions.AddView(rotateRight, WeightedWrap(Orientation.Horizontal));
        modelActions.AddView(zoomIn, WeightedWrap(Orientation.Horizontal));
        modelActions.AddView(zoomOut, WeightedWrap(Orientation.Horizontal));
        workspacePanel.AddView(modelActions);

        bodySizeValue = Label("163 centimeters, average Filipino adult");
        workspacePanel.AddView(Label("Body size"));
        bodySizeSlider = new SeekBar(this) { Max = 60, Progress = 23, ContentDescription = "Body size" };
        bodySizeSlider.SetMinimumHeight(Dp(52));
        bodySizeSlider.ProgressChanged += (_, args) =>
        {
            var centimeters = 140 + args.Progress;
            bodySizeValue.Text = AnatomicalDefaults.DescribeHeight(centimeters);
            UpdateBodySizeSemantics(centimeters);
            if (!args.FromUser || synchronizingControls) return;
            anatomyState = anatomyState with { HeightCentimeters = centimeters };
            ApplyAnatomyState(false, false);
        };
        bodySizeSlider.StopTrackingTouch += (_, _) => AnnounceAnatomy();
        workspacePanel.AddView(bodySizeSlider,
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(56)));
        workspacePanel.AddView(bodySizeValue);
        UpdateBodySizeSemantics(AnatomicalDefaults.HeightCentimeters);

        skinToneValue = Label("Light brown to medium tan, Filipino");
        workspacePanel.AddView(Label("Skin tone and complexion"));
        skinToneSlider = new SeekBar(this) { Max = 100, Progress = 55, ContentDescription = "Skin tone and complexion" };
        skinToneSlider.SetMinimumHeight(Dp(52));
        skinToneSlider.ProgressChanged += (_, args) =>
        {
            skinToneValue.Text = AnatomicalDefaults.SkinToneFromSlider(args.Progress).Description;
            UpdateSkinToneSemantics(args.Progress);
            if (!args.FromUser || synchronizingControls) return;
            anatomyState = anatomyState with { SkinToneValue = args.Progress };
            ApplyAnatomyState(false, false);
        };
        skinToneSlider.StopTrackingTouch += (_, _) => AnnounceAnatomy();
        workspacePanel.AddView(skinToneSlider,
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(56)));
        workspacePanel.AddView(skinToneValue);
        UpdateSkinToneSemantics(AnatomicalDefaults.SkinToneValue);

        reducedMotionSwitch = new Switch(this)
        {
            Text = "Reduce anatomy motion",
            ContentDescription = "Reduce anatomy motion. Shows the selected body area without animation.",
        };
        reducedMotionSwitch.SetMinimumHeight(Dp(48));
        reducedMotionSwitch.CheckedChange += (_, args) =>
        {
            if (synchronizingControls) return;
            anatomyState = anatomyState with { ReducedMotion = args.IsChecked };
            ApplyAnatomyState(false, true);
        };
        workspacePanel.AddView(reducedMotionSwitch);

        placementSummary = Label(string.Empty);
        workspacePanel.AddView(placementSummary);

        workspacePanel.AddView(offlineSectionHeading);
        offlineNavigationButton = CreateWorkspaceNavigationButton();
        workspacePanel.AddView(offlineNavigationButton);
        heartbeatTestButton = ActionButton("Test processing heartbeat",
            "Plays three processing heartbeat pulses over about ten seconds using the same offline-processing audio path.");
        heartbeatTestButton.Click += (_, _) =>
        {
            if (heartbeatTestActive)
                StopHeartbeatTest("Processing heartbeat test stopped.");
            else
                TrackTask(RunHeartbeatTestAsync());
        };
        workspacePanel.AddView(heartbeatTestButton);
        offlineAiCheckBox = new CheckBox(this)
        {
            Text = "Offline AI Describe",
            ContentDescription = "Offline AI Describe. Installs and uses a validated local model only after consent; no image is uploaded.",
        };
        offlineAiCheckBox.SetMinimumHeight(Dp(48));
        offlineAiCheckBox.Enabled = false;
        offlineAiCheckBox.CheckedChange += (_, args) =>
        {
            if (synchronizingControls) return;
            if (args.IsChecked) TrackTask(EnableOfflineAiAsync());
            else
                StopOfflineAi("Offline AI Describe is off.", updateStatus: false);
        };
        workspacePanel.AddView(offlineAiCheckBox);
        offlineAiDescription = Label("Offline AI Describe is off.");
        offlineAiDescription.Focusable = true;
        offlineAiDescription.ContentDescription = "Offline AI visual description. Offline AI Describe is off.";
        offlineAiDescription.AccessibilityLiveRegion = AccessibilityLiveRegion.None;
        workspacePanel.AddView(offlineAiDescription);

        progress = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Visibility = ViewStates.Gone,
            Indeterminate = true,
            ContentDescription = "Processing progress",
        };
        progress.SetMinimumHeight(Dp(8));
        workspacePanel.AddView(progress);
        cancelWorkButton = ActionButton("Cancel offline AI", "Stops the current model download or description preparation safely.");
        cancelWorkButton.Visibility = ViewStates.Gone;
        cancelWorkButton.Click += (_, _) => CancelOfflineAiWork();
        workspacePanel.AddView(cancelWorkButton);
        workspacePanel.AddView(CreateWorkspaceNavigationButton());

        var previewsHeading = Heading("Current previews", 20, false);
        previewsHeading.ImportantForAccessibility = ImportantForAccessibility.No;
        workspacePanel.AddView(previewsHeading);
        workspacePanel.AddView(visualRow);
        root.AddView(workspacePanel);
        SetContentView(scroll);
        root.RequestApplyInsets();
        ApplyAnatomyState(false, false);
        UpdateStageText(0);
    }

    private Button CreateWorkspaceNavigationButton()
    {
        var button = ActionButton("Workspace navigation",
            "Opens a native list for moving to image actions, stage controls, body placement controls, or offline descriptions.");
        button.Click += (_, _) => ShowWorkspaceNavigation(button);
        return button;
    }

    private void ShowWorkspaceNavigation(View invoker)
    {
        var generation = Interlocked.Increment(ref sectionNavigationGeneration);
        pendingWorkspaceNavigation = null;
        var previousDialog = workspaceNavigationDialog;
        workspaceNavigationDialog = null;
        previousDialog?.Dismiss();

        var choices = new[]
        {
            "Image actions and workspace start",
            "Stage controls",
            "Body placement controls",
            "Offline descriptions",
        };
        AlertDialog? dialog = null;
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Go to workspace section");
        builder.SetItems(choices, (_, args) =>
        {
            if (generation != Interlocked.Read(ref sectionNavigationGeneration)) return;
            pendingWorkspaceNavigation = new(generation, invoker, args.Which);
        });
        builder.SetNegativeButton("Cancel", (_, _) =>
        {
            if (generation != Interlocked.Read(ref sectionNavigationGeneration)) return;
            pendingWorkspaceNavigation = new(generation, invoker, null);
        });
        builder.SetOnCancelListener(new DialogCancelListener(() =>
        {
            if (generation != Interlocked.Read(ref sectionNavigationGeneration)) return;
            pendingWorkspaceNavigation ??= new(generation, invoker, null);
        }));
        builder.SetOnDismissListener(new DialogDismissListener(() =>
        {
            if (ReferenceEquals(workspaceNavigationDialog, dialog)) workspaceNavigationDialog = null;
            CompletePendingWorkspaceNavigation();
        }));
        dialog = builder.Create() ?? throw new InvalidOperationException(
            "Android could not create the workspace navigation dialog.");
        dialog.SetCanceledOnTouchOutside(false);
        workspaceNavigationDialog = dialog;
        dialog.Show();
    }

    private void CompletePendingWorkspaceNavigation()
    {
        var pending = pendingWorkspaceNavigation;
        if (pending is null) return;
        if (pending.Generation != Interlocked.Read(ref sectionNavigationGeneration) || IsFinishing || IsDestroyed ||
            !workspaceVisible || workspacePanel.Visibility != ViewStates.Visible || !pending.Invoker.IsAttachedToWindow)
        {
            pendingWorkspaceNavigation = null;
            return;
        }
        if (!HasWindowFocus) return;
        pendingWorkspaceNavigation = null;
        if (pending.Selection is null)
        {
            RestoreWorkspaceNavigationInvoker(pending.Invoker, pending.Generation);
            return;
        }

        switch (pending.Selection.Value)
        {
            case 0:
                NavigateToSection(imageActionsHeading, takeButton, selectButton, saveButton, blackWidowButton);
                break;
            case 1:
                NavigateToSection(stageSectionHeading, stageNavigationButton, stageSlider,
                    previousButton, nextButton);
                break;
            case 2:
                NavigateToSection(bodySectionHeading, bodyNavigationButton, maleButton,
                    femaleButton, regionPicker);
                break;
            case 3:
                NavigateToSection(offlineSectionHeading, offlineNavigationButton,
                    heartbeatTestButton, offlineAiCheckBox, cancelWorkButton);
                break;
        }
    }

    private void RestoreWorkspaceNavigationInvoker(View invoker, long generation)
    {
        workspaceScroll.PostOnAnimation(new UiRunnable(() =>
        {
            if (generation != Interlocked.Read(ref sectionNavigationGeneration) || IsFinishing || IsDestroyed ||
                !workspaceVisible || workspacePanel.Visibility != ViewStates.Visible ||
                !workspaceScroll.IsAttachedToWindow || !invoker.IsAttachedToWindow)
                return;

            var accessibilityManager = (global::Android.Views.Accessibility.AccessibilityManager?)
                GetSystemService(global::Android.Content.Context.AccessibilityService);
            if (accessibilityManager?.IsTouchExplorationEnabled == true)
                invoker.PerformAccessibilityAction(Android.Views.Accessibility.Action.AccessibilityFocus, null);
            else
                invoker.RequestFocus();
        }));
    }

    private void NavigateToSection(View heading, params View[] actionCandidates)
    {
        var generation = Interlocked.Increment(ref sectionNavigationGeneration);
        var targetBounds = new Rect();
        heading.GetDrawingRect(targetBounds);
        contentRoot.OffsetDescendantRectToMyCoords(heading, targetBounds);
        workspaceScroll.ScrollTo(0, Math.Max(0, targetBounds.Top - Dp(8)));
        heading.GetDrawingRect(targetBounds);
        heading.RequestRectangleOnScreen(targetBounds, immediate: true);

        // Focus changes occur only after a deliberate shortcut activation. TalkBack receives
        // accessibility focus; keyboard and switch users receive ordinary focus as a fallback.
        workspaceScroll.PostOnAnimation(new UiRunnable(() =>
        {
            if (generation != Interlocked.Read(ref sectionNavigationGeneration) || IsFinishing || IsDestroyed ||
                !workspaceVisible || workspacePanel.Visibility != ViewStates.Visible ||
                !workspaceScroll.IsAttachedToWindow || !heading.IsAttachedToWindow)
                return;

            var firstActionable = FirstEnabledAction(actionCandidates);
            var accessibilityManager = (global::Android.Views.Accessibility.AccessibilityManager?)
                GetSystemService(global::Android.Content.Context.AccessibilityService);
            if (accessibilityManager?.IsTouchExplorationEnabled == true)
            {
                if (!heading.PerformAccessibilityAction(Android.Views.Accessibility.Action.AccessibilityFocus, null) &&
                    firstActionable is { Enabled: true, Visibility: ViewStates.Visible })
                    firstActionable.PerformAccessibilityAction(Android.Views.Accessibility.Action.AccessibilityFocus, null);
                return;
            }

            if (firstActionable is { Enabled: true, Visibility: ViewStates.Visible })
                firstActionable.RequestFocus();
        }));
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) CompletePendingWorkspaceNavigation();
    }

    private void InvalidateWorkspaceNavigation(bool dismissDialog)
    {
        Interlocked.Increment(ref sectionNavigationGeneration);
        pendingWorkspaceNavigation = null;
        if (!dismissDialog) return;
        var dialog = workspaceNavigationDialog;
        workspaceNavigationDialog = null;
        dialog?.Dismiss();
    }

    private static View? FirstEnabledAction(params View[] candidates) =>
        candidates.FirstOrDefault(candidate => candidate.Enabled && candidate.Visibility == ViewStates.Visible);

    private void LaunchPicker()
    {
        try { mediaLauncher.LaunchPhotoPicker(this, PickPhotoRequest); }
        catch (ActivityNotFoundException exception) { ShowError("No system image picker is available.", exception); }
    }

    private void LaunchCamera()
    {
        try
        {
            var directory = System.IO.Path.Combine(FilesDir?.AbsolutePath
                ?? throw new IOException("App-private storage is unavailable."), "captures");
            Directory.CreateDirectory(directory);
            pendingCameraPath = System.IO.Path.Combine(directory, $"capture-{Guid.NewGuid():N}.jpg");
            using (File.Create(pendingCameraPath)) { }
            pendingCameraUri = $"content://{CaptureFileProvider.Authority}/capture/{System.IO.Path.GetFileName(pendingCameraPath)}";
            var output = Uri.Parse(pendingCameraUri) ??
                         throw new InvalidOperationException("Camera output URI is invalid.");
            mediaLauncher.LaunchCamera(this, output, TakePhotoRequest);
        }
        catch (ActivityNotFoundException exception)
        {
            DeletePendingCamera();
            ShowError("No system camera application is available. Use Select photo instead.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                             InvalidOperationException or Java.Lang.SecurityException)
        {
            DeletePendingCamera();
            ShowError("TATAPP could not prepare app-private camera storage. Use Select photo instead.", exception);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == PickPhotoRequest)
        {
            if (resultCode != Result.Ok || data?.Data is null)
            {
                SetStatus("Photo selection canceled. The current design was kept.");
                return;
            }
            TrackTask(LoadPhotoAsync(data.Data));
            return;
        }
        if (requestCode == TakePhotoRequest)
        {
            if (resultCode != Result.Ok || pendingCameraUri is null)
            {
                DeletePendingCamera();
                SetStatus("Camera capture canceled. The current design was kept.");
                return;
            }
            var uri = Uri.Parse(pendingCameraUri);
            if (uri is not null) TrackTask(LoadPhotoAsync(uri, deleteCaptureAfterward: true));
            return;
        }
        if (requestCode == ExportRequest)
        {
            if (resultCode != Result.Ok || data?.Data is null || pendingExport is null)
            {
                if (pendingExport is { } canceledExport)
                    TryDeleteAnatomicalSnapshot(canceledExport.AnatomicalSnapshotPath);
                pendingExport = null;
                SetStatus("Save canceled. The source image was not changed.");
                return;
            }
            var snapshot = pendingExport.Value;
            var restore = documentRestoreTask;
            TrackTask(restore is { IsCompleted: false }
                ? CompleteExportAfterRestoreAsync(data.Data, snapshot, restore)
                : CompleteExportAsync(data.Data, snapshot));
            pendingExport = null;
        }
    }

    private async Task LoadPhotoAsync(Uri uri, bool deleteCaptureAfterward = false)
    {
        restoreCancellation?.Cancel();
        Interlocked.Increment(ref restoreGeneration);
        importCancellation?.Cancel();
        importCancellation?.Dispose();
        importCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            activityCancellation?.Token ?? CancellationToken.None);
        var token = importCancellation.Token;
        var generation = Interlocked.Increment(ref importGeneration);
        await RunOnUiThreadAsync(() => SetBusy(true, "Loading and validating the selected photo."));
        AndroidPhotoDocument? loaded = null;
        Bitmap? preparedPreview = null;
        try
        {
            loaded = await imageService.ImportAsync(uri, token).ConfigureAwait(false);
            preparedPreview = await Task.Run(() => imageService.ToBitmap(loaded.Preview), token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref importGeneration))
            {
                TryDeletePrivateImport(loaded.PrivatePath);
                return;
            }
            var accepted = false;
            await RunOnUiThreadAsync(() =>
            {
                if (token.IsCancellationRequested || generation != Interlocked.Read(ref importGeneration)) return;
                AcceptDocument(loaded, preparedPreview!);
                accepted = true;
            });
            if (accepted)
            {
                loaded = null;
                preparedPreview = null;
            }
        }
        catch (System.OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or OutOfMemoryException)
        {
            await RunOnUiThreadAsync(() =>
            {
                if (generation == Interlocked.Read(ref importGeneration))
                    ShowError("The photo could not be opened.", exception);
            });
        }
        finally
        {
            preparedPreview?.Dispose();
            if (loaded is not null) TryDeletePrivateImport(loaded.PrivatePath);
            await RunOnUiThreadAsync(() =>
            {
                if (deleteCaptureAfterward) DeletePendingCamera();
                if (generation == Interlocked.Read(ref importGeneration)) SetBusy(false);
            });
        }
    }

    private void AcceptDocument(AndroidPhotoDocument loaded, Bitmap preparedPreview,
        bool showWorkspace = true,
        bool advanceGeneration = true)
    {
        InvalidateWorkspaceNavigation(dismissDialog: true);
        StopHeartbeatTest(statusMessage: null);
        var previousPath = document?.PrivatePath;
        var previousId = document?.Id;
        document = loaded;
        if (advanceGeneration) documentGeneration++;
        renderCancellation?.Cancel();
        Interlocked.Increment(ref aiOperationGeneration);
        var cancellation = aiCancellation;
        aiCancellation = null;
        cancellation?.Cancel();
        aiPreprocessing = false;
        if (aiWorkActive) activeDialog?.Cancel();
        aiWorkActive = false;
        heartbeat.Stop();
        offlineDescriptions.Clear();
        synchronizingControls = true;
        offlineAiCheckBox.Checked = false;
        synchronizingControls = false;
        offlineAiDescription.Text = "Offline AI Describe is off for the newly loaded photo.";
        offlineAiDescription.ContentDescription = "Offline AI visual description. " + offlineAiDescription.Text;
        stageCache.Clear();
        if (previousId is not null) offlineAi.InvalidateSource(previousId);
        announcedStage = null;
        pendingReadyAnnouncement = null;
        workspaceVisible = showWorkspace;
        startPanel.Visibility = showWorkspace ? ViewStates.Gone : ViewStates.Visible;
        resumeButton.Visibility = ViewStates.Visible;
        workspacePanel.Visibility = showWorkspace ? ViewStates.Visible : ViewStates.Gone;
        sourceText.Text = $"{loaded.DisplayName} — {loaded.PixelWidth} by {loaded.PixelHeight} pixels";
        sourceText.ContentDescription = $"Selected photo. {sourceText.Text}";
        saveButton.Enabled = true;
        stageSlider.Enabled = true;
        offlineAiCheckBox.Enabled = true;
        stageSlider.Progress = 0;
        DisplayStageFrame(loaded.Preview, TattooStageCatalog.All[0], 0,
            preparedPreview, preparedTattoo: null, announceReady: false);
        SetStatus($"Loaded {loaded.DisplayName}. Original format: {PhotoFileFormats.Get(loaded.Format).DisplayName}. Stage 1 of {TattooStageCatalog.All.Count} is ready.");
        if (previousPath is not null && !string.Equals(previousPath, loaded.PrivatePath, StringComparison.Ordinal))
            TryDeletePrivateImport(previousPath);
    }

    private void RequestStageRender(int value, bool announceLoading)
    {
        UpdateStageText(value);
        if (document is null) return;
        renderCancellation?.Cancel();
        renderCancellation?.Dispose();
        renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(activityCancellation?.Token ?? CancellationToken.None);
        var token = renderCancellation.Token;
        var generation = Interlocked.Increment(ref renderGeneration);
        var source = document;
        var stage = TattooStageCatalog.FromSlider(value);
        if (announcedStage is { } priorStage && priorStage != stage.Kind)
        {
            pendingReadyAnnouncement = null;
            announcedStage = null;
        }
        if (announceLoading && announcedStage != stage.Kind)
        {
            announcedStage = stage.Kind;
            pendingReadyAnnouncement = stage.Kind;
            SetStatus($"Loading. Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}.");
        }
        TrackTask(RenderAfterSettleAsync(source, stage, value, generation, token));
    }

    private async Task RenderAfterSettleAsync(AndroidPhotoDocument source, TattooStage stage, int sliderValue,
        long generation, CancellationToken token)
    {
        Bitmap? preparedPreview = null;
        Bitmap? preparedTattoo = null;
        try
        {
            await Task.Delay(65, token).ConfigureAwait(false);
            ImageFrame frame;
            if (!stageCache.TryGet(source.Id, stage.Kind, out frame!))
            {
                frame = await Task.Run(() => TattooStageCatalog.RenderDesign(source.Preview, stage, token), token)
                    .ConfigureAwait(false);
                stageCache.Put(source.Id, stage.Kind, frame);
            }
            (preparedPreview, preparedTattoo) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var previewBitmap = imageService.ToBitmap(frame);
                try
                {
                    Bitmap? tattooBitmap = null;
                    if (stage.IsAnatomicalPlacement)
                    {
                        var placementTexture = TattooInkTexture.CreatePlacementTexture(frame);
                        token.ThrowIfCancellationRequested();
                        tattooBitmap = imageService.ToBitmap(placementTexture);
                    }
                    return (previewBitmap, tattooBitmap);
                }
                catch
                {
                    previewBitmap.Dispose();
                    throw;
                }
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref renderGeneration) || !ReferenceEquals(source, document)) return;
            await RunOnUiThreadAsync(() =>
            {
                if (token.IsCancellationRequested || generation != Interlocked.Read(ref renderGeneration) ||
                    !ReferenceEquals(source, document)) return;
                var announceReady = pendingReadyAnnouncement == stage.Kind;
                if (announceReady) pendingReadyAnnouncement = null;
                DisplayStageFrame(frame, stage, sliderValue, preparedPreview!,
                    preparedTattoo, announceReady);
                preparedPreview = null;
                preparedTattoo = null;
            });
        }
        catch (System.OperationCanceledException) { }
        catch (Exception exception) when (exception is InvalidDataException or OutOfMemoryException or ArgumentException)
        {
            await RunOnUiThreadAsync(() =>
            {
                if (generation == Interlocked.Read(ref renderGeneration) && ReferenceEquals(source, document))
                {
                    pendingReadyAnnouncement = null;
                    announcedStage = null;
                    ShowError("The selected stage could not be rendered.", exception);
                }
            });
        }
        finally
        {
            preparedPreview?.Dispose();
            preparedTattoo?.Dispose();
        }
    }

    private void DisplayStageFrame(ImageFrame frame, TattooStage stage, int sliderValue,
        Bitmap preparedPreview, Bitmap? preparedTattoo, bool announceReady)
    {
        ArgumentNullException.ThrowIfNull(preparedPreview);
        preview.SetImageBitmap(preparedPreview);
        var old = displayedBitmap;
        displayedBitmap = preparedPreview;
        displayedStage = stage;
        displayedStageValue = sliderValue;
        old?.Dispose();
        preview.ContentDescription = $"Preview of {document?.DisplayName}. Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}. {stage.Description}";
        anatomy.SetPreparedTattoo(preparedTattoo, stage.IsAnatomicalPlacement);
        anatomy.SetState(anatomyState, animateFocus: stage.IsAnatomicalPlacement);
        UpdateStageText(sliderValue);
        UpdatePlacementSummary();
        if (offlineDescriptions.TryGetValue(stage.Kind, out var description))
        {
            offlineAiDescription.Text = description;
            offlineAiDescription.ContentDescription = "Offline AI visual description. " + description;
        }
        if (announceReady)
            SetStatus($"Ready. Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}.");
    }

    private void UpdateStageText(int value)
    {
        var stage = TattooStageCatalog.FromSlider(value);
        stageHeading.Text = $"Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}";
        stageDescription.Text = stage.Description;
        stageValue.Text = $"Value {value} percent";
        var summaryState = displayedStage.Kind == stage.Kind && displayedStageValue == value
            ? "Current preview."
            : "Selected stage. Loading preview.";
        stageHeading.ContentDescription =
            $"{summaryState} Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}. " +
            $"Value {value} percent. {stage.Description}";
        previousButton.Enabled = StageIndex(stage) > 1;
        nextButton.Enabled = StageIndex(stage) < TattooStageCatalog.All.Count;
        stageSlider.ContentDescription = $"Visual development stage. Stage {StageIndex(stage)} of {TattooStageCatalog.All.Count}: {stage.Name}. {value} percent.";
    }

    private void MoveStage(int delta)
    {
        var current = TattooStageCatalog.FromSlider(stageSlider.Progress);
        var index = StageIndex(current) - 1;
        var next = TattooStageCatalog.All[Math.Clamp(index + delta, 0, TattooStageCatalog.All.Count - 1)];
        SetSemanticProgress((int)Math.Round(next.RepresentativeSliderValue));
    }

    private void SetSemanticProgress(int progressValue)
    {
        stageSlider.Progress = progressValue;
        RequestStageRender(progressValue, announceLoading: true);
    }

    public void OnProgressChanged(SeekBar? seekBar, int progress, bool fromUser)
    {
        if (!ReferenceEquals(seekBar, stageSlider)) return;
        UpdateStageText(progress);
        if (fromUser) RequestStageRender(progress, announceLoading: !stageSliderTracking);
    }

    public void OnStartTrackingTouch(SeekBar? seekBar)
    {
        if (ReferenceEquals(seekBar, stageSlider)) stageSliderTracking = true;
    }
    public void OnStopTrackingTouch(SeekBar? seekBar)
    {
        if (!ReferenceEquals(seekBar, stageSlider)) return;
        stageSliderTracking = false;
        RequestStageRender(stageSlider.Progress, announceLoading: true);
    }

    private async Task BeginExportAsync()
    {
        var source = document;
        if (source is null) return;
        var stage = displayedStage;
        var sliderValue = displayedStageValue;
        var exportAnatomy = anatomyState;
        var focusProgress = anatomy.FocusProgress;
        var sourceGeneration = documentGeneration;
        var supported = source.Format is PhotoFileFormat.Jpeg or PhotoFileFormat.Png;
        var outputFormat = supported ? source.Format : PhotoFileFormat.Png;
        string? anatomicalSnapshotPath = null;
        if (stage.IsAnatomicalPlacement)
        {
            SetBusy(true, "Capturing the exact visible anatomical preview for export.");
            try
            {
                using var capture = anatomy.Capture(focusProgress);
                anatomicalSnapshotPath = await SaveAnatomicalSnapshotAsync(capture,
                    activityCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                 InvalidDataException or OutOfMemoryException or
                                                 System.OperationCanceledException)
            {
                await RunOnUiThreadAsync(() => ShowError(
                    "The anatomical preview could not be captured for saving.", exception));
                return;
            }
            finally
            {
                await RunOnUiThreadAsync(() => SetBusy(false));
            }
        }
        if (sourceGeneration != documentGeneration || !ReferenceEquals(source, document))
        {
            TryDeleteAnatomicalSnapshot(anatomicalSnapshotPath);
            return;
        }
        var snapshot = new ExportSnapshot(sourceGeneration, stage, sliderValue,
            outputFormat, !supported, exportAnatomy, focusProgress, anatomicalSnapshotPath);
        var extension = PhotoFileFormats.Get(outputFormat).PreferredExtension;
        var baseName = System.IO.Path.GetFileNameWithoutExtension(source.DisplayName);
        var safeStage = string.Join('-', stage.Name.ToLowerInvariant().Split([' ', '_', '—'], StringSplitOptions.RemoveEmptyEntries));
        var mimeType = outputFormat == PhotoFileFormat.Jpeg ? "image/jpeg" : "image/png";
        var suggestedName = $"{baseName}-tattoo-{safeStage}{extension}";
        try
        {
            await RunOnUiThreadAsync(() =>
            {
                if (sourceGeneration != documentGeneration || !ReferenceEquals(source, document))
                    throw new System.OperationCanceledException("The source changed before export selection.");
                pendingExport = snapshot;
                if (!supported)
                    SetStatus($"Android has no reliable {PhotoFileFormats.Get(source.Format).DisplayName} encoder. Choose a location for a non-destructive PNG copy.");
                documentExporter.LaunchCreateDocument(this, mimeType, suggestedName, ExportRequest);
            });
        }
        catch (ActivityNotFoundException exception)
        {
            TryDeleteAnatomicalSnapshot(anatomicalSnapshotPath);
            await RunOnUiThreadAsync(() =>
            {
                pendingExport = null;
                ShowError("No system document provider is available for saving.", exception);
            });
        }
        catch (System.OperationCanceledException)
        {
            TryDeleteAnatomicalSnapshot(anatomicalSnapshotPath);
        }
    }

    private async Task CompleteExportAsync(Uri destination, ExportSnapshot snapshot)
    {
        var source = document;
        if (source is null || snapshot.DocumentGeneration != documentGeneration)
        {
            TryDeleteAnatomicalSnapshot(snapshot.AnatomicalSnapshotPath);
            await RunOnUiThreadAsync(() => SetStatus("Save stopped because the source image changed."));
            return;
        }
        if (string.Equals(source.OriginalUri, destination.ToString(), StringComparison.Ordinal))
        {
            TryDeleteAnatomicalSnapshot(snapshot.AnatomicalSnapshotPath);
            await RunOnUiThreadAsync(() => SetStatus("TATAPP will not overwrite the source image. Choose a new filename."));
            return;
        }
        await RunOnUiThreadAsync(() => SetBusy(true, $"Rendering {snapshot.Stage.Name} for export."));
        try
        {
            ImageFrame output;
            var downsampled = false;
            if (snapshot.Stage.IsAnatomicalPlacement)
            {
                output = await LoadAnatomicalSnapshotAsync(snapshot.AnatomicalSnapshotPath,
                    activityCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                var decoded = await imageService.DecodeForExportAsync(source,
                    activityCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
                downsampled = decoded.Downsampled;
                output = await Task.Run(() => TattooStageCatalog.RenderDesign(decoded.Frame, snapshot.Stage,
                    activityCancellation?.Token ?? CancellationToken.None)).ConfigureAwait(false);
            }
            await imageService.SaveAsync(output, destination, snapshot.OutputFormat,
                activityCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => SetStatus($"Saved {snapshot.Stage.Name} as {PhotoFileFormats.Get(snapshot.OutputFormat).DisplayName}." +
                (snapshot.UsesFallback ? " The original format was unsupported, so a PNG copy was created." : string.Empty) +
                (downsampled ? " Memory pressure required export at the high-quality preview dimensions." : string.Empty)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                                             or NotSupportedException or OutOfMemoryException or System.OperationCanceledException
                                             or Java.IO.FileNotFoundException or Java.Lang.SecurityException)
        {
            await RunOnUiThreadAsync(() => ShowError("The image could not be saved.", exception));
        }
        finally
        {
            TryDeleteAnatomicalSnapshot(snapshot.AnatomicalSnapshotPath);
            await RunOnUiThreadAsync(() => SetBusy(false));
        }
    }

    private static async Task AwaitRestoreAndExportAsync(Task restoreTask,
        Func<Task> export)
    {
        await restoreTask.ConfigureAwait(false);
        await export().ConfigureAwait(false);
    }

    private Task CompleteExportAfterRestoreAsync(Uri destination, ExportSnapshot snapshot, Task restoreTask) =>
        AwaitRestoreAndExportAsync(restoreTask, () => CompleteExportAsync(destination, snapshot));

    private async Task EnableOfflineAiAsync()
    {
        StopHeartbeatTest(statusMessage: null);
        var operationGeneration = Interlocked.Increment(ref aiOperationGeneration);
        var priorCancellation = aiCancellation;
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            activityCancellation?.Token ?? CancellationToken.None);
        aiCancellation = operationCancellation;
        priorCancellation?.Cancel();
        var token = operationCancellation.Token;
        var source = document;
        var sourceGeneration = documentGeneration;
        var anatomySnapshot = anatomyState;
        var terminal = 0;
        aiWorkActive = true;
        try
        {
            if (source is null)
            {
                Interlocked.Exchange(ref terminal, 1);
                await DisableOfflineAiAsync("Load a photo before enabling Offline AI Describe.",
                    operationGeneration);
                return;
            }
            await RunOnUiThreadAsync(() => SetBusy(true,
                "Checking device headroom and verifying any installed offline model files."));
            var capability = await offlineAi.CaptureCapabilitiesAsync(token).ConfigureAwait(false);
            OfflineModelVariant? model = null;
            ModelInstallationStatus? status = null;
            foreach (var candidate in offlineAi.Catalog.Variants
                         .OrderBy(candidate => candidate.WorkingSetBytes)
                         .ThenBy(candidate => candidate.Artifacts.Sum(artifact => artifact.InstalledBytes))
                         .ThenBy(candidate => candidate.QualityRank))
            {
                if (!OfflineModelPolicy.IsSuitable(candidate, capability,
                        OfflineModelAdmissionMode.InstalledUse)) continue;
                var candidateStatus = await offlineAi.GetStatusAsync(candidate, token).ConfigureAwait(false);
                if (candidateStatus.State != ModelInstallationState.Ready) continue;
                model = candidate;
                status = candidateStatus;
                break;
            }
            model ??= OfflineModelPolicy.SelectSmallestSuitable(offlineAi.Catalog, capability,
                OfflineModelAdmissionMode.Installation);
            if (model is null)
            {
                Interlocked.Exchange(ref terminal, 1);
                await DisableOfflineAiAsync("Offline AI Describe is unavailable because no tested model tier has sufficient API, ABI, memory, storage, CPU, and acceleration headroom on this device. Ordinary editing remains fully offline.",
                    operationGeneration);
                return;
            }
            status ??= await offlineAi.GetStatusAsync(model, token).ConfigureAwait(false);
            var cacheKey = OfflineDescriptionCacheKey.Create(source.Id, model, offlineAi.Catalog,
                "tatapp-android-objective-stage-v2", anatomySnapshot);
            EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                anatomySnapshot, model, cacheKey, token);
            var consented = status.State == ModelInstallationState.Ready;
            if (!consented)
            {
                var accepted = await ConfirmAsync("Install offline model",
                    $"Install {model.DisplayName} ({FormatBytes(model.Artifacts.Sum(artifact => artifact.DownloadBytes))} total) in app-private storage? The two matching model files download over HTTPS, resume when possible, and are verified before atomic installation. Photos are never transmitted.",
                    operationGeneration, token);
                if (!accepted)
                {
                    Interlocked.Exchange(ref terminal, 1);
                    await DisableOfflineAiAsync("Offline model installation canceled.", operationGeneration);
                    return;
                }
                consented = true;
                await RunOnUiThreadAsync(() =>
                {
                    EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                        anatomySnapshot, model, cacheKey, token);
                    SetBusy(true, "Downloading the offline model artifact set.");
                    progress.Indeterminate = false;
                    progress.Max = 100;
                });
                var progressReporter = new CoalescingModelInstallProgress(update =>
                {
                    TrackTask(RunOnUiThreadAsync(() =>
                    {
                        if (Volatile.Read(ref terminal) != 0 ||
                            !IsCurrentAiOperation(operationGeneration, source, sourceGeneration,
                                anatomySnapshot, model, cacheKey, token)) return;
                        var percentage = update.TotalBytes <= 0 ? 0 :
                            (int)Math.Clamp(update.CompletedBytes * 100 / update.TotalBytes, 0, 100);
                        progress.Progress = percentage;
                        progress.ContentDescription = $"Offline model {update.Phase.ToString().ToLowerInvariant()}, {percentage} percent";
                        SetStatus(update.Phase == ModelInstallPhase.Downloading
                            ? $"Offline model download {percentage} percent."
                            : update.Message);
                    }));
                });
                await offlineAi.EnsureReadyAsync(model, consented, progressReporter, token).ConfigureAwait(false);
            }
            else
                await offlineAi.EnsureReadyAsync(model, true, null, token).ConfigureAwait(false);

            EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                anatomySnapshot, model, cacheKey, token);
            await RunOnUiThreadAsync(() =>
            {
                EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                    anatomySnapshot, model, cacheKey, token);
                SetBusy(true,
                    "Opening the verified offline model and preparing descriptions sequentially. No photo data leaves this device.");
                aiPreprocessing = true;
                if (foreground && !heartbeat.Start())
                    SetStatus("The processing heartbeat is unavailable. Offline description preparation continues with visible progress.");
            });
            var stageProgress = new CallbackProgress<OfflineDescriptionProgress>(update =>
                TrackTask(RunOnUiThreadAsync(() =>
                {
                    if (Volatile.Read(ref terminal) != 0 ||
                        !IsCurrentAiOperation(operationGeneration, source, sourceGeneration,
                            anatomySnapshot, model, cacheKey, token)) return;
                    progress.Indeterminate = false;
                    progress.Max = update.Total;
                    progress.Progress = update.Completed;
                    progress.ContentDescription = $"Offline description preparation, {update.Completed} of {update.Total} stages";
                    offlineAiDescription.Text =
                        $"Preparing offline descriptions: {update.Completed} of {update.Total}, {update.Stage.Name}.";
                    offlineAiDescription.ContentDescription =
                        "Offline AI visual description. " + offlineAiDescription.Text;
                })));
            var imageSource = new DelegatingStageDescriptionImageSource((stage, maximumDimension, stageToken) =>
                RenderOfflineDescriptionStageAsync(source, stage, anatomySnapshot, maximumDimension, stageToken));
            var batch = await offlineAi.PreloadAsync(cacheKey, model, imageSource, stageProgress, token)
                .ConfigureAwait(false);
            EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                anatomySnapshot, model, cacheKey, token);
            Interlocked.Exchange(ref terminal, 1);
            await RunOnUiThreadAsync(() =>
            {
                EnsureCurrentAiOperation(operationGeneration, source, sourceGeneration,
                    anatomySnapshot, model, cacheKey, token);
                offlineDescriptions.Clear();
                foreach (var pair in batch.Descriptions) offlineDescriptions[pair.Key] = pair.Value;
                var selected = TattooStageCatalog.FromSlider(stageSlider.Progress);
                offlineAiDescription.Text = offlineDescriptions[selected.Kind];
                offlineAiDescription.ContentDescription = "Offline AI visual description. " + offlineAiDescription.Text;
                heartbeat.Stop();
                aiPreprocessing = false;
                aiWorkActive = false;
                if (ReferenceEquals(aiCancellation, operationCancellation)) aiCancellation = null;
                SetBusy(false);
                SetStatus($"Offline descriptions are ready for all {batch.Descriptions.Count} stages.");
            });
        }
        catch (System.OperationCanceledException)
        {
            Interlocked.Exchange(ref terminal, 1);
            await DisableOfflineAiAsync("Offline AI preparation canceled.", operationGeneration);
        }
        catch (OutOfMemoryException exception)
        {
            Interlocked.Exchange(ref terminal, 1);
            if (operationGeneration == Interlocked.Read(ref aiOperationGeneration))
            {
                operationCancellation.Cancel();
                stageCache.Clear();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
            }
            await DisableOfflineAiAsync("Offline AI Describe stopped before memory exhaustion. Its model session and cached previews were released; non-AI editing remains available. " + exception.Message,
                operationGeneration);
        }
        catch (Exception exception) when (IsExpectedOfflineAiFailure(exception))
        {
            Interlocked.Exchange(ref terminal, 1);
            if (operationGeneration == Interlocked.Read(ref aiOperationGeneration))
                operationCancellation.Cancel();
            await DisableOfflineAiAsync("Offline AI Describe could not start because the local model runtime failed. The app has returned to non-AI editing. " + exception.Message,
                operationGeneration);
        }
        finally
        {
            try
            {
                if (operationGeneration == Interlocked.Read(ref aiOperationGeneration) && !IsDestroyed)
                    await RunOnUiThreadAsync(() =>
                    {
                        if (operationGeneration != Interlocked.Read(ref aiOperationGeneration)) return;
                        aiPreprocessing = false;
                        aiWorkActive = false;
                        heartbeat.Stop();
                        if (ReferenceEquals(aiCancellation, operationCancellation)) aiCancellation = null;
                        SetBusy(false);
                    });
            }
            finally
            {
                operationCancellation.Dispose();
            }
        }
    }

    private static bool IsExpectedOfflineAiFailure(Exception exception) => exception is
        IOException or HttpRequestException or InvalidDataException or NotSupportedException or
        UnauthorizedAccessException or InvalidOperationException or ArgumentException or
        DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
        LLama.Exceptions.RuntimeError or LLama.Exceptions.LLamaDecodeError or
        LLama.Exceptions.LoadWeightsFailedException or LLama.Exceptions.ContextOverflowException or
        LLama.Exceptions.MissingTemplateException or LLama.Exceptions.TemplateNotFoundException ||
        exception is TypeInitializationException
        {
            InnerException: DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
        };

    private async Task<IRenderedStageImage> RenderOfflineDescriptionStageAsync(AndroidPhotoDocument source,
        TattooStage stage, AnatomicalWorkflowState anatomySnapshot, int maximumDimension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = TattooStageCatalog.RenderDesign(source.Preview, stage, cancellationToken);
        if (!stage.IsAnatomicalPlacement)
            return EncodeRenderedStage(rendered, maximumDimension, cancellationToken);

        Bitmap? preparedTattoo = null;
        Bitmap? capture = null;
        try
        {
            preparedTattoo = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var texture = TattooInkTexture.CreatePlacementTexture(rendered);
                cancellationToken.ThrowIfCancellationRequested();
                return imageService.ToBitmap(texture);
            }, cancellationToken).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                capture = anatomy.CapturePlacement(preparedTattoo!, anatomySnapshot, 1f);
            });
            return await Task.Run(() => EncodeBitmap(capture ??
                    throw new InvalidDataException("Android could not capture the anatomical preview."),
                maximumDimension, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            capture?.Dispose();
            preparedTattoo?.Dispose();
        }
    }

    private bool IsCurrentAiOperation(long operationGeneration, AndroidPhotoDocument source,
        long sourceGeneration, AnatomicalWorkflowState anatomySnapshot, OfflineModelVariant model,
        OfflineDescriptionCacheKey cacheKey, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !foreground || IsFinishing || IsDestroyed ||
            operationGeneration != Interlocked.Read(ref aiOperationGeneration) ||
            sourceGeneration != Interlocked.Read(ref documentGeneration) ||
            !ReferenceEquals(source, document) || anatomyState != anatomySnapshot ||
            !offlineAiCheckBox.Checked) return false;
        return cacheKey == OfflineDescriptionCacheKey.Create(source.Id, model, offlineAi.Catalog,
            "tatapp-android-objective-stage-v2", anatomyState);
    }

    private void EnsureCurrentAiOperation(long operationGeneration, AndroidPhotoDocument source,
        long sourceGeneration, AnatomicalWorkflowState anatomySnapshot, OfflineModelVariant model,
        OfflineDescriptionCacheKey cacheKey, CancellationToken cancellationToken)
    {
        if (!IsCurrentAiOperation(operationGeneration, source, sourceGeneration,
                anatomySnapshot, model, cacheKey, cancellationToken))
            throw new System.OperationCanceledException(
                "The source, anatomical placement, or activity lifecycle changed during offline AI work.",
                cancellationToken);
    }

    private Task DisableOfflineAiAsync(string message, long operationGeneration) => RunOnUiThreadAsync(() =>
    {
        if (operationGeneration != Interlocked.Read(ref aiOperationGeneration) || IsDestroyed) return;
        synchronizingControls = true;
        offlineAiCheckBox.Checked = false;
        synchronizingControls = false;
        offlineDescriptions.Clear();
        offlineAiDescription.Text = message;
        offlineAiDescription.ContentDescription = "Offline AI visual description. " + message;
        SetStatus(message);
    });

    private Task<bool> ConfirmAsync(string title, string message, long operationGeneration,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        AlertDialog? dialog = null;
        var registration = cancellationToken.Register(() =>
        {
            completion.TrySetCanceled(cancellationToken);
            if (IsDestroyed) return;
            RunOnUiThread(() =>
            {
                if (!ReferenceEquals(activeDialog, dialog)) return;
                activeDialog = null;
                dialog?.Cancel();
                dialog?.Dismiss();
            });
        });
        _ = completion.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        RunOnUiThread(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsFinishing || IsDestroyed ||
                operationGeneration != Interlocked.Read(ref aiOperationGeneration))
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }
            activeDialog?.Dismiss();
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle(title);
            builder.SetMessage(message);
            builder.SetPositiveButton("Install", (_, _) =>
            {
                if (ReferenceEquals(activeDialog, dialog)) activeDialog = null;
                completion.TrySetResult(true);
            });
            builder.SetNegativeButton("Cancel", (_, _) =>
            {
                if (ReferenceEquals(activeDialog, dialog)) activeDialog = null;
                completion.TrySetResult(false);
            });
            builder.SetOnCancelListener(new DialogCancelListener(() =>
            {
                if (ReferenceEquals(activeDialog, dialog)) activeDialog = null;
                completion.TrySetResult(false);
            }));
            dialog = builder.Create();
            activeDialog = dialog;
            dialog?.Show();
        });
        return completion.Task;
    }

    private void RegionTapped(BodyRegionKind region)
    {
        // The visual model also resolves the region's preferred rotation and
        // camera frame. Adopt the complete state so picker and model stay exact.
        anatomyState = anatomy.State;
        synchronizingControls = true;
        regionPicker.SetSelection(BodyRegionCatalog.All.Select(item => item.Kind).ToList().IndexOf(region));
        synchronizingControls = false;
        ApplyAnatomyState(true, true);
    }

    private void ApplyAnatomyState(bool announce, bool animate)
    {
        InvalidateOfflineDescriptions();
        anatomy.SetState(anatomyState, animate);
        UpdatePlacementSummary();
        if (announce) AnnounceAnatomy();
    }

    private void InvalidateOfflineDescriptions()
    {
        if (offlineDescriptions.Count == 0 && !offlineAiCheckBox.Checked) return;
        StopOfflineAi(
            "Offline descriptions were cleared because anatomical placement changed. Enable Offline AI Describe again to refresh them.",
            updateStatus: false);
    }

    private void AnnounceAnatomy()
    {
        UpdatePlacementSummary();
        var region = BodyRegionCatalog.Get(anatomyState.Region);
        SetStatus($"Anatomical selection updated. {region.AccessibleDescription}; " +
                  $"{AnatomicalDefaults.DescribeHeight(anatomyState.HeightCentimeters)}; " +
                  $"{AnatomicalDefaults.SkinToneFromSlider(anatomyState.SkinToneValue).Description} complexion; " +
                  $"rotation {anatomyState.RotationDegrees:0} degrees; camera distance {anatomyState.CameraDistance:0.0}.");
    }

    private void UpdatePlacementSummary()
    {
        var region = BodyRegionCatalog.Get(anatomyState.Region);
        var sex = anatomyState.Sex == AnatomicalSex.Male ? "Male" : "Female";
        var tone = AnatomicalDefaults.SkinToneFromSlider(anatomyState.SkinToneValue).Description;
        var placement = displayedStage.IsAnatomicalPlacement
            ? "Tattoo placement preview active."
            : "Tattoo placement preview is not active at this stage.";
        var rotationDistance = Math.Abs((AnatomicalWorkflowState.PreferredRotation(anatomyState.Region) -
                                         anatomyState.RotationDegrees) % 360);
        rotationDistance = Math.Min(rotationDistance, 360 - rotationDistance);
        var visibility = rotationDistance > 100
            ? " The selected surface is turned away; rotate it toward the viewer to show the placement."
            : string.Empty;
        placementSummary.Text =
            $"Current placement. {sex} anatomical model. Selected region: {region.AccessibleDescription}. " +
            $"{AnatomicalDefaults.DescribeHeight(anatomyState.HeightCentimeters)}; {tone} complexion; " +
            $"rotation {anatomyState.RotationDegrees:0} degrees; camera distance {anatomyState.CameraDistance:0.0}. " +
            placement + visibility;
        placementSummary.ContentDescription = placementSummary.Text;
    }

    private void OpenBlackWidow()
    {
        try
        {
            var destination = Uri.Parse(ProductMetadata.BlackWidowTattooUrl) ??
                              throw new InvalidOperationException("The configured external link is invalid.");
            externalLinkLauncher.Open(this, destination);
            SetStatus("Opening the BLACK WIDOW TATTOO Facebook page in the default web browser.");
        }
        catch (ActivityNotFoundException exception)
        {
            ShowError("The BLACK WIDOW TATTOO page could not be opened in the default web browser.", exception);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        interfaceBusy = busy;
        progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
        cancelWorkButton.Visibility = busy && aiWorkActive ? ViewStates.Visible : ViewStates.Gone;
        cancelWorkButton.Enabled = busy && aiWorkActive;
        if (busy) progress.Indeterminate = true;
        takeButton.Enabled = !busy;
        selectButton.Enabled = !busy;
        saveButton.Enabled = !busy && document is not null;
        stageSlider.Enabled = !busy && document is not null;
        previousButton.Enabled = !busy && StageIndex(TattooStageCatalog.FromSlider(stageSlider.Progress)) > 1;
        nextButton.Enabled = !busy && StageIndex(TattooStageCatalog.FromSlider(stageSlider.Progress)) < TattooStageCatalog.All.Count;
        offlineAiCheckBox.Enabled = !busy && document is not null;
        heartbeatTestButton.Enabled = heartbeatTestActive || !busy;
        if (message is not null) SetStatus(message);
    }

    private async Task RunHeartbeatTestAsync()
    {
        if (interfaceBusy || aiWorkActive || aiPreprocessing)
        {
            SetStatus("The heartbeat test is unavailable while another operation is active.");
            return;
        }

        var generation = Interlocked.Increment(ref heartbeatTestGeneration);
        heartbeatTestCancellation?.Cancel();
        heartbeatTestCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            activityCancellation?.Token ?? CancellationToken.None);
        heartbeatTestCancellation = cancellation;
        if (!heartbeat.Start())
        {
            if (ReferenceEquals(heartbeatTestCancellation, cancellation)) heartbeatTestCancellation = null;
            cancellation.Dispose();
            SetStatus("The processing heartbeat is unavailable. Visual processing progress remains available.");
            return;
        }

        heartbeatTestActive = true;
        heartbeatTestButton.Text = "Stop heartbeat test";
        heartbeatTestButton.ContentDescription =
            "Stop heartbeat test. Stops the three-pulse processing heartbeat demonstration.";
        offlineAiCheckBox.Enabled = false;
        SetStatus("Processing heartbeat test started. Three pulses will play over about ten seconds.");
        try
        {
            await Task.Delay(HeartbeatTestDurationMilliseconds, cancellation.Token).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                if (generation == Interlocked.Read(ref heartbeatTestGeneration) &&
                    heartbeatTestActive && !IsFinishing && !IsDestroyed)
                    StopHeartbeatTest("Processing heartbeat test complete.");
            }).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(heartbeatTestCancellation, cancellation)) heartbeatTestCancellation = null;
            cancellation.Dispose();
        }
    }

    private void StopHeartbeatTest(string? statusMessage)
    {
        var wasActive = heartbeatTestActive;
        Interlocked.Increment(ref heartbeatTestGeneration);
        var cancellation = heartbeatTestCancellation;
        heartbeatTestCancellation = null;
        cancellation?.Cancel();
        heartbeatTestActive = false;
        heartbeat.Stop();
        heartbeatTestButton.Text = "Test processing heartbeat";
        heartbeatTestButton.ContentDescription =
            "Test processing heartbeat. Plays three processing heartbeat pulses over about ten seconds using the same offline-processing audio path.";
        heartbeatTestButton.Enabled = !interfaceBusy;
        offlineAiCheckBox.Enabled = !interfaceBusy && document is not null;
        if (wasActive && statusMessage is not null) SetStatus(statusMessage);
    }

    private void CancelOfflineAiWork()
    {
        if (!aiWorkActive) return;
        StopOfflineAi("Offline AI preparation canceled. Ordinary editing remains available.",
            updateStatus: true);
    }

    private void StopOfflineAi(string description, bool updateStatus)
    {
        var wasActive = aiWorkActive;
        Interlocked.Increment(ref aiOperationGeneration);
        aiPreprocessing = false;
        aiWorkActive = false;
        var cancellation = aiCancellation;
        aiCancellation = null;
        cancellation?.Cancel();
        heartbeat.Stop();
        if (wasActive) activeDialog?.Cancel();
        synchronizingControls = true;
        offlineAiCheckBox.Checked = false;
        synchronizingControls = false;
        offlineDescriptions.Clear();
        offlineAiDescription.Text = description;
        offlineAiDescription.ContentDescription = "Offline AI visual description. " + description;
        if (wasActive) SetBusy(false);
        if (updateStatus) SetStatus(description);
    }

    private void SetStatus(string message)
    {
        accessibilityAnnouncer.UpdateStatus(status, message);
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            try
            {
                if (IsDestroyed) completion.TrySetCanceled();
                else { action(); completion.TrySetResult(); }
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private void TrackTask(Task task)
    {
        lock (taskSynchronization) activeTasks.Add(task);
        _ = task.ContinueWith(completed =>
        {
            lock (taskSynchronization) activeTasks.Remove(task);
            if (!completed.IsFaulted || completed.Exception is null) return;
            var failure = completed.Exception.Flatten();
            global::Android.Util.Log.Error("TATAPP", "Unexpected background task failure: " + failure);
            if (IsFinishing || IsDestroyed) return;
            RunOnUiThread(() =>
            {
                if (!IsFinishing && !IsDestroyed)
                    ShowError("An unexpected background operation failed.", failure.InnerExceptions[0]);
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void ShowError(string summary, Exception exception)
    {
        var message = summary + " " + exception.Message;
        SetStatus(message);
        if (IsFinishing || IsDestroyed) return;
        activeDialog?.Dismiss();
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("TATAPP");
        builder.SetMessage(message);
        builder.SetPositiveButton("OK", (_, _) => activeDialog = null);
        builder.SetOnCancelListener(new DialogCancelListener(() => activeDialog = null));
        activeDialog = builder.Create();
        activeDialog?.Show();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutBoolean("workspace", workspaceVisible);
        outState.PutLong("documentGeneration", documentGeneration);
        outState.PutInt("stage", stageSlider.Progress);
        outState.PutInt("sex", (int)anatomyState.Sex);
        outState.PutInt("region", (int)anatomyState.Region);
        outState.PutDouble("height", anatomyState.HeightCentimeters);
        outState.PutDouble("tone", anatomyState.SkinToneValue);
        outState.PutDouble("rotation", anatomyState.RotationDegrees);
        outState.PutDouble("cameraDistance", anatomyState.CameraDistance);
        outState.PutBoolean("reduced", anatomyState.ReducedMotion);
        outState.PutString("capturePath", pendingCameraPath);
        outState.PutString("captureUri", pendingCameraUri);
        if (pendingExport is { } export)
        {
            outState.PutBoolean("hasPendingExport", true);
            outState.PutLong("exportGeneration", export.DocumentGeneration);
            outState.PutInt("exportStage", (int)export.Stage.Kind);
            outState.PutDouble("exportSlider", export.SliderValue);
            outState.PutInt("exportFormat", (int)export.OutputFormat);
            outState.PutBoolean("exportFallback", export.UsesFallback);
            outState.PutInt("exportSex", (int)export.Anatomy.Sex);
            outState.PutInt("exportRegion", (int)export.Anatomy.Region);
            outState.PutDouble("exportHeight", export.Anatomy.HeightCentimeters);
            outState.PutDouble("exportTone", export.Anatomy.SkinToneValue);
            outState.PutDouble("exportRotation", export.Anatomy.RotationDegrees);
            outState.PutDouble("exportCameraDistance", export.Anatomy.CameraDistance);
            outState.PutBoolean("exportReduced", export.Anatomy.ReducedMotion);
            outState.PutDouble("exportFocus", export.AnatomyFocusProgress);
            outState.PutString("exportSnapshotPath", export.AnatomicalSnapshotPath);
        }
        if (document is not null)
        {
            outState.PutString("sourcePath", document.PrivatePath);
            outState.PutString("sourceUri", document.OriginalUri);
            outState.PutString("sourceName", document.DisplayName);
        }
    }

    private void RestoreCompactState(Bundle? state)
    {
        if (state is null)
        {
            SweepAbandonedPrivateInputs([]);
            return;
        }
        workspaceVisible = state.GetBoolean("workspace", false);
        documentGeneration = Math.Max(0, state.GetLong("documentGeneration", 0));
        pendingCameraPath = state.GetString("capturePath");
        pendingCameraUri = state.GetString("captureUri");
        try
        {
            anatomyState = new AnatomicalWorkflowState(
                (AnatomicalSex)state.GetInt("sex", (int)AnatomicalDefaults.Sex),
                (BodyRegionKind)state.GetInt("region", (int)AnatomicalDefaults.Region),
                state.GetDouble("height", AnatomicalDefaults.HeightCentimeters),
                state.GetDouble("tone", AnatomicalDefaults.SkinToneValue),
                state.GetDouble("rotation", AnatomicalWorkflowState.PreferredRotation(AnatomicalDefaults.Region)),
                state.GetDouble("cameraDistance", AnatomicalCameraFraming.OverviewDistance),
                state.GetBoolean("reduced", false)).Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            anatomyState = AnatomicalWorkflowState.Default;
        }
        synchronizingControls = true;
        maleButton.Checked = anatomyState.Sex == AnatomicalSex.Male;
        femaleButton.Checked = anatomyState.Sex == AnatomicalSex.Female;
        regionPicker.SetSelection(BodyRegionCatalog.All.Select(item => item.Kind).ToList().IndexOf(anatomyState.Region));
        bodySizeSlider.Progress = (int)Math.Round(anatomyState.HeightCentimeters - 140);
        skinToneSlider.Progress = (int)Math.Round(anatomyState.SkinToneValue);
        reducedMotionSwitch.Checked = anatomyState.ReducedMotion;
        stageSlider.Progress = Math.Clamp(state.GetInt("stage", 0), stageSlider.Min, stageSlider.Max);
        synchronizingControls = false;
        ApplyAnatomyState(false, false);
        var path = state.GetString("sourcePath");
        var name = state.GetString("sourceName");
        RestorePendingExport(state);
        if (path is not null && name is not null && File.Exists(path))
        {
            restoreCancellation?.Cancel();
            restoreCancellation?.Dispose();
            restoreCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                activityCancellation?.Token ?? CancellationToken.None);
            var generation = Interlocked.Increment(ref restoreGeneration);
            documentRestoreTask = RestorePhotoAsync(path, state.GetString("sourceUri"), name,
                stageSlider.Progress, workspaceVisible, generation, restoreCancellation.Token);
            TrackTask(documentRestoreTask);
        }
        SweepAbandonedPrivateInputs([path, pendingCameraPath, pendingExport?.AnatomicalSnapshotPath]);
    }

    private async Task RestorePhotoAsync(string path, string? originalUri, string displayName, int stage,
        bool showWorkspace, long generation, CancellationToken cancellationToken)
    {
        Bitmap? preparedPreview = null;
        try
        {
            var loaded = await imageService.OpenPrivateAsync(path, originalUri, displayName,
                cancellationToken).ConfigureAwait(false);
            preparedPreview = await Task.Run(() => imageService.ToBitmap(loaded.Preview), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref restoreGeneration)) return;
            await RunOnUiThreadAsync(() =>
            {
                if (generation != Interlocked.Read(ref restoreGeneration)) return;
                AcceptDocument(loaded, preparedPreview!, showWorkspace, advanceGeneration: false);
                preparedPreview = null;
                stageSlider.Progress = stage;
                RequestStageRender(stage, announceLoading: false);
                SetStatus("Workspace restored from app-private state.");
            });
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (OutOfMemoryException exception)
        {
            stageCache.Clear();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
            await RunOnUiThreadAsync(() =>
            {
                if (generation == Interlocked.Read(ref restoreGeneration))
                    ShowError(
                        "The previous workspace could not be restored within the device memory budget. Select the source again to continue.",
                        exception);
            });
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await RunOnUiThreadAsync(() =>
            {
                if (generation == Interlocked.Read(ref restoreGeneration))
                    ShowError("The previous workspace could not be restored.", exception);
            });
        }
        finally
        {
            preparedPreview?.Dispose();
        }
    }

    private void RestorePendingExport(Bundle state)
    {
        if (!state.GetBoolean("hasPendingExport", false)) return;
        try
        {
            var exportAnatomy = new AnatomicalWorkflowState(
                (AnatomicalSex)state.GetInt("exportSex"),
                (BodyRegionKind)state.GetInt("exportRegion"),
                state.GetDouble("exportHeight"),
                state.GetDouble("exportTone"),
                state.GetDouble("exportRotation"),
                state.GetDouble("exportCameraDistance"),
                state.GetBoolean("exportReduced")).Validate();
            pendingExport = new ExportSnapshot(
                state.GetLong("exportGeneration"),
                TattooStageNavigator.Get((TattooStageKind)state.GetInt("exportStage")),
                state.GetDouble("exportSlider"),
                (PhotoFileFormat)state.GetInt("exportFormat"),
                state.GetBoolean("exportFallback"), exportAnatomy,
                state.GetDouble("exportFocus", 0), state.GetString("exportSnapshotPath"));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                             InvalidOperationException or NotSupportedException)
        {
            pendingExport = null;
        }
    }

    public override void OnTrimMemory(TrimMemory level)
    {
        base.OnTrimMemory(level);
        if (level is TrimMemory.RunningLow or TrimMemory.RunningCritical || level >= TrimMemory.Background)
        {
            stageCache.Clear();
            CancelStageRenderForMemoryPressure();
            restoreCancellation?.Cancel();
            StopHeartbeatTest("Processing heartbeat test stopped because Android reported memory pressure.");
        }
        if (level == TrimMemory.RunningCritical || level >= TrimMemory.UiHidden)
        {
            StopOfflineAi(
                "Offline AI was stopped because Android reported memory pressure. Re-enable it to prepare descriptions again.",
                updateStatus: true);
        }
    }

    public override void OnLowMemory()
    {
        stageCache.Clear();
        CancelStageRenderForMemoryPressure();
        importCancellation?.Cancel();
        restoreCancellation?.Cancel();
        StopHeartbeatTest("Processing heartbeat test stopped because Android reported low memory.");
        StopOfflineAi(
            "Offline AI was stopped because Android reported low memory. Re-enable it to prepare descriptions again.",
            updateStatus: true);
        base.OnLowMemory();
    }

    private void CancelStageRenderForMemoryPressure()
    {
        renderCancellation?.Cancel();
        Interlocked.Increment(ref renderGeneration);
        pendingReadyAnnouncement = null;
        announcedStage = null;
        if (document is null || displayedBitmap is null || stageSlider.Progress == displayedStageValue) return;

        // Do not immediately retry allocation-heavy rendering in a memory callback. Restore the
        // controls to the bitmap that remains visible so navigation and export agree after cleanup.
        stageSlider.Progress = displayedStageValue;
        UpdateStageText(displayedStageValue);
        SetStatus($"Stage rendering stopped because Android reported memory pressure. Showing Stage {StageIndex(displayedStage)} of {TattooStageCatalog.All.Count}: {displayedStage.Name}.");
    }

    protected override void OnResume()
    {
        base.OnResume();
        foreground = true;
        if (aiPreprocessing && !heartbeat.Start())
            SetStatus("The processing heartbeat is unavailable. Offline description preparation continues with visible progress.");
    }

    protected override void OnStop()
    {
        foreground = false;
        InvalidateWorkspaceNavigation(dismissDialog: true);
        StopHeartbeatTest("Processing heartbeat test stopped because TATAPP left the foreground.");
        if (aiWorkActive || offlineAiCheckBox.Checked || offlineDescriptions.Count > 0)
            StopOfflineAi(
                "Offline AI was stopped while TATAPP was in the background. Re-enable it to prepare descriptions again.",
                updateStatus: false);
        base.OnStop();
    }

    public override void OnBackPressed() => HandleBack();

    private void HandleBack()
    {
        if (workspaceNavigationDialog is { IsShowing: true } navigationDialog)
        {
            navigationDialog.Cancel();
            return;
        }
        if (heartbeatTestActive)
        {
            StopHeartbeatTest("Processing heartbeat test stopped.");
            return;
        }
        if (aiWorkActive)
        {
            CancelOfflineAiWork();
            return;
        }
        if (workspacePanel.Visibility == ViewStates.Visible)
        {
            InvalidateWorkspaceNavigation(dismissDialog: true);
            workspaceVisible = false;
            workspacePanel.Visibility = ViewStates.Gone;
            startPanel.Visibility = ViewStates.Visible;
            resumeButton.Visibility = document is null ? ViewStates.Gone : ViewStates.Visible;
            SetStatus("Returned to Start. The current workspace is retained; select a photo to replace it.");
            return;
        }
        Finish();
    }

    private void RegisterBackCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        backInvokedCallback = new BackInvokedCallback(HandleBack);
        OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
            Android.Window.IOnBackInvokedDispatcher.PriorityDefault, backInvokedCallback);
    }

    private void UnregisterBackCallback()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || backInvokedCallback is null) return;
        OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(backInvokedCallback);
        backInvokedCallback.Dispose();
        backInvokedCallback = null;
    }

    protected override void OnDestroy()
    {
        UnregisterBackCallback();
        InvalidateWorkspaceNavigation(dismissDialog: true);
        Interlocked.Increment(ref aiOperationGeneration);
        activityCancellation?.Cancel();
        importCancellation?.Cancel();
        restoreCancellation?.Cancel();
        aiCancellation?.Cancel();
        renderCancellation?.Cancel();
        StopHeartbeatTest(statusMessage: null);
        heartbeat.Stop();
        activeDialog?.Cancel();
        activeDialog?.Dismiss();
        activeDialog = null;
        Task[] pending;
        lock (taskSynchronization) pending = activeTasks.ToArray();
        _ = DisposeAfterJobsAsync(pending);
        base.OnDestroy();
    }

    private sealed class BackInvokedCallback(Action action) : Java.Lang.Object,
        Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => action();
    }

    private sealed class UiRunnable(Action action) : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run() => action();
    }

    private async Task DisposeAfterJobsAsync(Task[] pending)
    {
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("TATAPP", $"Background cleanup observed a canceled or failed task: {exception.Message}");
        }
        activityCancellation?.Dispose();
        importCancellation?.Dispose();
        restoreCancellation?.Dispose();
        aiCancellation?.Dispose();
        renderCancellation?.Dispose();
        displayedBitmap?.Dispose();
        heartbeat.Dispose();
        offlineAi.Dispose();
    }

    private void DeletePendingCamera()
    {
        if (pendingCameraPath is not null && File.Exists(pendingCameraPath))
        {
            try { File.Delete(pendingCameraPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Java.Lang.SecurityException) { }
        }
        pendingCameraPath = null;
        pendingCameraUri = null;
    }

    private static void TryDeletePrivateImport(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (Java.Lang.SecurityException) { }
    }

    private void SweepAbandonedPrivateInputs(IEnumerable<string?> protectedPaths)
    {
        var privateRoot = FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(privateRoot)) return;
        var protectedSet = protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => System.IO.Path.GetFullPath(path!))
            .ToHashSet(StringComparer.Ordinal);
        var directories = new List<string>
        {
            System.IO.Path.Combine(privateRoot, "imports"),
            System.IO.Path.Combine(privateRoot, "captures"),
        };
        if (!string.IsNullOrWhiteSpace(CacheDir?.AbsolutePath))
            directories.Add(System.IO.Path.Combine(CacheDir.AbsolutePath, "export-snapshots"));
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    var absolute = System.IO.Path.GetFullPath(path);
                    if (!protectedSet.Contains(absolute)) TryDeletePrivateImport(absolute);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Java.Lang.SecurityException) { }
        }
    }

    private void TryDeleteAnatomicalSnapshot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(CacheDir?.AbsolutePath)) return;
        try
        {
            var root = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(CacheDir.AbsolutePath, "export-snapshots")) +
                       System.IO.Path.DirectorySeparatorChar;
            var absolute = System.IO.Path.GetFullPath(path);
            if (absolute.StartsWith(root, StringComparison.Ordinal)) TryDeletePrivateImport(absolute);
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
    }

    private async Task<string> SaveAnatomicalSnapshotAsync(Bitmap bitmap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var root = System.IO.Path.Combine(CacheDir?.AbsolutePath ??
                                          throw new IOException("App-private cache storage is unavailable."),
            "export-snapshots");
        Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, $"anatomy-{Guid.NewGuid():N}.bgra");
        try
        {
            await Task.Run(async () =>
            {
                var frame = BitmapToFrame(bitmap);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var header = new byte[16];
                BitConverter.TryWriteBytes(header.AsSpan(0, 4), 0x54415431);
                BitConverter.TryWriteBytes(header.AsSpan(4, 4), frame.Width);
                BitConverter.TryWriteBytes(header.AsSpan(8, 4), frame.Height);
                BitConverter.TryWriteBytes(header.AsSpan(12, 4), frame.Pixels.Length);
                await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(frame.Pixels, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch
        {
            TryDeleteAnatomicalSnapshot(path);
            throw;
        }
    }

    private async Task<ImageFrame> LoadAnatomicalSnapshotAsync(string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("The captured anatomical export is unavailable.");
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(CacheDir?.AbsolutePath ??
                                                                     throw new IOException("App-private cache storage is unavailable."),
            "export-snapshots")) + System.IO.Path.DirectorySeparatorChar;
        var absolute = System.IO.Path.GetFullPath(path);
        if (!absolute.StartsWith(root, StringComparison.Ordinal) || !File.Exists(absolute))
            throw new InvalidDataException("The captured anatomical export path is invalid or missing.");
        await using var input = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[16];
        await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var magic = BitConverter.ToInt32(header, 0);
        var width = BitConverter.ToInt32(header, 4);
        var height = BitConverter.ToInt32(header, 8);
        var length = BitConverter.ToInt32(header, 12);
        var expected = checked(width * height * 4);
        if (magic != 0x54415431 || width <= 0 || height <= 0 || length != expected ||
            expected > 64 * 1024 * 1024 || input.Length != 16L + expected)
            throw new InvalidDataException("The captured anatomical export is corrupt or exceeds its safety limit.");
        var pixels = GC.AllocateUninitializedArray<byte>(expected);
        await input.ReadExactlyAsync(pixels, cancellationToken).ConfigureAwait(false);
        return new ImageFrame(width, height, pixels);
    }

    private static ImageFrame BitmapToFrame(Bitmap bitmap)
    {
        var colors = new int[checked(bitmap.Width * bitmap.Height)];
        bitmap.GetPixels(colors, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        var pixels = new byte[checked(colors.Length * 4)];
        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            var offset = index * 4;
            pixels[offset] = (byte)Color.GetBlueComponent(color);
            pixels[offset + 1] = (byte)Color.GetGreenComponent(color);
            pixels[offset + 2] = (byte)Color.GetRedComponent(color);
            pixels[offset + 3] = (byte)Color.GetAlphaComponent(color);
        }
        return new ImageFrame(bitmap.Width, bitmap.Height, pixels);
    }

    private AndroidRenderedStageImage EncodeRenderedStage(ImageFrame frame, int maximumDimension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = imageService.ToBitmap(frame);
        return EncodeBitmap(bitmap, maximumDimension, cancellationToken);
    }

    private static AndroidRenderedStageImage EncodeBitmap(Bitmap bitmap, int maximumDimension,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDimension);
        cancellationToken.ThrowIfCancellationRequested();
        var largest = Math.Max(bitmap.Width, bitmap.Height);
        var ratio = Math.Min(1d, maximumDimension / (double)largest);
        var width = Math.Max(1, (int)Math.Floor(bitmap.Width * ratio));
        var height = Math.Max(1, (int)Math.Floor(bitmap.Height * ratio));
        using var scaled = ratio < 1
            ? Bitmap.CreateScaledBitmap(bitmap, width, height, true)
              ?? throw new InvalidDataException("Android could not create the bounded AI input image.")
            : bitmap.Copy(Bitmap.Config.Argb8888!, false)
              ?? throw new InvalidDataException("Android could not copy the AI input image.");
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        if (!scaled.Compress(Bitmap.CompressFormat.Png!, 100, output))
            throw new IOException("Android could not encode the offline AI input image.");
        return new AndroidRenderedStageImage(width, height, output.ToArray());
    }

    private TextView Heading(string text, float sp, bool top)
    {
        var view = Label(text);
        view.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
        view.SetTypeface(view.Typeface, Android.Graphics.TypefaceStyle.Bold);
        if (OperatingSystem.IsAndroidVersionAtLeast(28)) MarkAsHeading(view);
        view.SetPadding(0, top ? 0 : Dp(12), 0, Dp(4));
        return view;
    }

    private void UpdateBodySizeSemantics(double centimeters)
    {
        var description = $"Body size, {AnatomicalDefaults.DescribeHeight(centimeters)}. Range 140 to 200 centimeters.";
        bodySizeSlider.ContentDescription = description;
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) SetStateDescription(bodySizeSlider,
            $"{Math.Round(centimeters):0} centimeters");
    }

    private void UpdateSkinToneSemantics(double value)
    {
        var tone = AnatomicalDefaults.SkinToneFromSlider(value).Description;
        skinToneSlider.ContentDescription =
            $"Skin tone and complexion, {tone}. Adjustable from lighter to deeper skin tone.";
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) SetStateDescription(skinToneSlider, tone);
    }

    [SupportedOSPlatform("android28.0")]
    private static void MarkAsHeading(View view) => view.AccessibilityHeading = true;

    [SupportedOSPlatform("android30.0")]
    private static void SetStateDescription(View view, string description) => view.StateDescription = description;

    private TextView Label(string text) => new(this)
    {
        Text = text,
        TextSize = 16,
        ImportantForAccessibility = ImportantForAccessibility.Yes,
    };

    private Button ActionButton(string text, string description)
    {
        var button = new Button(this)
        {
            Text = text,
            ContentDescription = text + ". " + description,
        };
        button.SetMinimumHeight(Dp(48));
        button.SetMinimumWidth(Dp(48));
        button.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        return button;
    }

    private LinearLayout Stack(bool vertical) => new(this)
    {
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
        ImportantForAccessibility = ImportantForAccessibility.No,
    };

    private LinearLayout.LayoutParams WeightedWrap(Orientation orientation) => orientation == Orientation.Horizontal
        ? new LinearLayout.LayoutParams(0, Wrap, 1) { MarginEnd = Dp(4) }
        : new LinearLayout.LayoutParams(Match, Wrap) { BottomMargin = Dp(4) };

    private static LinearLayout.LayoutParams MatchWrap() => new(Match, Wrap);
    private const int Match = ViewGroup.LayoutParams.MatchParent;
    private const int Wrap = ViewGroup.LayoutParams.WrapContent;
    private int Dp(float value) => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1));
    private static int StageIndex(TattooStage stage)
    {
        for (var index = 0; index < TattooStageCatalog.All.Count; index++)
            if (TattooStageCatalog.All[index].Kind == stage.Kind) return index + 1;
        return 1;
    }

    private static string FormatBytes(long value) => value >= 1_073_741_824
        ? $"{value / 1_073_741_824d:0.0} GB"
        : $"{value / 1_048_576d:0} MB";

    private sealed class DialogCancelListener(Action canceled) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => canceled();
    }

    private sealed class DialogDismissListener(Action dismissed) : Java.Lang.Object,
        IDialogInterfaceOnDismissListener
    {
        public void OnDismiss(IDialogInterface? dialog) => dismissed();
    }

    private sealed record PendingWorkspaceNavigation(long Generation, View Invoker, int? Selection);

    private sealed class CoalescingModelInstallProgress(Action<ModelInstallProgress> callback)
        : IProgress<ModelInstallProgress>
    {
        private readonly object synchronization = new();
        private ModelInstallPhase? lastPhase;
        private int lastPercentage = -1;
        private long lastReportTimestamp;

        public void Report(ModelInstallProgress value)
        {
            var percentage = value.TotalBytes <= 0 ? 0 :
                (int)Math.Clamp(value.CompletedBytes * 100 / value.TotalBytes, 0, 100);
            var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (synchronization)
            {
                var phaseChanged = lastPhase != value.Phase;
                var enoughTimeElapsed = lastReportTimestamp == 0 ||
                    System.Diagnostics.Stopwatch.GetElapsedTime(lastReportTimestamp, timestamp) >=
                    TimeSpan.FromMilliseconds(750);
                if (!phaseChanged && percentage != 100 &&
                    (percentage < lastPercentage + 5 || !enoughTimeElapsed)) return;
                lastPhase = value.Phase;
                lastPercentage = percentage;
                lastReportTimestamp = timestamp;
            }
            callback(value);
        }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class RootInsetsListener(int left, int top, int right, int bottom)
        : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            int insetLeft;
            int insetTop;
            int insetRight;
            int insetBottom;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                insetLeft = bars.Left;
                insetTop = bars.Top;
                insetRight = bars.Right;
                insetBottom = bars.Bottom;
            }
            else
            {
#pragma warning disable CS0618
                insetLeft = insets.SystemWindowInsetLeft;
                insetTop = insets.SystemWindowInsetTop;
                insetRight = insets.SystemWindowInsetRight;
                insetBottom = insets.SystemWindowInsetBottom;
#pragma warning restore CS0618
            }
            view.SetPadding(left + insetLeft, top + insetTop, right + insetRight, bottom + insetBottom);
            return insets;
        }
    }
}
