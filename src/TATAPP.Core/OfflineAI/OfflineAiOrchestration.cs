using System.Collections.Frozen;
using TATAPP.Core.Caching;

namespace TATAPP.Core.OfflineAI;

public enum OfflineModelAdmissionMode
{
    Installation,
    InstalledUse,
}

public static class OfflineModelPolicy
{
    public static OfflineModelVariant? SelectSmallestSuitable(OfflineModelCatalog catalog,
        OfflineAiDeviceCapabilities device) => SelectSmallestSuitable(
        catalog, device, OfflineModelAdmissionMode.Installation);

    public static OfflineModelVariant? SelectSmallestSuitable(OfflineModelCatalog catalog,
        OfflineAiDeviceCapabilities device, OfflineModelAdmissionMode admissionMode) =>
        SuitableVariants(catalog, device, admissionMode)
            .OrderBy(variant => variant.WorkingSetBytes)
            .ThenBy(variant => variant.Artifacts.Sum(artifact => artifact.InstalledBytes))
            .ThenBy(variant => variant.QualityRank)
            .FirstOrDefault();

    public static IReadOnlyList<OfflineModelVariant> FallbackCandidates(
        OfflineModelCatalog catalog, OfflineAiDeviceCapabilities device,
        OfflineModelVariant failedVariant) => FallbackCandidates(
        catalog, device, failedVariant, OfflineModelAdmissionMode.Installation);

    public static IReadOnlyList<OfflineModelVariant> FallbackCandidates(
        OfflineModelCatalog catalog, OfflineAiDeviceCapabilities device,
        OfflineModelVariant failedVariant, OfflineModelAdmissionMode admissionMode) =>
        SuitableVariants(catalog, device, admissionMode)
            .Where(variant => variant.QualityRank < failedVariant.QualityRank &&
                              variant.WorkingSetBytes < failedVariant.WorkingSetBytes)
            .OrderByDescending(variant => variant.QualityRank)
            .ThenByDescending(variant => variant.WorkingSetBytes)
            .ToArray();

    public static bool IsSuitable(OfflineModelVariant variant, OfflineAiDeviceCapabilities device)
        => IsSuitable(variant, device, OfflineModelAdmissionMode.Installation);

    public static bool IsSuitable(OfflineModelVariant variant, OfflineAiDeviceCapabilities device,
        OfflineModelAdmissionMode admissionMode)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(device);
        if (!Enum.IsDefined(admissionMode)) throw new ArgumentOutOfRangeException(nameof(admissionMode));
        return HasPreloadHeadroom(variant, device) &&
               (admissionMode == OfflineModelAdmissionMode.InstalledUse ||
                device.AvailableStorageBytes >= variant.RequiredFreeStorageBytes);
    }

    /// <summary>
    /// Compatibility name for the pre-load admission check.
    /// </summary>
    public static bool HasRuntimeHeadroom(OfflineModelVariant variant,
        OfflineAiDeviceCapabilities device) => HasPreloadHeadroom(variant, device);

    /// <summary>
    /// Admission before opening a model session. This includes the model working set
    /// because it has not yet become resident.
    /// </summary>
    public static bool HasPreloadHeadroom(OfflineModelVariant variant,
        OfflineAiDeviceCapabilities device)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(device);
        if (!MeetsStaticRequirements(variant, device) || device.IsLowMemory) return false;
        if (device.MemoryClassBytes < variant.MinimumMemoryClassBytes ||
            device.AvailableMemoryBytes < variant.RequiredAvailableMemoryBytes)
            return false;
        return true;
    }

    /// <summary>
    /// Headroom after the model session is resident. Requiring its working set again
    /// would double-count memory already reflected by the live available-memory probe.
    /// </summary>
    public static bool HasResidentSessionHeadroom(OfflineModelVariant variant,
        OfflineAiDeviceCapabilities device)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(device);
        return MeetsStaticRequirements(variant, device) &&
               !device.IsLowMemory &&
               device.MemoryClassBytes >= variant.MinimumMemoryClassBytes &&
               device.AvailableMemoryBytes >= variant.MemorySafetyReserveBytes;
    }

    private static IEnumerable<OfflineModelVariant> SuitableVariants(OfflineModelCatalog catalog,
        OfflineAiDeviceCapabilities device, OfflineModelAdmissionMode admissionMode)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(device);
        if (!Enum.IsDefined(admissionMode)) throw new ArgumentOutOfRangeException(nameof(admissionMode));
        return catalog.Variants.Where(variant => IsSuitable(variant, device, admissionMode));
    }

    private static bool MeetsStaticRequirements(OfflineModelVariant variant,
        OfflineAiDeviceCapabilities device) =>
        variant.Tested &&
        device.AndroidApiLevel >= variant.MinimumApiLevel &&
        variant.SupportedAbis.Any(abi => device.SupportedAbis.Contains(abi, StringComparer.Ordinal)) &&
        (device.Acceleration & variant.RequiredAcceleration) == variant.RequiredAcceleration &&
        variant.RequiredCpuFeatures.All(feature => device.CpuFeatures.Contains(feature, StringComparer.Ordinal));
}

