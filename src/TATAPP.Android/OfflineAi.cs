using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using LLama;
using LLama.Common;
using LLama.Native;
using TATAPP.Core;
using TATAPP.Core.OfflineAI;

namespace TATAPP.AndroidApp;

internal sealed class AndroidOfflineAiCapabilityProbe(Context context) : IOfflineAiCapabilityProbe
{
    private readonly Context context = context.ApplicationContext ?? context;

    public ValueTask<OfflineAiDeviceCapabilities> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = (ActivityManager?)context.GetSystemService(Context.ActivityService);
        var memory = new ActivityManager.MemoryInfo();
        manager?.GetMemoryInfo(memory);
        var storage = new StatFs(context.FilesDir?.AbsolutePath
            ?? throw new IOException("App-private storage is unavailable."));
        return ValueTask.FromResult(new OfflineAiDeviceCapabilities(
            (int)Build.VERSION.SdkInt,
            Build.SupportedAbis ?? [],
            Math.Max(1, manager?.MemoryClass ?? 1) * 1024L * 1024L,
            Math.Max(1, manager?.LargeMemoryClass ?? 1) * 1024L * 1024L,
            Math.Max(0, memory.AvailMem),
            memory.LowMemory,
            Math.Max(0, storage.AvailableBytes),
            ReadCpuFeatures(),
            OfflineAiAcceleration.Cpu));
    }

    private static string[] ReadCpuFeatures()
    {
        try
        {
            return File.ReadLines("/proc/cpuinfo")
                .Where(line => line.StartsWith("Features", StringComparison.OrdinalIgnoreCase) ||
                               line.StartsWith("flags", StringComparison.OrdinalIgnoreCase))
                .SelectMany(line => line[(line.IndexOf(':') + 1)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}

internal sealed class AndroidOfflineModelInstaller : IOfflineModelInstaller, IDisposable
{
    private const int BufferSize = 256 * 1024;
    private readonly string modelRoot;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromHours(12) };

    public AndroidOfflineModelInstaller(Context context)
    {
        modelRoot = Path.Combine(context.ApplicationContext?.FilesDir?.AbsolutePath
            ?? context.FilesDir?.AbsolutePath
            ?? throw new IOException("App-private storage is unavailable."), "models");
        Directory.CreateDirectory(modelRoot);
    }

    public async Task<ModelInstallationStatus> GetStatusAsync(OfflineModelVariant variant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var destination = VariantDirectory(variant);
        ReconcileInterruptedSwap(destination);
        var verified = 0;
        long verifiedBytes = 0;
        var anyFinal = false;
        foreach (var artifact in variant.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(destination, artifact.FileName);
            if (!File.Exists(path)) continue;
            anyFinal = true;
            if (await VerifyAsync(path, artifact, cancellationToken).ConfigureAwait(false))
            {
                verified++;
                verifiedBytes += artifact.InstalledBytes;
            }
        }

        var staging = StagingDirectory(variant);
        var partial = Directory.Exists(staging) && Directory.EnumerateFiles(staging).Any();
        var state = verified == variant.Artifacts.Count
            ? ModelInstallationState.Ready
            : anyFinal ? ModelInstallationState.Invalid
            : partial ? ModelInstallationState.Partial : ModelInstallationState.Missing;
        return new(variant.Id, state, verified, verifiedBytes, state switch
        {
            ModelInstallationState.Ready => "Every model artifact is present and checksum-verified.",
            ModelInstallationState.Partial => "A resumable model download is present.",
            ModelInstallationState.Invalid => "The installed artifact set is incomplete or invalid.",
            _ => "The model has not been installed.",
        });
    }

    public async Task InstallAsync(ModelInstallRequest request, IProgress<ModelInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.UserConsented) throw new InvalidOperationException("Explicit model-install consent is required.");
        var variant = request.Variant;
        var total = variant.Artifacts.Sum(artifact => artifact.DownloadBytes);
        var staging = StagingDirectory(variant);
        ReconcileInterruptedSwap(VariantDirectory(variant));
        Directory.CreateDirectory(staging);
        RemoveUnknownStagingFiles(staging, variant);
        progress?.Report(new(ModelInstallPhase.Checking, 0, total, "Checking resumable model files."));

        long completedBefore = 0;
        foreach (var artifact in variant.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partial = Path.Combine(staging, artifact.FileName + ".partial");
            var complete = Path.Combine(staging, artifact.FileName);
            if (File.Exists(complete) && await VerifyAsync(complete, artifact, cancellationToken).ConfigureAwait(false))
            {
                completedBefore += artifact.DownloadBytes;
                continue;
            }
            TryDelete(complete);
            if (File.Exists(partial) && new FileInfo(partial).Length == artifact.DownloadBytes)
            {
                progress?.Report(new(ModelInstallPhase.Verifying, completedBefore + artifact.DownloadBytes,
                    total, $"Verifying completed resumable file {artifact.FileName}."));
                if (await VerifyAsync(partial, artifact, cancellationToken).ConfigureAwait(false))
                {
                    File.Move(partial, complete, true);
                    completedBefore += artifact.DownloadBytes;
                    continue;
                }
                TryDelete(partial);
            }
            await DownloadArtifactAsync(artifact, partial, completedBefore, total, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new(ModelInstallPhase.Verifying,
                completedBefore + artifact.DownloadBytes, total, $"Verifying {artifact.FileName}."));
            if (!await VerifyAsync(partial, artifact, cancellationToken).ConfigureAwait(false))
            {
                TryDelete(partial);
                throw new InvalidDataException($"The checksum for '{artifact.FileName}' did not match the tested manifest.");
            }
            File.Move(partial, complete, true);
            completedBefore += artifact.DownloadBytes;
        }

        var available = new StatFs(modelRoot).AvailableBytes;
        var installedBytes = variant.Artifacts.Sum(artifact => artifact.InstalledBytes);
        if (available < installedBytes + variant.StorageSafetyReserveBytes)
            throw new IOException("There is no longer enough free storage to atomically install the verified model set.");

        progress?.Report(new(ModelInstallPhase.Installing, total, total,
            "Atomically placing the verified model artifact set."));
        var destination = VariantDirectory(variant);
        var retired = destination + ".retired";
        TryDeleteDirectory(retired);
        if (Directory.Exists(destination)) Directory.Move(destination, retired);
        try
        {
            Directory.Move(staging, destination);
            TryDeleteDirectory(retired);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(retired)) Directory.Move(retired, destination);
            throw;
        }
        progress?.Report(new(ModelInstallPhase.Ready, total, total, "Offline model is verified and installed."));
    }

    public string ArtifactPath(OfflineModelVariant variant, string fileName) =>
        Path.Combine(VariantDirectory(variant), Path.GetFileName(fileName));

    public void Dispose() => httpClient.Dispose();

    private async Task DownloadArtifactAsync(OfflineModelArtifact artifact, string partial,
        long completedBefore, long total, IProgress<ModelInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existing > artifact.DownloadBytes)
        {
            TryDelete(partial);
            existing = 0;
        }
        var available = new StatFs(modelRoot).AvailableBytes;
        if (available < artifact.DownloadBytes - existing + 64L * 1024 * 1024)
            throw new IOException($"Insufficient app-private storage for '{artifact.FileName}'.");

        using var request = new HttpRequestMessage(HttpMethod.Get, artifact.DownloadUri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (existing > 0 && (response.StatusCode != HttpStatusCode.PartialContent ||
                             response.Content.Headers.ContentRange?.From != existing))
        {
            TryDelete(partial);
            existing = 0;
            using var restart = new HttpRequestMessage(HttpMethod.Get, artifact.DownloadUri);
            using var restarted = await httpClient.SendAsync(restart, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            restarted.EnsureSuccessStatusCode();
            await CopyResponseAsync(restarted, partial, FileMode.Create, artifact, completedBefore,
                total, progress, cancellationToken).ConfigureAwait(false);
            return;
        }
        response.EnsureSuccessStatusCode();
        await CopyResponseAsync(response, partial, existing == 0 ? FileMode.Create : FileMode.Append,
            artifact, completedBefore, total, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyResponseAsync(HttpResponseMessage response, string partial, FileMode mode,
        OfflineModelArtifact artifact, long completedBefore, long total,
        IProgress<ModelInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = mode == FileMode.Append && File.Exists(partial) ? new FileInfo(partial).Length : 0;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(partial, mode, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        var completed = existing;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            completed = checked(completed + read);
            if (completed > artifact.DownloadBytes)
                throw new InvalidDataException($"'{artifact.FileName}' exceeded its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new(ModelInstallPhase.Downloading, completedBefore + completed, total,
                $"Downloading {artifact.FileName}."));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (completed != artifact.DownloadBytes)
            throw new InvalidDataException($"'{artifact.FileName}' is incomplete and can be resumed.");
    }

    private string VariantDirectory(OfflineModelVariant variant) => Path.Combine(modelRoot, SafeId(variant.Id));
    private string StagingDirectory(OfflineModelVariant variant) => VariantDirectory(variant) + ".installing";

    private static void ReconcileInterruptedSwap(string destination)
    {
        var retired = destination + ".retired";
        if (!Directory.Exists(retired)) return;
        if (!Directory.Exists(destination))
            Directory.Move(retired, destination);
        else
            TryDeleteDirectory(retired);
    }

    private static void RemoveUnknownStagingFiles(string staging, OfflineModelVariant variant)
    {
        var expected = variant.Artifacts
            .SelectMany(artifact => new[] { artifact.FileName, artifact.FileName + ".partial" })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(staging))
            if (!expected.Contains(Path.GetFileName(path))) TryDelete(path);
        foreach (var directory in Directory.EnumerateDirectories(staging)) TryDeleteDirectory(directory);
    }

    private static string SafeId(string value)
    {
        var safe = new string(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        if (!string.Equals(value, safe, StringComparison.Ordinal) || safe.Length == 0)
            throw new InvalidDataException("The model ID cannot be used as an app-private directory name.");
        return safe;
    }

    private static async Task<bool> VerifyAsync(string path, OfflineModelArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != artifact.InstalledBytes) return false;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        return string.Equals(Convert.ToHexString(hash.GetHashAndReset()), artifact.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (Java.Lang.SecurityException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (Java.Lang.SecurityException) { }
    }
}

internal sealed class LlamaSharpVisionSessionFactory(AndroidOfflineModelInstaller installer)
    : IOfflineVisionSessionFactory
{
    public async Task<IOfflineVisionSession> OpenAsync(OfflineModelVariant variant,
        CancellationToken cancellationToken)
    {
        var text = variant.Artifacts.Single(artifact =>
            !artifact.FileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        var projector = variant.Artifacts.Single(artifact =>
            artifact.FileName.Contains("mmproj", StringComparison.OrdinalIgnoreCase));
        var parameters = new ModelParams(installer.ArtifactPath(variant, text.FileName))
        {
            ContextSize = (uint)Math.Min(variant.ContextTokens, 2048),
            BatchSize = 128,
            UBatchSize = 128,
            GpuLayerCount = 0,
            Threads = Math.Clamp(System.Environment.ProcessorCount - 1, 1, 4),
            BatchThreads = Math.Clamp(System.Environment.ProcessorCount - 1, 1, 4),
            UseMemorymap = true,
            UseMemoryLock = false,
        };
        var mtmdParameters = MtmdContextParams.Default();
        mtmdParameters.UseGpu = false;
        mtmdParameters.NThreads = Math.Clamp(System.Environment.ProcessorCount - 1, 1, 4);
        mtmdParameters.Warmup = false;
        mtmdParameters.ImageMinTokens = -1;
        mtmdParameters.ImageMaxTokens = 512;

        LLamaWeights? weights = null;
        LLamaContext? context = null;
        MtmdWeights? mtmd = null;
        try
        {
            weights = await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            context = weights.CreateContext(parameters);
            mtmd = await MtmdWeights.LoadFromFileAsync(installer.ArtifactPath(variant, projector.FileName),
                weights, mtmdParameters, cancellationToken).ConfigureAwait(false);
            if (!mtmd.SupportsVision) throw new InvalidDataException("The verified projection model does not support vision.");
            return new LlamaSharpVisionSession(weights, context, mtmd, mtmdParameters.MediaMarker);
        }
        catch
        {
            mtmd?.Dispose();
            context?.Dispose();
            weights?.Dispose();
            throw;
        }
    }
}

internal sealed class LlamaSharpVisionSession : IOfflineVisionSession
{
    private readonly LLamaWeights weights;
    private readonly LLamaContext context;
    private readonly MtmdWeights mtmd;
    private readonly string mediaMarker;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;

    public LlamaSharpVisionSession(LLamaWeights weights, LLamaContext context, MtmdWeights mtmd,
        string? mediaMarker)
    {
        this.weights = weights;
        this.context = context;
        this.mtmd = mtmd;
        this.mediaMarker = mediaMarker ?? NativeApi.MtmdDefaultMarker() ?? "<media>";
    }

    public async Task<string> DescribeAsync(StageDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            // InteractiveExecutor retains prompt/token bookkeeping that is not reset
            // by llama_memory_clear. A fresh executor prevents one stage from becoming
            // a continuation of the prior stage while retaining the costly model state.
            var executor = new InteractiveExecutor(context, mtmd);
            var output = new StringBuilder();
            try
            {
                var embed = mtmd.LoadMedia(request.Image.EncodedBytes.Span);
                executor.Embeds.Add(embed);
                var sourceContext = string.IsNullOrWhiteSpace(request.VerifiedSourceDescription)
                    ? string.Empty
                    : $" The source-stage description was: {request.VerifiedSourceDescription} Treat it only as context and never copy facts that the current rendered image does not support.";
                var placementScope = request.Stage.IsAnatomicalPlacement
                    ? "Describe anatomical placement only when it is directly visible in this rendered image."
                    : "This is a design-only stage, not an anatomical placement view; do not mention a body, body location, or anatomical placement.";
                var instruction = $"{mediaMarker}\nDescribe only objective visible facts in the rendered TATAPP stage '{request.Stage.Name}'. " +
                                  $"Explain visible line, tone, and texture. {placementScope} Distinguish uncertainty, avoid identity or medical claims, and do not infer unseen content. " +
                                  $"Return one short paragraph only, with no headings, lists, quotations, code blocks, or alternative answers.{sourceContext}";
                var template = new LLamaTemplate(weights, strict: true) { AddAssistant = true };
                template.Add("system", "You are an offline visual description assistant. Be concise, vivid, factual, and explicit about uncertainty.");
                template.Add("user", instruction);
                var prompt = LLamaTemplate.Encoding.GetString(template.Apply());
                await foreach (var text in executor.InferAsync(prompt,
                    new InferenceParams
                    {
                        MaxTokens = Math.Min(request.MaximumOutputTokens, 96),
                        AntiPrompts = ["```", "<|im_end|>", "<|endoftext|>"],
                    }, linked.Token))
                    output.Append(text);
            }
            finally
            {
                foreach (var pending in executor.Embeds) pending.Dispose();
                executor.Embeds.Clear();
                mtmd.ClearMedia();
                context.NativeHandle.MemoryClear();
            }
            var result = output.ToString().Trim();
            var fence = result.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) result = result[..fence].Trim();
            if (result.Length > 20 && result[^1] is not ('.' or '!' or '?'))
            {
                var lastSentence = result.LastIndexOfAny(['.', '!', '?']);
                if (lastSentence >= 20) result = result[..(lastSentence + 1)].Trim();
            }
            return string.IsNullOrWhiteSpace(result)
                ? throw new InvalidDataException("The local vision model returned an empty description.")
                : result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            mtmd.ClearMedia();
            context.NativeHandle.MemoryClear();
            mtmd.Dispose();
            context.Dispose();
            weights.Dispose();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
            lifetime.Dispose();
        }
    }
}

internal sealed class AndroidRenderedStageImage(int width, int height, byte[] bytes) : IRenderedStageImage
{
    private byte[] bytes = bytes;
    public int PixelWidth { get; } = width;
    public int PixelHeight { get; } = height;
    public string ContentType => "image/png";
    public ReadOnlyMemory<byte> EncodedBytes => bytes;

    public ValueTask DisposeAsync()
    {
        bytes = [];
        return ValueTask.CompletedTask;
    }
}

internal sealed class DelegatingStageDescriptionImageSource(
    Func<TattooStage, int, CancellationToken, Task<IRenderedStageImage>> render)
    : IStageDescriptionImageSource
{
    public Task<IRenderedStageImage> RenderAsync(TattooStage stage, int maximumDimension,
        CancellationToken cancellationToken) => render(stage, maximumDimension, cancellationToken);
}

internal sealed class OfflineAiService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly IOfflineModelInstaller installer;
    private readonly IOfflineAiCapabilityProbe capabilityProbe;
    private readonly IOfflineVisionSessionFactory sessionFactory;
    private readonly OfflineDescriptionCache descriptionCache = new(2 * 1024 * 1024);
    private readonly OfflineDescriptionPreloadCoordinator preloadCoordinator;
    private readonly OfflineModelProvisioner provisioner;

    public OfflineAiService(Context context, IOfflineModelInstaller installer,
        IOfflineAiCapabilityProbe capabilityProbe, IOfflineVisionSessionFactory sessionFactory)
    {
        this.installer = installer ?? throw new ArgumentNullException(nameof(installer));
        this.capabilityProbe = capabilityProbe ?? throw new ArgumentNullException(nameof(capabilityProbe));
        this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        provisioner = new(installer);
        preloadCoordinator = new(descriptionCache);
        Catalog = LoadCatalog(context);
    }

    public OfflineModelCatalog Catalog { get; }
    public ValueTask<OfflineAiDeviceCapabilities> CaptureCapabilitiesAsync(CancellationToken token) =>
        capabilityProbe.CaptureAsync(token);
    public Task<ModelInstallationStatus> GetStatusAsync(OfflineModelVariant variant, CancellationToken token) =>
        installer.GetStatusAsync(variant, token);
    public Task<ModelProvisionResult> EnsureReadyAsync(OfflineModelVariant variant, bool consented,
        IProgress<ModelInstallProgress>? progress, CancellationToken token) =>
        provisioner.EnsureReadyAsync(variant, consented, progress, token);
    public Task<OfflineDescriptionBatch> PreloadAsync(OfflineDescriptionCacheKey key,
        OfflineModelVariant variant, IStageDescriptionImageSource imageSource,
        IProgress<OfflineDescriptionProgress>? progress, CancellationToken token) =>
        preloadCoordinator.PreloadAsync(key, variant, imageSource, sessionFactory, capabilityProbe, progress, token);

    public void InvalidateSource(string sourceId) => descriptionCache.InvalidateSource(sourceId);

    public void Dispose()
    {
        preloadCoordinator.Dispose();
        descriptionCache.Dispose();
        if (installer is IDisposable disposableInstaller) disposableInstaller.Dispose();
    }

    private static OfflineModelCatalog LoadCatalog(Context context)
    {
        using var stream = context.Resources?.OpenRawResource(Resource.Raw.offline_ai_models)
            ?? throw new InvalidDataException("The offline AI model catalog is missing.");
        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidDataException("The offline AI model catalog is invalid.");
        if (dto.SchemaVersion != 1 || dto.Models is null)
            throw new InvalidDataException("The offline AI model catalog schema is unsupported.");
        return new OfflineModelCatalog(dto.Revision, dto.Models.Select(model => new OfflineModelVariant(
            model.Id, model.DisplayName, model.Quantization, model.QualityRank, model.Tested,
            model.MinimumApiLevel, model.SupportedAbis, model.MinimumMemoryClassBytes,
            model.MinimumAvailableMemoryBytes, model.WorkingSetBytes, model.MemorySafetyReserveBytes,
            model.StorageSafetyReserveBytes, model.MaximumImageDimension, model.ContextTokens,
            model.MaximumOutputTokens, model.Artifacts.Select(artifact => new OfflineModelArtifact(
                artifact.FileName, new Uri(artifact.DownloadUri, UriKind.Absolute), artifact.DownloadBytes,
                artifact.InstalledBytes, artifact.Sha256)), model.RequiredCpuFeatures,
            model.RequiredAcceleration)));
    }

    private sealed class CatalogDto
    {
        public int SchemaVersion { get; set; }
        public string Revision { get; set; } = string.Empty;
        public List<ModelDto>? Models { get; set; }
    }

    private sealed class ModelDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Quantization { get; set; } = string.Empty;
        public int QualityRank { get; set; }
        public bool Tested { get; set; }
        public int MinimumApiLevel { get; set; }
        public string[] SupportedAbis { get; set; } = [];
        public long MinimumMemoryClassBytes { get; set; }
        public long MinimumAvailableMemoryBytes { get; set; }
        public long WorkingSetBytes { get; set; }
        public long MemorySafetyReserveBytes { get; set; }
        public long StorageSafetyReserveBytes { get; set; }
        public int MaximumImageDimension { get; set; }
        public int ContextTokens { get; set; }
        public int MaximumOutputTokens { get; set; }
        public ArtifactDto[] Artifacts { get; set; } = [];
        public string[] RequiredCpuFeatures { get; set; } = [];
        public OfflineAiAcceleration RequiredAcceleration { get; set; }
    }

    private sealed class ArtifactDto
    {
        public string FileName { get; set; } = string.Empty;
        public string DownloadUri { get; set; } = string.Empty;
        public long DownloadBytes { get; set; }
        public long InstalledBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}

internal sealed class AndroidProcessingHeartbeat : IAndroidAudioFeedback
{
    private const double AndroidGain = ProcessingHeartbeatWaveform.MaximumCadenceGain;
    private const int MaximumStartAttempts = 2;
    private const int MaximumWarningCount = 4;
    private readonly object synchronization = new();
    private AudioTrack? audioTrack;
    private int warningsLogged;
    private bool disposed;

    public bool Start()
    {
        lock (synchronization)
        {
            if (disposed)
            {
                LogHeartbeatFailure("start_disposed", nameof(ObjectDisposedException));
                return false;
            }
            if (audioTrack?.PlayState == PlayState.Playing) return true;

            for (var attempt = 1; attempt <= MaximumStartAttempts; attempt++)
            {
                try
                {
                    ReleaseAudioTrack();
                    audioTrack = CreatePlayingAudioTrack();
                    return true;
                }
                catch (Exception exception) when (IsMemoryExhaustion(exception))
                {
                    LogHeartbeatFailure("start_memory_exhausted", exception.GetType().Name);
                    ReleaseAudioTrack();
                    return false;
                }
                catch (Exception exception) when (IsRecoverableAudioFailure(exception))
                {
                    LogHeartbeatFailure($"start_attempt_{attempt}", exception.GetType().Name);
                    ReleaseAudioTrack();
                }
            }
            return false;
        }
    }

    public void Stop()
    {
        lock (synchronization)
        {
            StopPlayback();
            ReleaseAudioTrack();
        }
    }

    public void Dispose()
    {
        lock (synchronization)
        {
            if (disposed) return;
            disposed = true;
            StopPlayback();
            ReleaseAudioTrack();
        }
    }

    private AudioTrack CreatePlayingAudioTrack()
    {
        var samples = ProcessingHeartbeatWaveform.CreateCadenceBuffer(AndroidGain);
        using var attributesBuilder = new AudioAttributes.Builder();
        _ = attributesBuilder.SetUsage(AudioUsageKind.AssistanceAccessibility);
        _ = attributesBuilder.SetContentType(AudioContentType.Sonification);
        using var attributes = attributesBuilder.Build() ??
                               throw new InvalidOperationException("Android audio attributes are unavailable.");
        using var formatBuilder = new AudioFormat.Builder();
        _ = formatBuilder.SetEncoding(Android.Media.Encoding.Pcm16bit);
        _ = formatBuilder.SetSampleRate(ProcessingHeartbeatWaveform.SampleRate);
        _ = formatBuilder.SetChannelMask(ChannelOut.Mono);
        using var format = formatBuilder.Build() ??
                           throw new InvalidOperationException("Android heartbeat audio format is unavailable.");
        var track = new AudioTrack(attributes, format, samples.Length * sizeof(short),
            AudioTrackMode.Static, AudioManager.AudioSessionIdGenerate);
        try
        {
            // MODE_STATIC normally reports NoStaticData until its first complete write.
            if (track.State == AudioTrackState.Uninitialized)
                throw new InvalidOperationException("Android could not initialize heartbeat audio.");
            var written = track.Write(samples, 0, samples.Length, WriteMode.Blocking);
            if (written != samples.Length)
                throw new InvalidOperationException("Android could not load the complete heartbeat cadence.");
            if (track.State != AudioTrackState.Initialized)
                throw new InvalidOperationException("Android did not initialize the written heartbeat audio.");
            if (track.SetLoopPoints(0, samples.Length, -1) != TrackStatus.Success)
                throw new InvalidOperationException("Android could not configure heartbeat looping.");
            if (track.SetPlaybackHeadPosition(0) != TrackStatus.Success)
                throw new InvalidOperationException("Android could not seek the heartbeat to its pre-roll.");
            track.Play();
            if (track.PlayState != PlayState.Playing)
                throw new InvalidOperationException("Android did not start heartbeat playback.");
            return track;
        }
        catch
        {
            ReleaseTrack(track);
            throw;
        }
    }

    private void StopPlayback()
    {
        if (audioTrack is null || audioTrack.State != AudioTrackState.Initialized) return;
        try
        {
            if (audioTrack.PlayState != PlayState.Stopped) audioTrack.Stop();
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                               Java.Lang.IllegalStateException)
        {
            LogHeartbeatFailure("stop", exception.GetType().Name);
        }
    }

    private void ReleaseAudioTrack()
    {
        var track = audioTrack;
        audioTrack = null;
        if (track is not null) ReleaseTrack(track);
    }

    private void ReleaseTrack(AudioTrack track)
    {
        try { track.Release(); }
        catch (Java.Lang.IllegalStateException exception)
        {
            LogHeartbeatFailure("release", exception.GetType().Name);
        }
        finally { track.Dispose(); }
    }

    private static bool IsMemoryExhaustion(Exception exception) => exception is
        OutOfMemoryException or Java.Lang.OutOfMemoryError;

    private static bool IsRecoverableAudioFailure(Exception exception) => exception is
        InvalidOperationException or Java.Lang.IllegalArgumentException or Java.Lang.IllegalStateException;

    private void LogHeartbeatFailure(string phase, string exceptionType)
    {
        if (warningsLogged >= MaximumWarningCount) return;
        warningsLogged++;
        global::Android.Util.Log.Warn("TATAPP.Audio",
            $"Heartbeat phase={phase} exception={exceptionType}");
    }
}
