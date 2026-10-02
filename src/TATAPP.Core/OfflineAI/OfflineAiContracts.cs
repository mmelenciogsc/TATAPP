using TATAPP.Core.Workflow;

namespace TATAPP.Core.OfflineAI;

[Flags]
public enum OfflineAiAcceleration
{
    None = 0,
    Cpu = 1,
    Gpu = 2,
    Npu = 4,
}

public sealed class OfflineAiDeviceCapabilities
{
    public OfflineAiDeviceCapabilities(int androidApiLevel, IEnumerable<string> supportedAbis,
        long memoryClassBytes, long largeMemoryClassBytes, long availableMemoryBytes,
        bool isLowMemory, long availableStorageBytes, IEnumerable<string>? cpuFeatures = null,
        OfflineAiAcceleration acceleration = OfflineAiAcceleration.Cpu)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(androidApiLevel);
        ArgumentNullException.ThrowIfNull(supportedAbis);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryClassBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largeMemoryClassBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(availableMemoryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(availableStorageBytes);
        AndroidApiLevel = androidApiLevel;
        SupportedAbis = Array.AsReadOnly(supportedAbis.Select(Normalize)
            .Distinct(StringComparer.Ordinal).ToArray());
        if (SupportedAbis.Count == 0) throw new ArgumentException("At least one ABI is required.", nameof(supportedAbis));
        MemoryClassBytes = memoryClassBytes;
        LargeMemoryClassBytes = largeMemoryClassBytes;
        AvailableMemoryBytes = availableMemoryBytes;
        IsLowMemory = isLowMemory;
        AvailableStorageBytes = availableStorageBytes;
        CpuFeatures = Array.AsReadOnly((cpuFeatures ?? []).Select(Normalize)
            .Distinct(StringComparer.Ordinal).ToArray());
        Acceleration = acceleration;
    }

    public int AndroidApiLevel { get; }
    public IReadOnlyList<string> SupportedAbis { get; }
    public long MemoryClassBytes { get; }
    public long LargeMemoryClassBytes { get; }
    public long AvailableMemoryBytes { get; }
    public bool IsLowMemory { get; }
    public long AvailableStorageBytes { get; }
    public IReadOnlyList<string> CpuFeatures { get; }
    public OfflineAiAcceleration Acceleration { get; }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }
}

public sealed class OfflineModelArtifact
{
    public OfflineModelArtifact(string fileName, Uri downloadUri, long downloadBytes,
        long installedBytes, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(downloadUri);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(downloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installedBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (Path.GetFileName(fileName) != fileName || fileName.Contains('/') || fileName.Contains('\\') ||
            fileName is "." or "..")
            throw new ArgumentException("Model artifact names must be plain file names.", nameof(fileName));
        if (downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Model artifacts must use HTTPS.", nameof(downloadUri));
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Model artifacts require a 64-character SHA-256 digest.", nameof(sha256));
        FileName = fileName;
        DownloadUri = downloadUri;
        DownloadBytes = downloadBytes;
        InstalledBytes = installedBytes;
        Sha256 = sha256.ToLowerInvariant();
    }

    public string FileName { get; }
    public Uri DownloadUri { get; }
    public long DownloadBytes { get; }
    public long InstalledBytes { get; }
    public string Sha256 { get; }
}

public sealed class OfflineModelVariant
{
    public OfflineModelVariant(string id, string displayName, string quantization, int qualityRank,
        bool tested, int minimumApiLevel, IEnumerable<string> supportedAbis,
        long minimumMemoryClassBytes, long minimumAvailableMemoryBytes,
        long workingSetBytes, long memorySafetyReserveBytes, long storageSafetyReserveBytes,
        int maximumImageDimension,
        int contextTokens, int maximumOutputTokens, IEnumerable<OfflineModelArtifact> artifacts,
        IEnumerable<string>? requiredCpuFeatures = null,
        OfflineAiAcceleration requiredAcceleration = OfflineAiAcceleration.Cpu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantization);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qualityRank);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumApiLevel);
        ArgumentNullException.ThrowIfNull(supportedAbis);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumMemoryClassBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumAvailableMemoryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workingSetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(memorySafetyReserveBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(storageSafetyReserveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputTokens);
        ArgumentNullException.ThrowIfNull(artifacts);
        Id = id.Trim();
        DisplayName = displayName.Trim();
        Quantization = quantization.Trim();
        QualityRank = qualityRank;
        Tested = tested;
        MinimumApiLevel = minimumApiLevel;
        SupportedAbis = Array.AsReadOnly(supportedAbis.Select(Normalize)
            .Distinct(StringComparer.Ordinal).ToArray());
        if (SupportedAbis.Count == 0) throw new ArgumentException("At least one ABI is required.", nameof(supportedAbis));
        MinimumMemoryClassBytes = minimumMemoryClassBytes;
        MinimumAvailableMemoryBytes = minimumAvailableMemoryBytes;
        WorkingSetBytes = workingSetBytes;
        MemorySafetyReserveBytes = memorySafetyReserveBytes;
        StorageSafetyReserveBytes = storageSafetyReserveBytes;
        MaximumImageDimension = maximumImageDimension;
        ContextTokens = contextTokens;
        MaximumOutputTokens = maximumOutputTokens;
        Artifacts = Array.AsReadOnly(artifacts.ToArray());
        if (Artifacts.Count == 0) throw new ArgumentException("At least one model artifact is required.", nameof(artifacts));
        if (Artifacts.Select(artifact => artifact.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Artifacts.Count)
            throw new ArgumentException("Model artifact names must be unique.", nameof(artifacts));
        RequiredCpuFeatures = Array.AsReadOnly((requiredCpuFeatures ?? []).Select(Normalize)
            .Distinct(StringComparer.Ordinal).ToArray());
        RequiredAcceleration = requiredAcceleration;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Quantization { get; }
    public int QualityRank { get; }
    public bool Tested { get; }
    public int MinimumApiLevel { get; }
    public IReadOnlyList<string> SupportedAbis { get; }
    public long MinimumMemoryClassBytes { get; }
    public long MinimumAvailableMemoryBytes { get; }
    public long WorkingSetBytes { get; }
    public long MemorySafetyReserveBytes { get; }
    public long StorageSafetyReserveBytes { get; }
    public int MaximumImageDimension { get; }
    public int ContextTokens { get; }
    public int MaximumOutputTokens { get; }
    public IReadOnlyList<OfflineModelArtifact> Artifacts { get; }
    public IReadOnlyList<string> RequiredCpuFeatures { get; }
    public OfflineAiAcceleration RequiredAcceleration { get; }

    // Installation may need the completed files and same-sized partial files to coexist
    // until checksum verification and atomic placement finish.
    public long RequiredFreeStorageBytes => checked(
        Artifacts.Sum(artifact => artifact.DownloadBytes + artifact.InstalledBytes) + StorageSafetyReserveBytes);

    public long RequiredAvailableMemoryBytes =>
        Math.Max(MinimumAvailableMemoryBytes, checked(WorkingSetBytes + MemorySafetyReserveBytes));

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }
}

public sealed class OfflineModelCatalog
{
    public OfflineModelCatalog(string revision, IEnumerable<OfflineModelVariant> variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(variants);
        Revision = revision.Trim();
        Variants = Array.AsReadOnly(variants.ToArray());
        if (Variants.Count == 0) throw new ArgumentException("At least one model variant is required.", nameof(variants));
        if (Variants.Select(variant => variant.Id).Distinct(StringComparer.Ordinal).Count() != Variants.Count)
            throw new ArgumentException("Model IDs must be unique.", nameof(variants));
    }

