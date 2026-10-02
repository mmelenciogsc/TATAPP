using Android.App;
using Android.App.Roles;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;

namespace TATAPP.AndroidApp;

internal interface IAndroidMediaLauncher
{
    void LaunchPhotoPicker(Activity activity, int requestCode);
    void LaunchCamera(Activity activity, Uri output, int requestCode);
}

internal sealed class AndroidMediaLauncher : IAndroidMediaLauncher
{
    public void LaunchPhotoPicker(Activity activity, int requestCode)
    {
        var action = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? "android.provider.action.PICK_IMAGES"
            : Intent.ActionOpenDocument;
        var intent = new Intent(action);
        intent.SetType("image/*");
        if (action == Intent.ActionOpenDocument)
        {
            intent.AddCategory(Intent.CategoryOpenable);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        }
        activity.StartActivityForResult(intent, requestCode);
    }

    public void LaunchCamera(Activity activity, Uri output, int requestCode)
    {
        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, output);
        intent.ClipData = ClipData.NewRawUri("TATAPP camera output", output);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        activity.StartActivityForResult(intent, requestCode);
    }
}

internal interface IAndroidDocumentExporter
{
    void LaunchCreateDocument(Activity activity, string mimeType, string suggestedName,
        int requestCode);
}

internal sealed class AndroidDocumentExporter : IAndroidDocumentExporter
{
    public void LaunchCreateDocument(Activity activity, string mimeType, string suggestedName,
        int requestCode)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        activity.StartActivityForResult(intent, requestCode);
    }
}

internal interface IAndroidExternalLinkLauncher
{
    void Open(Activity activity, Uri destination);
}

internal sealed class AndroidExternalLinkLauncher : IAndroidExternalLinkLauncher
{
    private const string BrowserProbeUrl = "https://www.example.com/";

    public void Open(Activity activity, Uri destination)
    {
        var packageManager = activity.PackageManager ??
                             throw new ActivityNotFoundException("Android's package manager is unavailable.");
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var roleManager = activity.GetSystemService(global::Android.Content.Context.RoleService) as RoleManager;
            if (roleManager?.IsRoleAvailable(RoleManager.RoleBrowser) != true)
                throw new ActivityNotFoundException("The Android browser role is unavailable.");
        }

        var browserPackage = ResolveDefaultBrowserPackage(packageManager);
        if (browserPackage is null)
            throw new ActivityNotFoundException("No default web browser is configured.");

        var intent = new Intent(Intent.ActionView, destination);
        intent.AddCategory(Intent.CategoryBrowsable);
        intent.SetPackage(browserPackage);
        if (packageManager.ResolveActivity(intent, PackageInfoFlags.MatchDefaultOnly) is null)
            throw new ActivityNotFoundException("The default web browser cannot open this HTTPS address.");
        activity.StartActivity(intent);
    }

    private static string? ResolveDefaultBrowserPackage(PackageManager packageManager)
    {
        var probeUri = Uri.Parse(BrowserProbeUrl) ??
                       throw new InvalidOperationException("The browser probe URL is invalid.");
        using var browserProbe = new Intent(Intent.ActionView, probeUri);
        browserProbe.AddCategory(Intent.CategoryBrowsable);
        var resolvedBrowser = packageManager.ResolveActivity(
            browserProbe, PackageInfoFlags.MatchDefaultOnly)?.ActivityInfo;
        var packageName = resolvedBrowser?.PackageName;
        var activityName = resolvedBrowser?.Name;
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(activityName))
            return null;

        // ResolveActivity returns the system resolver when no default exists.
        // A resolver is not one of the real activities returned for this probe.
        foreach (var candidate in packageManager.QueryIntentActivities(
                     browserProbe, PackageInfoFlags.MatchDefaultOnly))
        {
            var candidateActivity = candidate.ActivityInfo;
            if (candidateActivity is not null &&
                string.Equals(candidateActivity.PackageName, packageName, StringComparison.Ordinal) &&
                string.Equals(candidateActivity.Name, activityName, StringComparison.Ordinal))
                return packageName;
        }

        return null;
    }
}

internal interface IAndroidAccessibilityAnnouncer
{
    void UpdateStatus(TextView statusView, string message);
}

internal sealed class AndroidAccessibilityAnnouncer : IAndroidAccessibilityAnnouncer
{
    public void UpdateStatus(TextView statusView, string message)
    {
        statusView.Text = message;
        statusView.ContentDescription = "Application status. " + message;
    }
}

internal interface IAndroidAudioFeedback : IDisposable
{
    bool Start();
    void Stop();
}
