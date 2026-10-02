#if TATAPP_ENABLE_OFFLINE_AI_SMOKE
using Android.App;
using Android.Graphics;
using Android.OS;
using TATAPP.Core;
using TATAPP.Core.OfflineAI;

namespace TATAPP.AndroidApp;

/// <summary>
/// Opt-in emulator smoke entry point. It is compiled only when
/// TatappEnableOfflineAiSmoke=true and is absent from distributable builds.
/// </summary>
[Activity(Name = "com.grayscaleconsultants.tatapp.OfflineAiSmokeActivity",
    Label = "TATAPP offline AI smoke", Exported = true)]
internal sealed class OfflineAiSmokeActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = RunSmokeAsync();
    }

    private async Task RunSmokeAsync()
    {
        var privateReport = System.IO.Path.Combine(FilesDir?.AbsolutePath
            ?? throw new IOException("App-private storage is unavailable."), "offline-ai-smoke.txt");
        var externalDirectory = GetExternalFilesDir(null)?.AbsolutePath;
        try
        {
            var installer = new AndroidOfflineModelInstaller(this);
            var factory = new LlamaSharpVisionSessionFactory(installer);
            using var service = new OfflineAiService(this, installer,
                new AndroidOfflineAiCapabilityProbe(this), factory);
            var variant = service.Catalog.Variants.Single();
            var status = await installer.GetStatusAsync(variant, CancellationToken.None).ConfigureAwait(false);
            if (status.State != ModelInstallationState.Ready)
                throw new InvalidDataException($"Model status is {status.State}: {status.Detail}");

            await using var session = await factory.OpenAsync(variant, CancellationToken.None).ConfigureAwait(false);
            string firstDescription;
            await using (var image = CreateFixture(inverted: false))
            {
                firstDescription = await session.DescribeAsync(new StageDescriptionRequest(
                    TattooStageCatalog.All[0], image, variant.ContextTokens, 32),
                    CancellationToken.None).ConfigureAwait(false);
            }
            string secondDescription;
            await using (var image = CreateFixture(inverted: true))
            {
                secondDescription = await session.DescribeAsync(new StageDescriptionRequest(
                    TattooStageCatalog.All[0], image, variant.ContextTokens, 32),
                    CancellationToken.None).ConfigureAwait(false);
            }
            var result = "FIRST: " + firstDescription + "\nSECOND: " + secondDescription;
            if (string.Equals(firstDescription, secondDescription, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The reused session did not independently describe both smoke images. " + result);
            result = "PASS\n" + result;
            await WriteReportsAsync(privateReport, externalDirectory, result).ConfigureAwait(false);
            global::Android.Util.Log.Info("TATAPP-AI-SMOKE", result[..Math.Min(result.Length, 1200)]);
        }
        catch (Exception exception)
        {
            var result = "FAIL\n" + exception;
            await WriteReportsAsync(privateReport, externalDirectory, result).ConfigureAwait(false);
            global::Android.Util.Log.Error("TATAPP-AI-SMOKE", result[..Math.Min(result.Length, 2000)]);
        }
        finally
        {
            RunOnUiThread(Finish);
        }
    }

    private static async Task WriteReportsAsync(string privateReport, string? externalDirectory, string result)
    {
        await File.WriteAllTextAsync(privateReport, result).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(externalDirectory)) return;
        Directory.CreateDirectory(externalDirectory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(externalDirectory, "offline-ai-smoke.txt"), result)
            .ConfigureAwait(false);
    }

    private static AndroidRenderedStageImage CreateFixture(bool inverted)
    {
        using var bitmap = Bitmap.CreateBitmap(192, 192, Bitmap.Config.Argb8888!)
            ?? throw new InvalidDataException("Could not allocate the smoke fixture.");
        using var canvas = new Canvas(bitmap);
        var background = inverted ? Color.Black : Color.White;
        var foreground = inverted ? Color.Red : Color.Black;
        canvas.DrawColor(background);
        using var ink = new Paint(PaintFlags.AntiAlias) { Color = foreground, StrokeWidth = 12 };
        if (inverted) canvas.DrawRect(40, 40, 152, 152, ink);
        else
        {
            canvas.DrawCircle(96, 96, 58, ink);
            using var center = new Paint(PaintFlags.AntiAlias) { Color = background };
            canvas.DrawCircle(96, 96, 34, center);
        }
        using var output = new MemoryStream();
        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, output))
            throw new IOException("Could not encode the smoke fixture.");
        return new AndroidRenderedStageImage(bitmap.Width, bitmap.Height, output.ToArray());
    }
}
#endif