    public string Revision { get; }
    public IReadOnlyList<OfflineModelVariant> Variants { get; }
}

public enum ModelInstallationState
{
    Missing,
    Partial,
    Invalid,
    Ready,
}

public sealed record ModelInstallationStatus(string ModelId, ModelInstallationState State,
    int VerifiedArtifactCount, long VerifiedBytes, string Detail);

public enum ModelInstallPhase
{
    Checking,
    Downloading,
    Verifying,
    Installing,
    Ready,
}

public sealed record ModelInstallProgress(ModelInstallPhase Phase, long CompletedBytes,
    long TotalBytes, string Message);

public sealed record ModelInstallRequest(OfflineModelVariant Variant, bool UserConsented);

public interface IOfflineModelInstaller
{
    Task<ModelInstallationStatus> GetStatusAsync(OfflineModelVariant variant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Implementations resume into app-private partial files, verify every manifest SHA-256,
    /// then atomically place the complete artifact set. They must never expose partial files as ready.
    /// </summary>
    Task InstallAsync(ModelInstallRequest request, IProgress<ModelInstallProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IRenderedStageImage : IAsyncDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    string ContentType { get; }
    ReadOnlyMemory<byte> EncodedBytes { get; }
}

public interface IStageDescriptionImageSource
{
    Task<IRenderedStageImage> RenderAsync(TattooStage stage, int maximumDimension,
        CancellationToken cancellationToken);
}

public sealed record StageDescriptionRequest(TattooStage Stage, IRenderedStageImage Image,
    string? SourceModelObservation, int ContextTokens, int MaximumOutputTokens);

public interface IOfflineVisionSession : IAsyncDisposable
{
    Task<string> DescribeAsync(StageDescriptionRequest request, CancellationToken cancellationToken);
}

public interface IOfflineVisionSessionFactory
{
    Task<IOfflineVisionSession> OpenAsync(OfflineModelVariant variant,
        CancellationToken cancellationToken);
}

/// <summary>
/// Produces a fresh snapshot from Android system APIs. Preloading calls this repeatedly
/// so an earlier admission decision cannot outlive current low-memory pressure.
/// </summary>
public interface IOfflineAiCapabilityProbe
{
    ValueTask<OfflineAiDeviceCapabilities> CaptureAsync(CancellationToken cancellationToken);
}

public sealed record OfflineDescriptionProgress(int Completed, int Total, TattooStage Stage)
{
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}

public readonly record struct OfflineDescriptionCacheKey(
    string SourceFingerprint,
    string ModelId,
    string ModelRevision,
    string PromptRevision,
    string AnatomyStateKey)
{
    public static OfflineDescriptionCacheKey Create(string sourceFingerprint,
        OfflineModelVariant variant, OfflineModelCatalog catalog, string promptRevision,
        AnatomicalWorkflowState anatomy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptRevision);
        ArgumentNullException.ThrowIfNull(anatomy);
        return new(sourceFingerprint, variant.Id, catalog.Revision, promptRevision,
            anatomy.DescriptionCacheKey);
    }
}