public enum ModelProvisionOutcome
{
    AlreadyReady,
    ConsentRequired,
    InstalledAndReady,
}

public sealed record ModelProvisionResult(ModelProvisionOutcome Outcome,
    ModelInstallationStatus Status);

public sealed class OfflineModelProvisioner(IOfflineModelInstaller installer)
{
    private readonly IOfflineModelInstaller installer = installer ??
        throw new ArgumentNullException(nameof(installer));

    public async Task<ModelProvisionResult> EnsureReadyAsync(OfflineModelVariant variant,
        bool userConsented, IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var status = await installer.GetStatusAsync(variant, cancellationToken).ConfigureAwait(false);
        ValidateStatus(variant, status);
        if (status.State == ModelInstallationState.Ready)
        {
            EnsureVerifiedReady(variant, status);
            return new(ModelProvisionOutcome.AlreadyReady, status);
        }
        if (!userConsented)
            return new(ModelProvisionOutcome.ConsentRequired, status);

        await installer.InstallAsync(new(variant, true), progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        status = await installer.GetStatusAsync(variant, cancellationToken).ConfigureAwait(false);
        ValidateStatus(variant, status);
        if (status.State != ModelInstallationState.Ready)
            throw new InvalidDataException(
                $"Model installation completed without a verified ready state for '{variant.Id}'.");
        EnsureVerifiedReady(variant, status);
        return new(ModelProvisionOutcome.InstalledAndReady, status);
    }

    private static void ValidateStatus(OfflineModelVariant variant, ModelInstallationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!string.Equals(status.ModelId, variant.Id, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The installer returned status for '{status.ModelId}' while '{variant.Id}' was requested.");
        if (status.VerifiedArtifactCount < 0 || status.VerifiedBytes < 0)
            throw new InvalidDataException("Model verification totals cannot be negative.");
    }

    private static void EnsureVerifiedReady(OfflineModelVariant variant, ModelInstallationStatus status)
    {
        var expectedBytes = variant.Artifacts.Sum(artifact => artifact.InstalledBytes);
        if (status.VerifiedArtifactCount != variant.Artifacts.Count ||
            status.VerifiedBytes != expectedBytes)
            throw new InvalidDataException(
                $"Model '{variant.Id}' is marked ready without the exact verified artifact count and byte total.");
    }
}

public sealed class OfflineDescriptionBatch
{
    private readonly FrozenDictionary<TattooStageKind, string> descriptions;

    public OfflineDescriptionBatch(IReadOnlyDictionary<TattooStageKind, string> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        if (descriptions.Count != TattooStageCatalog.All.Count ||
            TattooStageCatalog.All.Any(stage => !descriptions.TryGetValue(stage.Kind, out var description) ||
                                                string.IsNullOrWhiteSpace(description)))
            throw new InvalidDataException("A description batch must contain every visual stage exactly once.");
        this.descriptions = descriptions.ToFrozenDictionary();
        EstimatedBytes = this.descriptions.Sum(pair =>
            sizeof(int) + checked(pair.Value.Length * sizeof(char)));
    }

    public IReadOnlyDictionary<TattooStageKind, string> Descriptions => descriptions;
    public long EstimatedBytes { get; }
    public string this[TattooStageKind stage] => descriptions[stage];
}

public sealed class OfflineDescriptionCache : IDisposable
{
    private readonly ByteBudgetLruCache<OfflineDescriptionCacheKey, OfflineDescriptionBatch> cache;

    public OfflineDescriptionCache(long maximumBytes) =>
        cache = new(maximumBytes, batch => Math.Max(1, batch.EstimatedBytes));

    public int Count => cache.Count;
    public long CurrentBytes => cache.CurrentBytes;

    public bool TryGet(OfflineDescriptionCacheKey key, out OfflineDescriptionBatch? batch) =>
        TryGetSnapshot(key, out batch);

    public bool Store(OfflineDescriptionCacheKey key, OfflineDescriptionBatch batch) =>
        cache.Set(key, batch);

    public int InvalidateSource(string sourceFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        return cache.Invalidate(key =>
            string.Equals(key.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal));
    }

    public int InvalidateModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return cache.Invalidate(key => string.Equals(key.ModelId, modelId, StringComparison.Ordinal));
    }

    public int InvalidateAnatomy(string sourceFingerprint, string anatomyStateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(anatomyStateKey);
        return cache.Invalidate(key =>
            string.Equals(key.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal) &&
            string.Equals(key.AnatomyStateKey, anatomyStateKey, StringComparison.Ordinal));
    }

    public void Dispose() => cache.Dispose();

    private bool TryGetSnapshot(OfflineDescriptionCacheKey key, out OfflineDescriptionBatch? batch)
    {
        if (!cache.TryAcquire(key, out var lease))
        {
            batch = null;
            return false;
        }
        using (var acquired = lease!)
        {
            // OfflineDescriptionBatch is deeply immutable, so the snapshot remains valid
            // after the cache lease ends and needs no native-resource lifetime.
            batch = acquired.Value;
            return true;
        }
    }
}

/// <summary>
/// Prepares exactly one complete description batch at a time. A single model session is
/// reused, frames are disposed stage-by-stage, and an incomplete or canceled run is never cached.
/// </summary>
public sealed class OfflineDescriptionPreloadCoordinator : IDisposable
{
    private readonly OfflineDescriptionCache cache;
    private readonly SemaphoreSlim preloadGate = new(1, 1);
    private readonly object synchronization = new();
    private readonly Dictionary<OfflineDescriptionCacheKey, Task<OfflineDescriptionBatch>> inFlight = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool disposed;
    private bool cancellationIssued;
    private bool resourcesDisposed;

