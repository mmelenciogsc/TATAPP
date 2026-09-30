using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using TATAPP.Core;

namespace TATAPP.App.Camera;

internal sealed class WindowsCameraCaptureCoordinator
{
    private readonly HashSet<string> filesBeforeCapture = new(StringComparer.OrdinalIgnoreCase);
    private DateTime captureStartedUtc;

    public bool IsWaitingForCapture { get; private set; }
    public bool WindowWasDeactivated { get; private set; }

    public CameraLaunchResult BeginCapture()
    {
        filesBeforeCapture.Clear();
        foreach (var path in EnumerateCameraPhotos()) filesBeforeCapture.Add(path);
        captureStartedUtc = DateTime.UtcNow;
        WindowWasDeactivated = false;
        try
        {
            Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true });
            IsWaitingForCapture = true;
            return new CameraLaunchResult(true,
                "Windows Camera opened. Take and save a photo, then return to TATAPP. The new Camera Roll photo will load automatically.");
        }
        catch (Win32Exception)
        {
            IsWaitingForCapture = false;
            return new CameraLaunchResult(false,
                "Windows Camera could not be opened. Use Select photo to choose an image instead.");
        }
    }

    public void MarkWindowDeactivated()
    {
        if (IsWaitingForCapture) WindowWasDeactivated = true;
    }

    public string? CompleteCapture()
    {
        if (!IsWaitingForCapture || !WindowWasDeactivated) return null;
        IsWaitingForCapture = false;
        return EnumerateCameraPhotos()
            .Where(path => !filesBeforeCapture.Contains(path))
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.LastWriteTimeUtc >= captureStartedUtc.AddSeconds(-2))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateCameraPhotos()
    {
        foreach (var directory in CameraRollDirectories())
        {
            if (!Directory.Exists(directory)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (var file in files)
            {
                try { _ = PhotoFileFormats.FromPath(file); }
                catch (NotSupportedException) { continue; }
                yield return file;
            }
        }
    }

    private static HashSet<string> CameraRollDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(pictures)) directories.Add(Path.Combine(pictures, "Camera Roll"));
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root)) directories.Add(Path.Combine(root, "Pictures", "Camera Roll"));
        }
        return directories;
    }
}

internal sealed record CameraLaunchResult(bool Succeeded, string Message);
