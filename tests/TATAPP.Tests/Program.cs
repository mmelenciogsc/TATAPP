using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using TATAPP.App;
using TATAPP.App.Anatomy;
using TATAPP.App.Imaging;
using TATAPP.App.OfflineAI;
using TATAPP.Core;

namespace TATAPP.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Slider zero preserves every original pixel", OriginalIsExact),
            ("Grayscale and binary stages have correct channel behavior", GrayscaleAndBinaryAreValid),
            ("Outline thickness decreases toward slider maximum", OutlineThicknessDecreases),
            ("Image processing is deterministic", ProcessingIsDeterministic),
            ("Image processing observes cancellation", ProcessingCanBeCancelled),
            ("Stage catalog covers the complete slider", StageCatalogIsComplete),
            ("Original photo formats resolve safely", FormatsResolveSafely),
            ("Same-format encoders preserve image dimensions", EncodersPreserveDimensions),
            ("Oversized images are rejected before unsafe allocation", OversizedImagesAreRejected),
            ("Application starts the accessible main window", ApplicationStartsMainWindow),
            ("Main window constructs with anatomy defaults", MainWindowConstructsWithDefaults),
            ("Primary controls expose screenreader metadata", PrimaryControlsAreAccessible),
            ("Status updates use a polite live region", StatusIsLiveRegion),
            ("Camera workflow has accessible local fallback", CameraWorkflowHasFallback),
            ("Black Widow Tattoo link is accessible and exact", BlackWidowTattooLinkIsAccessible),
            ("Every visual stage has a valid AI representative", StageRepresentativesAreValid),
            ("Anatomical defaults match the Filipino adult brief", AnatomicalDefaultsMatchBrief),
            ("Body region catalog is complete and unambiguous", BodyRegionCatalogIsComplete),
            ("Tattoo texture removes the design sheet background", TattooTextureRemovesBackground),
            ("WPF anatomy adapter preserves shared mesh geometry", WpfAnatomyAdapterPreservesSharedGeometry),
            ("Anatomical visual and dropdown selection synchronize", AnatomicalSelectionSynchronizes),
            ("Anatomical placement can be saved", AnatomicalPlacementCanBeSaved),
            ("Every anatomical region has contextual and detail camera framing", CameraFramingCoversEveryRegion),
            ("Placement stages trigger smooth region-focused camera movement", PlacementStagesAnimateCamera),
            ("Product is named Tattoo Art Prepper", ProductNameIsCorrect),
            ("Hardware tiers select conservatively", HardwareTiersAreConservative),
            ("Offline AI rechecks memory before every model load", OfflineAiRechecksMemory),
            ("Windows hardware inventory is usable", HardwareInventoryIsUsable),
            ("Offline AI prompt is vivid and truth-grounded", OfflineAiPromptIsGrounded),
            ("Offline AI controls expose screenreader metadata", OfflineAiControlsAreAccessible),
            ("Offline AI client stays on loopback and parses responses", OfflineAiClientIsLocal),
            ("Length-limited AI output keeps complete concise sentences", LengthLimitedAiOutputIsSafe),
            ("All stage descriptions preload into one complete cache", AllDescriptionsPreload),
            ("Vision images are bounded PNG data", VisionImagesAreBounded),
            ("Processing heartbeat contains valid wave audio", HeartbeatIsValidWave),
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS: {test.Name}");
            }
            catch (Exception exception)
            {
                failures.Add($"FAIL: {test.Name}: {exception.Message}");
                Console.Error.WriteLine(failures[^1]);
            }
        }
        Console.WriteLine($"TATAPP checks: {tests.Length - failures.Count}/{tests.Length} passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void OriginalIsExact()
    {
        var source = ColorPattern(18, 14);
        var output = TattooImageProcessor.Render(source, 0);
        Assert(!ReferenceEquals(source, output));
        Assert(source.Width == output.Width && source.Height == output.Height);
        Assert(source.Pixels.SequenceEqual(output.Pixels));
    }

    private static void GrayscaleAndBinaryAreValid()
    {
        var source = ColorPattern(32, 24);
        var grayscale = TattooImageProcessor.Render(source, 20);
        Assert(Pixels(grayscale).All(pixel => pixel.Blue == pixel.Green && pixel.Green == pixel.Red && pixel.Alpha == 255));
        var binary = TattooImageProcessor.Render(source, 36);
        Assert(Pixels(binary).All(pixel => pixel.Blue == pixel.Green && pixel.Green == pixel.Red
            && pixel.Blue is 0 or 255 && pixel.Alpha == 255));
    }

    private static void OutlineThicknessDecreases()
    {
        var source = SquarePattern(72, 72);
        var thick = TattooImageProcessor.Render(source, 55);
        var fine = TattooImageProcessor.Render(source, 91);
        var thickMarks = Pixels(thick).Count(pixel => pixel.Blue < 128);
        var fineMarks = Pixels(fine).Count(pixel => pixel.Blue < 128);
        Assert(thickMarks > fineMarks && fineMarks > 0);
    }

    private static void ProcessingIsDeterministic()
    {
        var source = ColorPattern(41, 37);
        var first = TattooImageProcessor.Render(source, 73.5);
        var second = TattooImageProcessor.Render(source, 73.5);
        Assert(first.Pixels.SequenceEqual(second.Pixels));
    }

    private static void ProcessingCanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() =>
            TattooImageProcessor.Render(ColorPattern(100, 100), 50, cancellation.Token));
    }

    private static void StageCatalogIsComplete()
    {
        var expected = new[]
        {
            TattooStageKind.Original, TattooStageKind.ColorFade, TattooStageKind.Grayscale,
            TattooStageKind.Binarized, TattooStageKind.LineArt, TattooStageKind.ThickOutline,
            TattooStageKind.MediumOutline, TattooStageKind.FineOutline,
            TattooStageKind.AnatomicalFineOutline, TattooStageKind.AnatomicalMediumOutline,
            TattooStageKind.AnatomicalThickOutline, TattooStageKind.AnatomicalLineArt,
            TattooStageKind.AnatomicalBinarized, TattooStageKind.AnatomicalGrayscale,
            TattooStageKind.AnatomicalColorFade, TattooStageKind.AnatomicalFullColor,
        };
        var actual = Enumerable.Range(0, 101).Select(value => TattooStageCatalog.FromSlider(value).Kind)
            .Distinct().ToArray();
        Assert(actual.SequenceEqual(expected));
        Assert(TattooStageCatalog.FromSlider(-10).Kind == TattooStageKind.Original);
        Assert(TattooStageCatalog.FromSlider(110).Kind == TattooStageKind.AnatomicalFullColor);
    }

    private static void FormatsResolveSafely()
    {
        Assert(PhotoFileFormats.FromPath("portrait.JPEG").Format == PhotoFileFormat.Jpeg);
        Assert(PhotoFileFormats.FromPath("stencil.png").Format == PhotoFileFormat.Png);
        Assert(PhotoFileFormats.FromPath("scan.tiff").PreferredExtension == ".tif");
        AssertThrows<NotSupportedException>(() => PhotoFileFormats.FromPath("unsafe.exe"));
        Assert(PhotoFileFormats.OpenDialogFilter.Contains("*.jpg", StringComparison.Ordinal));
    }

    private static void EncodersPreserveDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), "TATAPP-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = ColorPattern(19, 13);
            foreach (var info in PhotoFileFormats.Supported)
            {
                var path = Path.Combine(root, "encoded" + info.PreferredExtension);
                PhotoCodec.Save(source, path, info.Format);
                Assert(File.Exists(path) && new FileInfo(path).Length > 0);
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth == 19 && decoder.Frames[0].PixelHeight == 13);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void OversizedImagesAreRejected()
    {
        const ulong gib = 1_073_741_824;
        PhotoCodec.EnsureImageDimensionsAreSafe(6000, 4000, 4 * gib, preparingDerivedImage: true);
        AssertThrows<InvalidDataException>(() =>
            PhotoCodec.EnsureImageDimensionsAreSafe(9000, 9000, 16 * gib, preparingDerivedImage: false));
        AssertThrows<InvalidDataException>(() =>
            PhotoCodec.EnsureImageDimensionsAreSafe(6000, 4000, 2 * gib, preparingDerivedImage: true));
    }

    private static void PrimaryControlsAreAccessible()
    {
        var document = LoadMainWindowXaml();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[]
                 {
                     "TakePhotoButton", "SelectPhotoButton", "SaveButton", "BlackWidowTattooButton",
                     "DevelopmentSlider", "PreviewImage", "AnatomyViewport", "MaleBodyRadioButton",
                     "FemaleBodyRadioButton", "BodyRegionComboBox", "RotateLeftButton", "RotateRightButton",
                     "ZoomInButton", "ZoomOutButton", "BodySizeSlider", "SkinToneSlider",
                 })
        {
            var element = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == name);
            Assert(!string.IsNullOrWhiteSpace((string?)element.Attribute("AutomationProperties.Name")));
            Assert((string?)element.Attribute("AutomationProperties.AutomationId") == name);
            Assert(!string.IsNullOrWhiteSpace((string?)element.Attribute("AutomationProperties.HelpText")));
        }
        var buttons = document.Descendants().Where(item => item.Name.LocalName == "Button").ToArray();
        Assert(buttons.All(button => ((string?)button.Attribute("Content"))?.Contains('_', StringComparison.Ordinal) == true));
        var slider = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "DevelopmentSlider");
        Assert((string?)slider.Attribute("Minimum") == "0" && (string?)slider.Attribute("Maximum") == "100");
    }

    private static void ApplicationStartsMainWindow()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "App.xaml.cs"));
        Assert(source.Contains("MainWindow = window", StringComparison.Ordinal)
            && source.Contains("window.Show()", StringComparison.Ordinal));
    }

    private static void MainWindowConstructsWithDefaults()
    {
        using var window = new MainWindow();
        Assert(window.Title.Contains("Tattoo Art Prepper", StringComparison.Ordinal));
    }

    private static void StatusIsLiveRegion()
    {
        var document = LoadMainWindowXaml();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var status = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "StatusText");
        Assert((string?)status.Attribute("AutomationProperties.LiveSetting") == "Polite");
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "Accessibility", "ScreenReaderAnnouncer.cs"));
        Assert(source.Contains("LiveRegionChanged", StringComparison.Ordinal));
        Assert(source.Contains("AutomationProperties.SetName", StringComparison.Ordinal));
    }

    private static void CameraWorkflowHasFallback()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "Camera",
            "WindowsCameraCaptureCoordinator.cs"));
        Assert(source.Contains("microsoft.windows.camera:", StringComparison.Ordinal)
            && source.Contains("Camera Roll", StringComparison.Ordinal)
            && source.Contains("Select photo", StringComparison.Ordinal));
    }

    private static void BlackWidowTattooLinkIsAccessible()
    {
        var document = LoadMainWindowXaml();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = document.Descendants().Single(item =>
            (string?)item.Attribute(x + "Name") == "BlackWidowTattooButton");
        Assert((string?)button.Attribute("AutomationProperties.Name") == "BLACK WIDOW TATTOO");
        Assert((string?)button.Attribute("AutomationProperties.AutomationId") == "BlackWidowTattooButton");
        Assert(!string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.HelpText")));
        Assert(((string?)button.Attribute("Content"))?.Replace("_", string.Empty, StringComparison.Ordinal) ==
               "BLACK WIDOW TATTOO");

        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "MainWindow.xaml.cs"));
        Assert(source.Contains("https://www.facebook.com/grayscaleconsultants", StringComparison.Ordinal));
        Assert(source.Contains("UseShellExecute = true", StringComparison.Ordinal));
    }

    private static void StageRepresentativesAreValid()
    {
        Assert(TattooStageCatalog.All.Count == 16);
        Assert(TattooStageCatalog.ImageStages.Count == 8);
        Assert(TattooStageCatalog.AnatomicalStages.Count == 8);
        Assert(TattooStageCatalog.All.Select(stage => stage.Kind).Distinct().Count() == 16);
        Assert(TattooStageCatalog.All.All(stage =>
            TattooStageCatalog.FromSlider(stage.RepresentativeSliderValue).Kind == stage.Kind));
        Assert(TattooStageCatalog.ImageStages.All(stage => !stage.IsAnatomicalPlacement));
        Assert(TattooStageCatalog.AnatomicalStages.All(stage => stage.IsAnatomicalPlacement));
        var reverseSourceValues = TattooStageCatalog.AnatomicalStages
            .Select(stage => stage.SourceImageSliderValue).ToArray();
        Assert(reverseSourceValues.Length == 8 && reverseSourceValues[0] == 91 &&
               reverseSourceValues[1] == 78 && reverseSourceValues[2] == 62 &&
               reverseSourceValues[3] == 48 && reverseSourceValues[4] == 35 &&
               reverseSourceValues[5] == 23 && reverseSourceValues[6] == 12 &&
               reverseSourceValues[7] == 0);
    }

    private static void AnatomicalDefaultsMatchBrief()
    {
        Assert(AnatomicalDefaults.Sex == AnatomicalSex.Male);
        Assert(AnatomicalDefaults.Region == BodyRegionKind.LeftOuterUpperArm);
        Assert(Math.Abs(AnatomicalDefaults.HeightCentimeters - 163) < 0.01);
        Assert(Math.Abs(AnatomicalDefaults.SkinToneValue - 55) < 0.01);
        Assert(AnatomicalDefaults.DescribeHeight(163).Contains("average Filipino adult", StringComparison.Ordinal));
        Assert(AnatomicalDefaults.SkinToneFromSlider(55).Description ==
               "light brown to medium tan, Filipino");

        var document = LoadMainWindowXaml();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var male = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "MaleBodyRadioButton");
        var size = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "BodySizeSlider");
        var tone = document.Descendants().Single(item => (string?)item.Attribute(x + "Name") == "SkinToneSlider");
        Assert((string?)male.Attribute("IsChecked") == "True");
        Assert((string?)size.Attribute("Value") == "163");
        Assert((string?)tone.Attribute("Value") == "55");
    }

    private static void BodyRegionCatalogIsComplete()
    {
        Assert(BodyRegionCatalog.All.Count >= 22);
        Assert(BodyRegionCatalog.All.Select(region => region.Kind).Distinct().Count() == BodyRegionCatalog.All.Count);
        Assert(BodyRegionCatalog.All.Select(region => region.DisplayName).Distinct(StringComparer.Ordinal).Count() ==
               BodyRegionCatalog.All.Count);
        Assert(BodyRegionCatalog.Get(BodyRegionCatalog.DefaultRegion).AccessibleDescription.Contains(
            "left upper arm", StringComparison.OrdinalIgnoreCase));
        Assert(BodyRegionCatalog.Get(BodyRegionCatalog.DefaultRegion).ToString() ==
               BodyRegionCatalog.Get(BodyRegionCatalog.DefaultRegion).DisplayName);
        Assert(BodyRegionCatalog.All.Any(region => region.Kind == BodyRegionKind.FullChestAndAbdomen));
        Assert(BodyRegionCatalog.All.Any(region => region.Kind == BodyRegionKind.FullBack));
        Assert(BodyRegionCatalog.All.All(region => !string.IsNullOrWhiteSpace(region.AccessibleDescription)));
    }

    private static void TattooTextureRemovesBackground()
    {
        var source = new ImageFrame(4, 1,
        [
            205, 225, 238, 255,
            0, 0, 0, 255,
            30, 80, 220, 255,
            205, 225, 238, 255,
        ]);
        var ink = TattooInkTexture.Create(source);
        var pixels = Pixels(ink).ToArray();
        Assert(pixels[0].Alpha == 0);
        Assert(pixels[1].Alpha > 200);
        Assert(pixels[2].Alpha > 150);
        Assert(pixels[2].Red < 220 && pixels[2].Green < 80 && pixels[2].Blue < 30);
        Assert(ink.Width == source.Width && ink.Height == source.Height);
    }

    private static void AnatomicalSelectionSynchronizes()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "MainWindow.xaml.cs"));
        Assert(source.Contains("BodyRegionTapped", StringComparison.Ordinal));
        Assert(source.Contains("BodyRegionComboBox.SelectedItem = BodyRegionCatalog.Get(regionKind)", StringComparison.Ordinal));
        Assert(source.Contains("anatomy.SelectRegion(region.Kind, orientToRegion: true)", StringComparison.Ordinal));
        Assert(source.Contains("BodyRegionComboBox_SelectionChanged", StringComparison.Ordinal));
    }

    private static void WpfAnatomyAdapterPreservesSharedGeometry()
    {
        var controllerSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App",
            "Anatomy", "AnatomyViewportController.cs"));
        Assert(controllerSource.Contains("AnatomicalGeometryCatalog.ForSex(sex).BodySegments",
            StringComparison.Ordinal));
        Assert(controllerSource.Contains("GetPlacement(region).Mesh", StringComparison.Ordinal));
        Assert(controllerSource.Contains("TattooInkTexture.CreatePlacementTexture(source)",
            StringComparison.Ordinal));
        Assert(!controllerSource.Contains("CreateCylinderPatch", StringComparison.Ordinal));

        foreach (var sex in Enum.GetValues<AnatomicalSex>())
        {
            var geometry = AnatomicalGeometryCatalog.ForSex(sex);
            foreach (var source in geometry.BodySegments.Select(segment => segment.Mesh)
                         .Concat(geometry.PlacementSurfaces.Select(surface => surface.Mesh)))
            {
                var adapted = WpfAnatomicalMeshAdapter.Create(source);
                Assert(adapted.IsFrozen);
                Assert(adapted.Positions.Count == source.Vertices.Length);
                Assert(adapted.Normals.Count == source.Vertices.Length);
                Assert(adapted.TextureCoordinates.Count == source.Vertices.Length);
                Assert(adapted.TriangleIndices.SequenceEqual(source.TriangleIndices));
                for (var index = 0; index < source.Vertices.Length; index++)
                {
                    var vertex = source.Vertices[index];
                    Assert(adapted.Positions[index] == new System.Windows.Media.Media3D.Point3D(
                        vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
                    Assert(adapted.Normals[index] == new System.Windows.Media.Media3D.Vector3D(
                        vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
                    Assert(adapted.TextureCoordinates[index] == new System.Windows.Point(
                        vertex.SurfacePoint.U, vertex.SurfacePoint.V));
                }
            }
        }
    }

    private static void AnatomicalPlacementCanBeSaved()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "MainWindow.xaml.cs"));
        Assert(source.Contains("stage.IsAnatomicalPlacement", StringComparison.Ordinal));
        Assert(source.Contains("CaptureAnatomicalPreview()", StringComparison.Ordinal));
        Assert(source.Contains("target.Render(AnatomyViewport)", StringComparison.Ordinal));
        Assert(source.Contains("PhotoCodec.Save(fullResolution, destination, format.Format)", StringComparison.Ordinal));
    }

    private static void CameraFramingCoversEveryRegion()
    {
        foreach (var region in BodyRegionCatalog.All)
        {
            var frame = AnatomicalCameraFraming.ForRegion(region.Kind);
            Assert(frame.ContextDistance > frame.DetailDistance);
            Assert(frame.DetailDistance >= 4.5);
            Assert(!string.IsNullOrWhiteSpace(frame.ContextDescription));
        }
        var chest = AnatomicalCameraFraming.ForRegion(BodyRegionKind.UpperLeftChest);
        Assert(chest.ContextDescription.Contains("head", StringComparison.Ordinal));
        Assert(chest.ContextDescription.Contains("neck", StringComparison.Ordinal));
        Assert(chest.ContextDescription.Contains("left shoulder", StringComparison.Ordinal));
        Assert(chest.ContextDescription.Contains("upper-left chest", StringComparison.Ordinal));
    }

    private static void PlacementStagesAnimateCamera()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "Anatomy",
            "AnatomyViewportController.cs"));
        var window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "MainWindow.xaml.cs"));
        Assert(controller.Contains("CubicEase", StringComparison.Ordinal));
        Assert(controller.Contains("TimeSpan.FromMilliseconds(450)", StringComparison.Ordinal));
        Assert(controller.Contains("TimeSpan.FromMilliseconds(2350)", StringComparison.Ordinal));
        Assert(window.Contains("anatomy.AnimatePlacementFocus()", StringComparison.Ordinal));
        Assert(window.Contains("anatomy.FocusSelectedRegionInstant()", StringComparison.Ordinal));
    }

    private static void ProductNameIsCorrect()
    {
        var document = LoadMainWindowXaml();
        var window = document.Root ?? throw new InvalidOperationException("Main window XAML is empty.");
        Assert(((string?)window.Attribute("Title"))?.Contains("Tattoo Art Prepper", StringComparison.Ordinal) == true);
        Assert(!document.ToString().Contains("Tattoo Art Preparation", StringComparison.Ordinal));
    }

    private static void HardwareTiersAreConservative()
    {
        const ulong gib = 1_073_741_824;
        var compact = OfflineAiProfileSelector.Select(new(8 * gib, 6 * gib, false, 0, "Integrated"));
        var balanced = OfflineAiProfileSelector.Select(new(16 * gib, 10 * gib, true, 6 * gib, "GPU"));
        var professional = OfflineAiProfileSelector.Select(new(32 * gib, 20 * gib, true, 12 * gib, "GPU"));
        var constrained = OfflineAiProfileSelector.Select(new(32 * gib, 8 * gib, true, 12 * gib, "GPU"));
        Assert(compact.Model == "qwen3-vl:2b-instruct");
        Assert(balanced.Model == "qwen3-vl:4b-instruct");
        Assert(professional.Model == "qwen3-vl:8b-instruct");
        Assert(constrained.Model == "qwen3-vl:2b-instruct");
        Assert(compact.KeepAlive == "0" && balanced.KeepAlive == "0" && professional.KeepAlive == "0");
        Assert(compact.MaximumImageDimension <= 768 && compact.ContextLength <= 1536);
        Assert(!OfflineAiProfileSelector.HasSafeHeadroom(compact,
            new(4 * gib, gib, false, 0, "Integrated")));
        Assert(!OfflineAiProfileSelector.HasSafeHeadroom(compact,
            new(4 * gib, 4 * gib, false, 0, "Integrated")));
    }

    private static void OfflineAiRechecksMemory()
    {
        const ulong gib = 1_073_741_824;
        var profile = OfflineAiProfileSelector.Select(new(8 * gib, 6 * gib, false, 0, "Integrated"));
        AssertThrows<InvalidOperationException>(() => OfflineAiProfileSelector.EnsureSafeHeadroom(
            profile, new(8 * gib, 2 * gib, false, 0, "Integrated"), "loading a stage"));
        OfflineAiProfileSelector.EnsureSafeHeadroom(profile,
            new(8 * gib, 6 * gib, false, 0, "Integrated"), "loading a stage");
    }

    private static void HardwareInventoryIsUsable()
    {
        var hardware = OfflineAiHardwareDetector.Detect();
        Assert(hardware.TotalMemoryBytes > 0);
        Assert(hardware.AvailableMemoryBytes > 0);
        Assert(!string.IsNullOrWhiteSpace(hardware.GpuDescription));
        Assert(!string.IsNullOrWhiteSpace(hardware.AccessibleSummary));
    }

    private static void OfflineAiPromptIsGrounded()
    {
        var stage = TattooStageCatalog.All.Single(item => item.Kind == TattooStageKind.LineArt);
        var prompt = OllamaVisionClient.BuildStagePrompt(stage, stage.RepresentativeSliderValue);
        Assert(prompt.Contains("actual rendered preview", StringComparison.Ordinal));
        Assert(prompt.Contains(stage.Name, StringComparison.Ordinal));
        Assert(prompt.Contains(stage.Description, StringComparison.Ordinal));
        Assert(prompt.Contains("Do not guess", StringComparison.Ordinal));
        Assert(prompt.Contains("totally blind", StringComparison.Ordinal));
    }

    private static void OfflineAiControlsAreAccessible()
    {
        var document = LoadMainWindowXaml();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var checkBox = document.Descendants().Single(item =>
            (string?)item.Attribute(x + "Name") == "OfflineAIDescribeCheckBox");
        Assert((string?)checkBox.Attribute("AutomationProperties.Name") == "Offline AI Describe");
        Assert((string?)checkBox.Attribute("AutomationProperties.AutomationId") == "OfflineAIDescribeCheckBox");
        Assert(!string.IsNullOrWhiteSpace((string?)checkBox.Attribute("AutomationProperties.HelpText")));
        var description = document.Descendants().Single(item =>
            (string?)item.Attribute(x + "Name") == "OfflineAiDescriptionText");
        Assert((string?)description.Attribute("AutomationProperties.LiveSetting") == "Polite");
        Assert((string?)description.Attribute("Focusable") == "True");
    }

    private static void OfflineAiClientIsLocal()
    {
        var handler = new FakeOllamaHandler();
        using var client = new OllamaVisionClient(handler);
        var status = client.GetStatusAsync().GetAwaiter().GetResult();
        Assert(status.IsReachable && status.IsVersionSupported);
        Assert(status.HasModel("qwen3-vl:4b-instruct"));
        var profile = OfflineAiProfileSelector.Select(
            new(16UL * 1_073_741_824, 6UL * 1_073_741_824, false, 0, "Integrated"));
        var stage = TattooStageCatalog.All[0];
        var description = client.DescribeStageAsync("AA==", stage, 0, profile).GetAwaiter().GetResult();
        Assert(description == "A centered rose keeps crisp outer petals and fine interior veins.");
        Assert(handler.Requests.All(request => request.Host == "127.0.0.1"));
        Assert(handler.ChatBody.Contains(profile.Model, StringComparison.Ordinal));
        Assert(handler.ChatBody.Contains("actual rendered preview", StringComparison.Ordinal));
        Assert(OllamaVisionClient.BuildStagePrompt(stage, 0, "A frontal gray wolf with a red neckerchief.")
            .Contains("A frontal gray wolf with a red neckerchief.", StringComparison.Ordinal));
        Assert(!handler.ChatBody.Contains("num_batch", StringComparison.Ordinal));
    }

    private static void AllDescriptionsPreload()
    {
        var handler = new FakeOllamaHandler();
        using var client = new OllamaVisionClient(handler);
        var profile = new OfflineAiModelProfile(OfflineAiTier.Compact, "Compact",
            "qwen3-vl:2b-instruct", 96, 2048, 256, "0", 0);
        var updates = new List<OfflineAiPreloadProgress>();
        var descriptions = OfflineAiDescriptionPreloader.PreloadAsync(
            ColorPattern(32, 24), profile, client, new InlineProgress<OfflineAiPreloadProgress>(updates.Add))
            .GetAwaiter().GetResult();
        Assert(descriptions.Count == TattooStageCatalog.All.Count);
        Assert(TattooStageCatalog.All.All(stage => descriptions.ContainsKey(stage.Kind)));
        Assert(updates.Count == 16 && updates[^1].Completed == 16 && updates[^1].Percentage == 100);
        Assert(handler.ChatRequestCount == 16);
        Assert(handler.UnloadRequestCount == 16);
    }

    private static void LengthLimitedAiOutputIsSafe()
    {
        const string response = "{\"message\":{\"content\":\"A centered wolf face keeps both eyes and its dark muzzle distinct. Fine contours preserve the ears and layered cheek fur. This unfinished excess should never reach JAWS\"},\"done\":true,\"done_reason\":\"length\"}";
        var handler = new FakeOllamaHandler(response);
        using var client = new OllamaVisionClient(handler);
        var profile = new OfflineAiModelProfile(OfflineAiTier.Compact, "Compact",
            "qwen3-vl:2b-instruct", 96, 1536, 512, "0", 0);
        var description = client.DescribeStageAsync("AA==", TattooStageCatalog.All[0], 0, profile)
            .GetAwaiter().GetResult();
        Assert(description == "A centered wolf face keeps both eyes and its dark muzzle distinct. Fine contours preserve the ears and layered cheek fur.");
    }

    private static void VisionImagesAreBounded()
    {
        var encoded = Convert.FromBase64String(PhotoCodec.ToPngBase64(ColorPattern(200, 100), 80));
        using var stream = new MemoryStream(encoded);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert(decoder.Frames[0].PixelWidth == 80 && decoder.Frames[0].PixelHeight == 40);
    }

    private static void HeartbeatIsValidWave()
    {
        var assembly = typeof(OfflineAiDescriptionPreloader).Assembly;
        var type = assembly.GetType("TATAPP.App.OfflineAI.ProcessingHeartbeat", throwOnError: true)!;
        var method = type.GetMethod("CreateWaveData",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var wave = (byte[])method.Invoke(null, null)!;
        Assert(wave.Length > 44);
        Assert(Encoding.ASCII.GetString(wave, 0, 4) == "RIFF");
        Assert(Encoding.ASCII.GetString(wave, 8, 4) == "WAVE");
    }

    private static XDocument LoadMainWindowXaml() =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", "TATAPP.App", "MainWindow.xaml"), LoadOptions.PreserveWhitespace);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TATAPP.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TATAPP repository root.");
    }

    private static ImageFrame ColorPattern(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)((x * 17 + y * 3) % 256);
                pixels[offset + 1] = (byte)((x * 5 + y * 19) % 256);
                pixels[offset + 2] = (byte)((x * 13 + y * 11) % 256);
                pixels[offset + 3] = (byte)(128 + (x + y) % 128);
            }
        return new ImageFrame(width, height, pixels, 300, 300);
    }

    private static ImageFrame SquarePattern(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)255, checked(width * height * 4)).ToArray();
        for (var y = 20; y < height - 20; y++)
            for (var x = 20; x < width - 20; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 15;
                pixels[offset + 3] = 255;
            }
        return new ImageFrame(width, height, pixels);
    }

    private static IEnumerable<(byte Blue, byte Green, byte Red, byte Alpha)> Pixels(ImageFrame frame)
    {
        for (var offset = 0; offset < frame.Pixels.Length; offset += 4)
            yield return (frame.Pixels[offset], frame.Pixels[offset + 1], frame.Pixels[offset + 2], frame.Pixels[offset + 3]);
    }

    private static void Assert(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeOllamaHandler(string? chatResponse = null) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string ChatBody { get; private set; } = string.Empty;
        public int ChatRequestCount { get; private set; }
        public int UnloadRequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri ?? throw new InvalidOperationException("Missing request URI."));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/version") return Json("{\"version\":\"0.12.7\"}");
            if (path == "/api/tags")
                return Json("{\"models\":[{\"name\":\"qwen3-vl:4b-instruct\"}]}");
            if (path == "/api/chat")
            {
                ChatRequestCount++;
                ChatBody = await (request.Content ?? throw new InvalidOperationException("Missing content."))
                    .ReadAsStringAsync(cancellationToken);
                return Json(chatResponse ?? "{\"message\":{\"content\":\"A centered rose keeps crisp outer petals and fine interior veins.\"},\"done\":true,\"done_reason\":\"stop\"}");
            }
            if (path == "/api/generate")
            {
                UnloadRequestCount++;
                return Json("{\"done\":true}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json"),
        };
    }
}
