using TATAPP.App.Imaging;
using TATAPP.Core;

namespace TATAPP.App.OfflineAI;

public sealed record OfflineAiPreloadProgress(int Completed, int Total, TattooStage Stage)
{
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}

public static class OfflineAiDescriptionPreloader
{
    public static async Task<IReadOnlyDictionary<TattooStageKind, string>> PreloadAsync(
        ImageFrame source,
        OfflineAiModelProfile profile,
        OllamaVisionClient client,
        IProgress<OfflineAiPreloadProgress>? progress = null,
        Func<TattooStage, CancellationToken, Task<ImageFrame>>? renderStage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(client);

        var descriptions = new Dictionary<TattooStageKind, string>();
        var stages = TattooStageCatalog.All;
        string? sourceDesignContext = null;
        for (var index = 0; index < stages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stage = stages[index];
            var description = await PrepareOneStageAsync(source, stage, profile, client,
                renderStage, sourceDesignContext, cancellationToken).ConfigureAwait(false);
            descriptions.Add(stage.Kind, description);
            if (stage.Kind == TattooStageKind.Original)
                sourceDesignContext = description;
            progress?.Report(new(index + 1, stages.Count, stage));

            // Each stage can temporarily own a render, a resized bitmap, PNG
            // bytes and a base64 request. Release those large, short-lived
            // objects before beginning the next model load.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
        }
        return descriptions;
    }

    private static async Task<string> PrepareOneStageAsync(
        ImageFrame source,
        TattooStage stage,
        OfflineAiModelProfile profile,
        OllamaVisionClient client,
        Func<TattooStage, CancellationToken, Task<ImageFrame>>? renderStage,
        string? sourceDesignContext,
        CancellationToken cancellationToken)
    {
        if (profile.MinimumAvailableMemoryBytes > 0)
            OfflineAiProfileSelector.EnsureSafeHeadroom(profile,
                OfflineAiHardwareDetector.Detect(), $"rendering {stage.Name}");

        // The app supplies a renderer for anatomical stages so Qwen sees the
        // real WPF 3D placement, not a prompt-derived approximation.
        var rendered = renderStage is null
            ? await Task.Run(() => TattooStageCatalog.RenderDesign(source, stage, cancellationToken),
                cancellationToken).ConfigureAwait(false)
            : await renderStage(stage, cancellationToken).ConfigureAwait(false);
        var png = await Task.Run(
            () => PhotoCodec.ToPngBase64(rendered, profile.MaximumImageDimension),
            cancellationToken).ConfigureAwait(false);

        if (profile.MinimumAvailableMemoryBytes > 0)
            OfflineAiProfileSelector.EnsureSafeHeadroom(profile,
                OfflineAiHardwareDetector.Detect(), $"loading the model for {stage.Name}");

        try
        {
            return await client.DescribeStageAsync(png, stage,
                stage.RepresentativeSliderValue, profile, sourceDesignContext,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // keep_alive=0 is also set on the request. This explicit unload is
            // a second safety boundary for runtimes that defer that release.
            await client.TryUnloadModelAsync(profile.Model).ConfigureAwait(false);
        }
    }
}
