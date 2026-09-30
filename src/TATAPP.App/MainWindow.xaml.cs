using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TATAPP.App.Accessibility;
using TATAPP.App.Anatomy;
using TATAPP.App.Camera;
using TATAPP.App.Imaging;
using TATAPP.App.OfflineAI;
using TATAPP.Core;

namespace TATAPP.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private const string BlackWidowTattooFacebookUrl = "https://www.facebook.com/grayscaleconsultants";
    private readonly WindowsCameraCaptureCoordinator camera = new();
    private readonly Dictionary<TattooStageKind, string> offlineAiDescriptions = [];
    private readonly AnatomyViewportController anatomy;
    private PhotoDocument? document;
    private CancellationTokenSource? previewCancellation;
    private CancellationTokenSource? offlineAiCancellation;
    private TattooStageKind? announcedStage;
    private TattooStageKind? renderedStage;
    private bool suppressOfflineAiCheckEvent;
    private bool synchronizingAnatomyControls;
    private bool isBusy;

    public MainWindow()
    {
        InitializeComponent();
        anatomy = new AnatomyViewportController(AnatomyViewport, BodyRegionTapped);
        anatomy.PlacementFocusCompleted += () =>
        {
            if (!IsLoaded) return;
            UpdateAnatomyAccessibility(announce: false);
            RefreshSettledPlacementStatus();
        };
        BodyRegionComboBox.ItemsSource = BodyRegionCatalog.All;
        BodyRegionComboBox.SelectedItem = BodyRegionCatalog.Get(AnatomicalDefaults.Region);
        anatomy.SetSex(AnatomicalDefaults.Sex);
        anatomy.SetHeight(AnatomicalDefaults.HeightCentimeters);
        anatomy.SetSkinTone(AnatomicalDefaults.SkinToneValue);
        anatomy.SelectRegion(AnatomicalDefaults.Region, orientToRegion: true);
        UpdateAnatomyAccessibility(announce: false);
        UpdateStageInformation(0, announce: false);
    }

    private async void SelectPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a photo for TATAPP",
            Filter = PhotoFileFormats.OpenDialogFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) await LoadPhotoAsync(dialog.FileName);
    }

    private void TakePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        var result = camera.BeginCapture();
        SetStatus(result.Message);
        if (!result.Succeeded)
            MessageBox.Show(this, result.Message, "Camera unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BlackWidowTattooButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BlackWidowTattooFacebookUrl) { UseShellExecute = true });
            SetStatus("Opening the BLACK WIDOW TATTOO Facebook page in the default browser.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ShowError("The BLACK WIDOW TATTOO Facebook page could not be opened.", exception);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (document is null || isBusy) return;
        var stage = TattooStageCatalog.FromSlider(DevelopmentSlider.Value);
        var format = document.Format;
        var dialog = new SaveFileDialog
        {
            Title = "Save the current TATAPP image",
            Filter = format.DialogFilter,
            DefaultExt = format.PreferredExtension,
            AddExtension = true,
            FileName = $"{Path.GetFileNameWithoutExtension(document.SourcePath)}-tattoo-{Slug(stage.Name)}{format.PreferredExtension}",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Rendering the full-resolution image for saving.");
        try
        {
            var destination = EnsureOriginalFormatExtension(dialog.FileName, format);
            if (!stage.IsAnatomicalPlacement)
                PhotoCodec.EnsureProcessingHeadroom(document.FullResolution);
            var fullResolution = stage.IsAnatomicalPlacement
                ? CaptureAnatomicalPreview()
                : await Task.Run(() => TattooStageCatalog.RenderDesign(document.FullResolution, stage));
            await Task.Run(() => PhotoCodec.Save(fullResolution, destination, format.Format));
            var placement = stage.IsAnatomicalPlacement
                ? $" for {BodyRegionCatalog.Get(anatomy.SelectedRegion).AccessibleDescription}"
                : string.Empty;
            SetStatus($"Saved {stage.Name}{placement} as {format.DisplayName}: {destination}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                                             or NotSupportedException or ArgumentException or OutOfMemoryException)
        {
            ShowError("The image could not be saved.", exception);
        }
        finally { SetBusy(false); }
    }

    private async void DevelopmentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SliderValueText.Text = $"{e.NewValue:0} percent";
        UpdateStageInformation(e.NewValue, announce: document is not null);
        if (document is null) return;

        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new CancellationTokenSource();
        var cancellationToken = previewCancellation.Token;
        try
        {
            await Task.Delay(65, cancellationToken);
            var source = document.Preview;
            var stage = TattooStageCatalog.FromSlider(e.NewValue);
            var rendered = await Task.Run(
                () => TattooStageCatalog.RenderDesign(source, stage, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PreviewImage.Source = PhotoCodec.ToBitmapSource(rendered);
            anatomy.SetTattoo(rendered, stage.IsAnatomicalPlacement);
            if (stage.IsAnatomicalPlacement && renderedStage != stage.Kind)
                anatomy.AnimatePlacementFocus();
            else if (!stage.IsAnatomicalPlacement && renderedStage is not null &&
                     TattooStageCatalog.All.First(item => item.Kind == renderedStage).IsAnatomicalPlacement)
                anatomy.ShowOverview();
            renderedStage = stage.Kind;
            UpdatePreviewAccessibility(stage, e.NewValue);
            if (stage.IsAnatomicalPlacement) UpdateAnatomyAccessibility(announce: false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadPhotoAsync(string path)
    {
        previewCancellation?.Cancel();
        offlineAiCancellation?.Cancel();
        offlineAiDescriptions.Clear();
        SetBusy(true, "Loading and preparing the selected photo.");
        var prepareOfflineDescriptions = false;
        try
        {
            var loaded = await Task.Run(() => PhotoCodec.Load(path));
            document = loaded;
            SourceNameText.Text = $"{loaded.DisplayName} — {loaded.FullResolution.Width} by {loaded.FullResolution.Height} pixels";
            EmptyStateText.Visibility = Visibility.Collapsed;
            DevelopmentSlider.IsEnabled = true;
            DevelopmentSlider.Value = 0;
            PreviewImage.Source = PhotoCodec.ToBitmapSource(loaded.Preview);
            anatomy.SetTattoo(loaded.Preview, visible: false);
            anatomy.ShowOverview();
            renderedStage = TattooStageKind.Original;
            AutomationProperties.SetName(PreviewImage,
                $"Preview of {loaded.DisplayName}. Original image with original color, texture, and detail.");
            UpdateStageInformation(0, announce: false);
            SetStatus($"Loaded {loaded.DisplayName}. Original format: {loaded.Format.DisplayName}. Slider is at Original image.");
            DevelopmentSlider.Focus();
            prepareOfflineDescriptions = OfflineAIDescribeCheckBox.IsChecked == true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                                             or NotSupportedException or ArgumentException or OutOfMemoryException)
        {
            ShowError("The photo could not be opened.", exception);
        }
        finally { SetBusy(false); }

        if (prepareOfflineDescriptions && document is not null)
            await PrepareOfflineDescriptionsAsync(document);
    }

    private void UpdateStageInformation(double value, bool announce)
    {
        var stage = TattooStageCatalog.FromSlider(value);
        StageHeadingText.Text = stage.Name;
        StageDescriptionText.Text = stage.IsAnatomicalPlacement
            ? AnatomicalPlacementDescription(stage)
            : stage.Description;
        UpdatePreviewAccessibility(stage, value);
        if (!announce || announcedStage == stage.Kind) return;
        announcedStage = stage.Kind;
        // Offline AI slider changes announce only the cached visual description.
        // Stage names, slider values, and anatomical controls are already exposed
        // through their own UI Automation properties; repeating them here makes
        // JAWS output long and obscures the visual information the model adds.
        if (OfflineAIDescribeCheckBox.IsChecked == true &&
            offlineAiDescriptions.TryGetValue(stage.Kind, out var offlineDescription))
        {
            ScreenReaderAnnouncer.Announce(OfflineAiDescriptionText,
                offlineDescription, "Offline AI visual description");
            return;
        }
        if (stage.IsAnatomicalPlacement)
        {
            var description = AnatomicalPlacementDescription(stage);
            SetStatus($"Current visual stage: {stage.Name}. {description}");
            return;
        }
        SetStatus($"Current visual stage: {stage.Name}. {stage.Description}");
    }

    private void UpdatePreviewAccessibility(TattooStage stage, double sliderValue)
    {
        if (document is null) return;
        if (stage.IsAnatomicalPlacement)
        {
            var anatomyDescription = AnatomicalPlacementDescription(stage);
            AutomationProperties.SetName(PreviewImage,
                $"Source design for anatomical placement. {document.DisplayName}.");
            AutomationProperties.SetItemStatus(PreviewImage, $"Slider {sliderValue:0} percent");
            AutomationProperties.SetName(AnatomyViewport, anatomyDescription);
            AutomationProperties.SetItemStatus(AnatomyViewport, "Tattoo placement preview active");
            return;
        }
        var description = OfflineAIDescribeCheckBox.IsChecked == true &&
                          offlineAiDescriptions.TryGetValue(stage.Kind, out var vividDescription)
            ? vividDescription
            : stage.Description;
        AutomationProperties.SetName(PreviewImage,
            $"Preview of {document.DisplayName}. {stage.Name}. {description}");
        AutomationProperties.SetItemStatus(PreviewImage, $"Slider {sliderValue:0} percent");
    }

    private void BodySexRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (anatomy is null || synchronizingAnatomyControls) return;
        var selectedSex = FemaleBodyRadioButton.IsChecked == true ? AnatomicalSex.Female : AnatomicalSex.Male;
        anatomy.SetSex(selectedSex);
        if (document is not null)
        {
            var stage = TattooStageCatalog.FromSlider(DevelopmentSlider.Value);
            anatomy.SetTattoo(TattooStageCatalog.RenderDesign(document.Preview, stage),
                stage.IsAnatomicalPlacement);
            if (stage.IsAnatomicalPlacement) anatomy.AnimatePlacementFocus();
        }
        UpdateAnatomyAccessibility(announce: IsLoaded);
    }

    private void BodyRegionComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (anatomy is null || synchronizingAnatomyControls ||
            BodyRegionComboBox.SelectedItem is not BodyRegionDefinition region)
            return;
        anatomy.SelectRegion(region.Kind, orientToRegion: true);
        if (document is not null && TattooStageCatalog.FromSlider(DevelopmentSlider.Value).IsAnatomicalPlacement)
            anatomy.AnimatePlacementFocus();
        UpdateAnatomyAccessibility(announce: IsLoaded);
    }

    private void BodyRegionTapped(BodyRegionKind regionKind)
    {
        synchronizingAnatomyControls = true;
        BodyRegionComboBox.SelectedItem = BodyRegionCatalog.Get(regionKind);
        synchronizingAnatomyControls = false;
        if (document is not null && TattooStageCatalog.FromSlider(DevelopmentSlider.Value).IsAnatomicalPlacement)
            anatomy.AnimatePlacementFocus();
        UpdateAnatomyAccessibility(announce: true);
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e)
    {
        anatomy.RotateBy(-15);
        UpdateAnatomyAccessibility(announce: true);
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e)
    {
        anatomy.RotateBy(15);
        UpdateAnatomyAccessibility(announce: true);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        anatomy.ZoomBy(-3);
        UpdateAnatomyAccessibility(announce: true);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        anatomy.ZoomBy(3);
        UpdateAnatomyAccessibility(announce: true);
    }

    private void BodySizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BodySizeValueText is null) return;
        var description = AnatomicalDefaults.DescribeHeight(e.NewValue);
        BodySizeValueText.Text = description;
        AutomationProperties.SetItemStatus(BodySizeSlider, description);
        if (anatomy is null) return;
        anatomy.SetHeight(e.NewValue);
        if (document is not null && TattooStageCatalog.FromSlider(DevelopmentSlider.Value).IsAnatomicalPlacement)
            anatomy.FocusSelectedRegionInstant();
        if (IsLoaded) UpdateAnatomyAccessibility(announce: true);
    }

    private void SkinToneSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SkinToneValueText is null) return;
        var tone = AnatomicalDefaults.SkinToneFromSlider(e.NewValue);
        SkinToneValueText.Text = tone.Description;
        AutomationProperties.SetItemStatus(SkinToneSlider, tone.Description);
        if (anatomy is null) return;
        anatomy.SetSkinTone(e.NewValue);
        if (IsLoaded) UpdateAnatomyAccessibility(announce: true);
    }

    private void UpdateAnatomyAccessibility(bool announce)
    {
        var description = AnatomicalPlacementDescription();
        AutomationProperties.SetName(AnatomyViewport, description);
        AutomationProperties.SetItemStatus(BodyRegionComboBox,
            BodyRegionCatalog.Get(anatomy.SelectedRegion).DisplayName);
        if (TattooStageCatalog.FromSlider(DevelopmentSlider.Value).IsAnatomicalPlacement)
            StageDescriptionText.Text = description;
        if (announce)
        {
            StatusText.Text = $"Anatomical selection updated. {description}";
            AutomationProperties.SetName(StatusText, $"Application status. {StatusText.Text}");
            ScreenReaderAnnouncer.Announce(AnatomySelectionText, description, "Anatomical selection status");
        }
    }

    private void RefreshSettledPlacementStatus()
    {
        if (document is null || OfflineAIDescribeCheckBox.IsChecked == true) return;
        var stage = TattooStageCatalog.FromSlider(DevelopmentSlider.Value);
        if (!stage.IsAnatomicalPlacement) return;
        var message = $"Current visual stage: {stage.Name}. {AnatomicalPlacementDescription(stage)}";
        StatusText.Text = message;
        AutomationProperties.SetName(StatusText, $"Application status. {message}");
    }

    private string AnatomicalPlacementDescription(TattooStage? stage = null)
    {
        stage ??= TattooStageCatalog.FromSlider(DevelopmentSlider.Value);
        var sex = anatomy.Sex == AnatomicalSex.Male ? "Male" : "Female";
        var region = BodyRegionCatalog.Get(anatomy.SelectedRegion);
        var height = AnatomicalDefaults.DescribeHeight(BodySizeSlider.Value);
        var tone = AnatomicalDefaults.SkinToneFromSlider(SkinToneSlider.Value).Description;
        var placement = document is null
            ? "Load an isolated tattoo design, then move beyond Fine outline to the anatomical placement stages."
            : stage.IsAnatomicalPlacement
                ? OfflineAIDescribeCheckBox.IsChecked == true &&
                  offlineAiDescriptions.TryGetValue(stage.Kind, out var vividDescription)
                    ? $"{stage.Name}. {vividDescription}"
                    : $"{stage.Name}. {stage.Description}"
                : "Move beyond Fine outline to preview the reverse development sequence on this surface.";
        return $"Interactive {sex.ToLowerInvariant()} anatomical model, {height}, {tone} complexion. " +
               $"Selected region: {region.AccessibleDescription}. Rotation {anatomy.RotationDegrees:0} degrees. " +
               $"Zoom {anatomy.ZoomPercent:0} percent. {placement} " +
               (stage.IsAnatomicalPlacement ? anatomy.PlacementFramingDescription : string.Empty);
    }

    private ImageFrame CaptureAnatomicalPreview()
    {
        AnatomyViewport.UpdateLayout();
        var width = Math.Max(1, (int)Math.Round(AnatomyViewport.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(AnatomyViewport.ActualHeight));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(AnatomyViewport);
        target.Freeze();
        return PhotoCodec.ToImageFrame(target);
    }

    private async void OfflineAIDescribeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (suppressOfflineAiCheckEvent) return;
        OfflineAiDescriptionPanel.Visibility = Visibility.Visible;
        if (document is null)
        {
            OfflineAiProfileText.Text = "The local model will be selected after a photo is loaded.";
            ScreenReaderAnnouncer.Announce(OfflineAiDescriptionText,
                "Offline AI Describe is on. Take or select a photo to prepare descriptions for all visual stages.",
                "Offline AI visual description");
            SetStatus("Offline AI Describe is on. Waiting for a photo.");
            return;
        }
        await PrepareOfflineDescriptionsAsync(document);
    }

    private void OfflineAIDescribeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (suppressOfflineAiCheckEvent) return;
        offlineAiCancellation?.Cancel();
        offlineAiDescriptions.Clear();
        OfflineAiDescriptionPanel.Visibility = Visibility.Collapsed;
        SetStatus("Offline AI Describe is off. Standard stage announcements remain available.");
    }

    private async Task PrepareOfflineDescriptionsAsync(PhotoDocument sourceDocument)
    {
        offlineAiCancellation?.Cancel();
        offlineAiCancellation?.Dispose();
        offlineAiCancellation = new CancellationTokenSource();
        var cancellationToken = offlineAiCancellation.Token;
        offlineAiDescriptions.Clear();
        OfflineAiDescriptionPanel.Visibility = Visibility.Visible;
        SetBusy(true, "Evaluating this computer for the safest local Qwen vision model.");
        SetProgress(indeterminate: true, 0, "Evaluating hardware");

        try
        {
            var hardware = await Task.Run(OfflineAiHardwareDetector.Detect, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var profile = OfflineAiProfileSelector.Select(hardware);
            OfflineAiProfileText.Text =
                $"Auto-selected {profile.DisplayName}: {profile.Model}. {hardware.AccessibleSummary}. Processing stays on this computer.";

            if (!OfflineAiProfileSelector.HasSafeHeadroom(profile, hardware))
                OfflineAiProfileSelector.EnsureSafeHeadroom(profile, hardware, "starting local vision processing");

            using var client = new OllamaVisionClient();
            var status = await client.GetStatusAsync(cancellationToken);
            if (!status.IsReachable)
            {
                var choice = MessageBox.Show(this,
                    status.Error + "\n\nWould you like to open Ollama's official Windows download page? " +
                    "After installing or starting Ollama, return to TATAPP and check Offline AI Describe again.",
                    "Offline AI runtime required", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo("https://ollama.com/download/windows") { UseShellExecute = true });
                DisableOfflineAi(status.Error);
                return;
            }
            if (!status.IsVersionSupported)
                throw new InvalidOperationException(
                    $"Ollama {OllamaVisionClient.MinimumVersion} or newer is required. This computer reports version {status.Version}.");

            if (!status.HasModel(profile.Model))
            {
                var install = MessageBox.Show(this,
                    $"TATAPP selected {profile.Model} after evaluating this computer. The model is not installed. " +
                    "Download it now through the local Ollama service? Internet access is needed for this one-time, multi-gigabyte download. " +
                    "No photograph is uploaded.",
                    "Install local Qwen vision model", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (install != MessageBoxResult.Yes)
                    throw new OperationCanceledException("Model installation was declined.", cancellationToken);

                var lastAnnouncedPercentage = -10;
                var modelProgress = new Progress<ModelPullProgress>(update =>
                {
                    SetProgress(update.Percentage < 0, Math.Max(0, update.Percentage),
                        update.Percentage < 0 ? "Preparing model download" : $"Model download {update.Percentage} percent");
                    OfflineAiDescriptionText.Text = update.Percentage < 0
                        ? "Preparing the local Qwen vision model download."
                        : $"Local Qwen vision model download: {update.Percentage} percent.";
                    if (update.Percentage == 100 || update.Percentage >= lastAnnouncedPercentage + 10)
                    {
                        lastAnnouncedPercentage = update.Percentage;
                        SetStatus(update.Percentage < 0
                            ? "Preparing the local model download."
                            : $"Local model download {update.Percentage} percent complete.");
                    }
                });
                await client.PullModelAsync(profile.Model, modelProgress, cancellationToken);
                status = await client.GetStatusAsync(cancellationToken);
                if (!status.HasModel(profile.Model))
                    throw new InvalidOperationException(
                        $"Ollama finished the download, but {profile.Model} is not available.");
            }

            var stageCount = TattooStageCatalog.All.Count;
            SetStatus($"Preparing {stageCount} offline descriptions with {profile.Model}. The slider will unlock when all stages are ready.");
            ScreenReaderAnnouncer.Announce(OfflineAiDescriptionText,
                $"Processing the loaded photo locally. {stageCount} visual stage descriptions, including the anatomical placement sequence, are being prepared. Listen for the heartbeat; the slider will unlock when processing is complete.",
                "Offline AI visual description");
            SetProgress(indeterminate: false, 0, "Offline descriptions 0 percent");
            using var heartbeat = ProcessingHeartbeat.Start();
            var progress = new Progress<OfflineAiPreloadProgress>(update =>
            {
                SetProgress(indeterminate: false, update.Percentage,
                    $"Offline descriptions {update.Percentage} percent");
                OfflineAiDescriptionText.Text =
                    $"Prepared {update.Completed} of {update.Total} stages. Latest: {update.Stage.Name}.";
                SetStatus($"Offline AI Describe prepared {update.Completed} of {update.Total}: {update.Stage.Name}.");
            });
            var descriptions = await OfflineAiDescriptionPreloader.PreloadAsync(
                sourceDocument.Preview, profile, client, progress,
                (stage, token) => RenderOfflineAiStageAsync(sourceDocument.Preview, stage, token),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(sourceDocument, document)) return;

            foreach (var pair in descriptions) offlineAiDescriptions.Add(pair.Key, pair.Value);
            RestoreCurrentStagePreview(sourceDocument);
            announcedStage = null;
            SetProgress(indeterminate: false, 100, "Offline descriptions 100 percent");
            SetStatus($"Offline AI Describe is ready. All {stageCount} stage descriptions are cached; slider changes are now immediate.");
            UpdateStageInformation(DevelopmentSlider.Value, announce: true);
        }
        catch (OperationCanceledException)
        {
            if (OfflineAIDescribeCheckBox.IsChecked == true)
                DisableOfflineAi("Offline AI Describe was canceled. Standard stage announcements remain available.");
        }
        catch (Exception exception) when (IsRecoverableOfflineAiException(exception))
        {
            DisableOfflineAi($"Offline AI Describe could not start. {exception.Message}");
            MessageBox.Show(this, exception.Message, "Offline AI Describe",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            if (document is not null) RestoreCurrentStagePreview(document);
            SetBusy(false);
        }
    }

    private async Task<ImageFrame> RenderOfflineAiStageAsync(ImageFrame source, TattooStage stage,
        CancellationToken cancellationToken)
    {
        var rendered = await Task.Run(
            () => TattooStageCatalog.RenderDesign(source, stage, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (!stage.IsAnatomicalPlacement) return rendered;

        var operation = Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            anatomy.SetTattoo(rendered, visible: true);
            anatomy.FocusSelectedRegionInstant();
            return CaptureAnatomicalPreview();
        });
        return await operation.Task.ConfigureAwait(false);
    }

    private void RestoreCurrentStagePreview(PhotoDocument sourceDocument)
    {
        if (!ReferenceEquals(sourceDocument, document)) return;
        var stage = TattooStageCatalog.FromSlider(DevelopmentSlider.Value);
        var rendered = TattooStageCatalog.RenderDesign(sourceDocument.Preview, stage);
        PreviewImage.Source = PhotoCodec.ToBitmapSource(rendered);
        anatomy.SetTattoo(rendered, stage.IsAnatomicalPlacement);
        if (stage.IsAnatomicalPlacement)
            anatomy.FocusSelectedRegionInstant();
        else
            anatomy.ShowOverview();
        renderedStage = stage.Kind;
    }

    private void DisableOfflineAi(string message)
    {
        offlineAiDescriptions.Clear();
        suppressOfflineAiCheckEvent = true;
        OfflineAIDescribeCheckBox.IsChecked = false;
        suppressOfflineAiCheckEvent = false;
        OfflineAiDescriptionPanel.Visibility = Visibility.Collapsed;
        SetStatus(message);
    }

    private void SetProgress(bool indeterminate, int percentage, string accessibleStatus)
    {
        BusyProgress.IsIndeterminate = indeterminate;
        if (!indeterminate) BusyProgress.Value = Math.Clamp(percentage, 0, 100);
        AutomationProperties.SetName(BusyProgress, "Offline AI preparation progress");
        AutomationProperties.SetItemStatus(BusyProgress, accessibleStatus);
    }

    private static bool IsRecoverableOfflineAiException(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or IOException or HttpRequestException
            or UnauthorizedAccessException or NotSupportedException or ArgumentException or OutOfMemoryException
            or Win32Exception;

    private async void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (!camera.IsWaitingForCapture || !camera.WindowWasDeactivated) return;
        await Task.Delay(600);
        var capturedPath = camera.CompleteCapture();
        if (capturedPath is not null)
            await LoadPhotoAsync(capturedPath);
        else
            SetStatus("No new Camera Roll photo was detected. If the camera saved elsewhere, use Select photo to open it.");
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e) => camera.MarkWindowDeactivated();

    private void MainWindow_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = null;
        offlineAiCancellation?.Cancel();
        offlineAiCancellation?.Dispose();
        offlineAiCancellation = null;
    }

    private void SetBusy(bool value, string? message = null)
    {
        isBusy = value;
        BusyProgress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        TakePhotoButton.IsEnabled = !value;
        SelectPhotoButton.IsEnabled = !value;
        SaveButton.IsEnabled = !value && document is not null;
        DevelopmentSlider.IsEnabled = !value && document is not null;
        OfflineAIDescribeCheckBox.IsEnabled = !value;
        MaleBodyRadioButton.IsEnabled = !value;
        FemaleBodyRadioButton.IsEnabled = !value;
        BodyRegionComboBox.IsEnabled = !value;
        BodySizeSlider.IsEnabled = !value;
        SkinToneSlider.IsEnabled = !value;
        RotateLeftButton.IsEnabled = !value;
        RotateRightButton.IsEnabled = !value;
        ZoomInButton.IsEnabled = !value;
        ZoomOutButton.IsEnabled = !value;
        AnatomyViewport.IsEnabled = !value;
        if (message is not null) SetStatus(message);
    }

    private void SetStatus(string message) =>
        ScreenReaderAnnouncer.Announce(StatusText, message, "Application status");

    private void ShowError(string summary, Exception exception)
    {
        var message = $"{summary} {exception.Message}";
        SetStatus(message);
        MessageBox.Show(this, message, "TATAPP", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string Slug(string value) =>
        string.Join('-', value.ToLowerInvariant().Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries));

    private static string EnsureOriginalFormatExtension(string path, PhotoFileFormatInfo format) =>
        format.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, format.PreferredExtension);
}