    public OfflineDescriptionPreloadCoordinator(OfflineDescriptionCache cache) =>
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public Task<OfflineDescriptionBatch> PreloadAsync(OfflineDescriptionCacheKey key,
        OfflineModelVariant variant, IStageDescriptionImageSource imageSource,
        IOfflineVisionSessionFactory sessionFactory, IOfflineAiCapabilityProbe capabilityProbe,
        IProgress<OfflineDescriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(imageSource);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(capabilityProbe);

        lock (synchronization)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cache.TryGet(key, out var cached)) return Task.FromResult(cached!);
            if (inFlight.TryGetValue(key, out var existing))
                return existing.WaitAsync(cancellationToken);

            var completion = new TaskCompletionSource<OfflineDescriptionBatch>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            inFlight.Add(key, completion.Task);
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            _ = RunTrackedAsync(key, variant, imageSource, sessionFactory, capabilityProbe,
                progress, linkedCancellation, completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    private async Task RunTrackedAsync(OfflineDescriptionCacheKey key, OfflineModelVariant variant,
        IStageDescriptionImageSource imageSource, IOfflineVisionSessionFactory sessionFactory,
        IOfflineAiCapabilityProbe capabilityProbe, IProgress<OfflineDescriptionProgress>? progress,
        CancellationTokenSource linkedCancellation,
        TaskCompletionSource<OfflineDescriptionBatch> completion)
    {
        try
        {
            completion.TrySetResult(await RunPreloadAsync(key, variant, imageSource,
                sessionFactory, capabilityProbe, progress, linkedCancellation.Token)
                .ConfigureAwait(false));
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            linkedCancellation.Dispose();
            var releaseResources = false;
            lock (synchronization)
            {
                inFlight.Remove(key);
                releaseResources = MarkResourcesForDisposalIfReady();
            }
            if (releaseResources) ReleaseResources();
        }
    }

    private async Task<OfflineDescriptionBatch> RunPreloadAsync(OfflineDescriptionCacheKey key,
        OfflineModelVariant variant, IStageDescriptionImageSource imageSource,
        IOfflineVisionSessionFactory sessionFactory, IOfflineAiCapabilityProbe capabilityProbe,
        IProgress<OfflineDescriptionProgress>? progress, CancellationToken cancellationToken)
    {
        var gateHeld = false;
        try
        {
            await preloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            if (cache.TryGet(key, out var cached)) return cached!;
            await EnsureRuntimeHeadroomAsync(variant, capabilityProbe, sessionResident: false, cancellationToken)
                .ConfigureAwait(false);
            var descriptions = new Dictionary<TattooStageKind, string>();
            string? verifiedSourceDescription = null;
            await using var session = await sessionFactory.OpenAsync(variant, cancellationToken)
                .ConfigureAwait(false);
            for (var index = 0; index < TattooStageCatalog.All.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureRuntimeHeadroomAsync(variant, capabilityProbe, sessionResident: true, cancellationToken)
                    .ConfigureAwait(false);
                var stage = TattooStageCatalog.All[index];
                await using var image = await imageSource.RenderAsync(stage,
                    variant.MaximumImageDimension, cancellationToken).ConfigureAwait(false);
                if (image.PixelWidth <= 0 || image.PixelHeight <= 0 ||
                    image.PixelWidth > variant.MaximumImageDimension ||
                    image.PixelHeight > variant.MaximumImageDimension || image.EncodedBytes.IsEmpty)
                    throw new InvalidDataException($"The rendered image for '{stage.Name}' is invalid or exceeds the model limit.");

                await EnsureRuntimeHeadroomAsync(variant, capabilityProbe, sessionResident: true, cancellationToken)
                    .ConfigureAwait(false);

                var description = await session.DescribeAsync(new(stage, image,
                    verifiedSourceDescription, variant.ContextTokens, variant.MaximumOutputTokens),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(description))
                    throw new InvalidDataException($"The local model returned no description for '{stage.Name}'.");
                descriptions.Add(stage.Kind, description.Trim());
                if (stage.Kind == TattooStageKind.Original)
                    verifiedSourceDescription = description.Trim();
                progress?.Report(new(index + 1, TattooStageCatalog.All.Count, stage));
            }

            var completed = new OfflineDescriptionBatch(descriptions);
            if (!cache.Store(key, completed))
                throw new InvalidOperationException(
                    "The complete offline-description batch exceeds its configured cache budget.");
            return completed;
        }
        finally
        {
            if (gateHeld) preloadGate.Release();
        }
    }

    private static async Task EnsureRuntimeHeadroomAsync(OfflineModelVariant variant,
        IOfflineAiCapabilityProbe capabilityProbe, bool sessionResident,
        CancellationToken cancellationToken)
    {
        var capabilities = await capabilityProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var sufficient = sessionResident
            ? OfflineModelPolicy.HasResidentSessionHeadroom(variant, capabilities)
            : OfflineModelPolicy.HasPreloadHeadroom(variant, capabilities);
        if (!sufficient)
            throw new InvalidOperationException(
                $"Offline description processing stopped before memory pressure could exhaust '{variant.Id}'" +
                (sessionResident ? " while its session was resident." : "."));
    }

    public void Dispose()
    {
        var releaseResources = false;
        lock (synchronization)
        {
            if (disposed) return;
            disposed = true;
        }
        lifetimeCancellation.Cancel();
        lock (synchronization)
        {
            cancellationIssued = true;
            releaseResources = MarkResourcesForDisposalIfReady();
        }
        if (releaseResources) ReleaseResources();
    }

    private bool MarkResourcesForDisposalIfReady()
    {
        if (!disposed || !cancellationIssued || resourcesDisposed || inFlight.Count != 0) return false;
        resourcesDisposed = true;
        return true;
    }

    private void ReleaseResources()
    {
        preloadGate.Dispose();
        lifetimeCancellation.Dispose();
    }
}
