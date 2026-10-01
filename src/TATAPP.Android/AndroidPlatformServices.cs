using Android.App;
using Android.Content;
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
    public void Open(Activity activity, Uri destination)
    {
        var intent = new Intent(Intent.ActionView, destination);
        intent.AddCategory(Intent.CategoryBrowsable);
        activity.StartActivity(intent);
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
    void Start();
    void Stop();
}
