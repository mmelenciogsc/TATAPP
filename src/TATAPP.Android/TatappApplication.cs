using Android.App;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace TATAPP.AndroidApp;

[Application]
public sealed class TatappApplication : Application
{
    public TatappApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    internal IServiceProvider Services { get; private set; } = null!;

    public override void OnCreate()
    {
        base.OnCreate();
        Services = new ServiceCollection()
            .AddSingleton<global::Android.Content.Context>(this)
            .AddSingleton<IAndroidImageService, AndroidImageService>()
            .AddSingleton<IAndroidMediaLauncher, AndroidMediaLauncher>()
            .AddSingleton<IAndroidDocumentExporter, AndroidDocumentExporter>()
            .AddSingleton<IAndroidExternalLinkLauncher, AndroidExternalLinkLauncher>()
            .AddSingleton<IAndroidAccessibilityAnnouncer, AndroidAccessibilityAnnouncer>()
            .AddTransient<IAndroidAudioFeedback, AndroidProcessingHeartbeat>()
            .AddTransient<OfflineAiService>(services =>
            {
                var context = services.GetRequiredService<global::Android.Content.Context>();
                var installer = new AndroidOfflineModelInstaller(context);
                var capabilityProbe = new AndroidOfflineAiCapabilityProbe(context);
                return new OfflineAiService(context, installer, capabilityProbe,
                    new LlamaSharpVisionSessionFactory(installer));
            })
            .BuildServiceProvider();
    }
}
