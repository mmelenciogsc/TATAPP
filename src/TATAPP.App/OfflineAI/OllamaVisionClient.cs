using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using TATAPP.Core;

namespace TATAPP.App.OfflineAI;

public sealed record ModelPullProgress(string Message, int Percentage, long CompletedBytes, long TotalBytes);

public sealed record OllamaRuntimeStatus(
    bool IsReachable,
    string Version,
    bool IsVersionSupported,
    IReadOnlyList<string> Models,
    string Error)
{
    public bool HasModel(string model) => Models.Any(installed =>
        string.Equals(installed, model, StringComparison.OrdinalIgnoreCase));
}

public sealed class OllamaVisionClient : IDisposable
{
    public const string MinimumVersion = "0.12.7";
    private static readonly Uri LocalEndpoint = new("http://127.0.0.1:11434/");
    private readonly HttpClient client;

    public OllamaVisionClient()
        : this(new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
    {
    }

    public OllamaVisionClient(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        client = new HttpClient(handler)
        {
            BaseAddress = LocalEndpoint,
            Timeout = TimeSpan.FromHours(2),
        };
    }

    public async Task<OllamaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var versionResponse = await client.GetAsync("api/version", cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(versionResponse, cancellationToken).ConfigureAwait(false);
            using var versionJson = JsonDocument.Parse(
                await versionResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var version = GetString(versionJson.RootElement, "version");

            using var modelsResponse = await client.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(modelsResponse, cancellationToken).ConfigureAwait(false);
            using var modelsJson = JsonDocument.Parse(
                await modelsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var models = new List<string>();
            if (modelsJson.RootElement.TryGetProperty("models", out var modelArray) &&
                modelArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelArray.EnumerateArray())
                {
                    var name = GetString(model, "name");
                    if (!string.IsNullOrWhiteSpace(name)) models.Add(name);
                }
            }

            return new(true, version, CompareVersions(version, MinimumVersion) >= 0, models, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                                             or InvalidOperationException or TaskCanceledException)
        {
            return new(false, string.Empty, false, [], FriendlyConnectionError(exception));
        }
    }

    public async Task<string> DescribeStageAsync(string pngBase64, TattooStage stage, double sliderValue,
        OfflineAiModelProfile profile, CancellationToken cancellationToken = default) =>
        await DescribeStageAsync(pngBase64, stage, sliderValue, profile, null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<string> DescribeStageAsync(string pngBase64, TattooStage stage, double sliderValue,
        OfflineAiModelProfile profile, string? sourceDesignContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngBase64);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(profile);

        var request = new
        {
            model = profile.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = BuildStagePrompt(stage, sliderValue, sourceDesignContext),
                    images = new[] { pngBase64 },
                },
            },
            stream = false,
            think = false,
            keep_alive = profile.KeepAlive,
            options = new
            {
                temperature = 0.1,
                num_ctx = profile.ContextLength,
                num_predict = profile.MaximumOutputTokens,
            },
        };
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("api/chat", content, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        if (!json.RootElement.TryGetProperty("message", out var message))
            throw new InvalidDataException("Ollama returned no description message.");
        var description = GetString(message, "content");
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidDataException("Ollama returned an empty stage description.");
        var done = !json.RootElement.TryGetProperty("done", out var doneValue) || doneValue.GetBoolean();
        var doneReason = GetString(json.RootElement, "done_reason");
        if (!done)
            throw new InvalidDataException("The local model stopped before completing a stage description.");
        return NormalizeDescription(description, requireCompleteSentence: IsLengthReason(doneReason));
    }

    public async Task<bool> TryUnloadModelAsync(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var content = new StringContent(
                JsonSerializer.Serialize(new { model, keep_alive = 0 }), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("api/generate", content, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                             or InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task PullModelAsync(string model, IProgress<ModelPullProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        using var content = new StringContent(JsonSerializer.Serialize(new { model, stream = true }),
            Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull") { Content = content };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var lastPercentage = -1;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var update = JsonDocument.Parse(line);
            var error = GetString(update.RootElement, "error");
            if (error.Length > 0) throw new InvalidOperationException($"Ollama model download failed: {error}");
            var state = GetString(update.RootElement, "status");
            var completed = GetInt64(update.RootElement, "completed");
            var total = GetInt64(update.RootElement, "total");
            var percentage = total > 0 ? Math.Clamp((int)(completed * 100L / total), 0, 100) : -1;
            if (string.Equals(state, "success", StringComparison.OrdinalIgnoreCase)) percentage = 100;
            if (percentage >= 0) lastPercentage = percentage;
            else if (lastPercentage >= 0) percentage = lastPercentage;
            var message = string.IsNullOrWhiteSpace(state) ? "Downloading model" : state;
            progress?.Report(new(message, percentage, completed, total));
        }
    }

    public static string BuildStagePrompt(TattooStage stage, double sliderValue,
        string? sourceDesignContext = null) =>
        "You are the local visual description component of TATAPP, assisting a totally blind or severely visually impaired tattoo lover or tattoo artist. " +
        $"The attached image is the actual rendered preview at slider {sliderValue:0} percent, in the stage named '{stage.Name}'. " +
        $"The deterministic renderer defines this stage as: {stage.Description} " +
        (string.IsNullOrWhiteSpace(sourceDesignContext)
            ? string.Empty
            : $"The original full-color source was already verified as follows: {sourceDesignContext} Keep the subject identity and distinctive accessories consistent with that verified source unless the current pixels definitively contradict it. ") +
        "Describe only what is visibly supported by this image. Use exactly two compact, vivid sentences totaling no more than 55 words. Identify the main subject or design, " +
        "its composition and approximate position, the most important shapes and contours, the visible contrast and retained or lost detail, " +
        (stage.IsAnatomicalPlacement
            ? "and how the visibly rendered ink follows the model surface. The application separately announces the exact selected body-region name, sex, size, complexion, and rotation, so do not infer or repeat those attributes. "
            : "and what this particular rendering would give a tattoo artist or stencil printer. ") +
        "Explicitly mention any part whose boundary becomes unclear or disappears. " +
        "Do not guess identities, hidden content, color when the rendered image is monochrome, intent, emotion, or details not visible. " +
        "Do not use Markdown, headings, bullet points, greetings, or phrases such as 'the image shows'. Return only the description.";

    public static int CompareVersions(string left, string right)
    {
        if (!Version.TryParse(NormalizeVersion(left), out var first)) return -1;
        if (!Version.TryParse(NormalizeVersion(right), out var second)) return 1;
        return first.CompareTo(second);
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "0.0";
        var normalized = value.Trim().TrimStart('v', 'V');
        var separator = normalized.IndexOfAny(['-', '+']);
        return separator >= 0 ? normalized[..separator] : normalized;
    }

    private static string NormalizeDescription(string value, bool requireCompleteSentence = false)
    {
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var normalized = string.Join(' ', words).Trim('"');
        if (normalized.Length == 0) return normalized;

        // Small local VLMs occasionally continue past a complete response until
        // Ollama reaches num_predict. Preserve only complete, requested
        // sentences instead of accepting a mid-sentence truncation or failing
        // the entire sixteen-stage cache after otherwise useful output.
        var bounded = new List<string>();
        var wordCount = 0;
        var sentenceCount = 0;
        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (wordCount == 55) break;
            bounded.Add(word);
            wordCount++;
            if (word.TrimEnd('"', '\'', ')', ']', '}', '”', '’') is { Length: > 0 } token &&
                token[^1] is '.' or '!' or '?')
            {
                sentenceCount++;
                if (sentenceCount == 2) break;
            }
        }

        if (requireCompleteSentence && sentenceCount == 0)
            throw new InvalidDataException("The local model stopped before completing a stage description.");

        var result = string.Join(' ', bounded);
        if (sentenceCount > 0)
        {
            var lastTerminator = result.LastIndexOfAny(['.', '!', '?']);
            result = result[..(lastTerminator + 1)];
        }
        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = body;
        try
        {
            using var json = JsonDocument.Parse(body);
            detail = GetString(json.RootElement, "error");
        }
        catch (JsonException)
        {
        }
        if (string.IsNullOrWhiteSpace(detail)) detail = response.ReasonPhrase ?? "Unknown error";
        throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode}: {detail}");
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long GetInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static bool IsLengthReason(string value) =>
        value.Contains("length", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("max_tokens", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyConnectionError(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null) current = current.InnerException;
        return exception is HttpRequestException || current is System.Net.Sockets.SocketException
            ? "Ollama is not reachable at 127.0.0.1:11434. Install or start Ollama for Windows."
            : exception.Message;
    }

    public void Dispose() => client.Dispose();
}
