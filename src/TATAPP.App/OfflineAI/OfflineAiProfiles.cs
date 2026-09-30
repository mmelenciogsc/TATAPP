using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TATAPP.App.OfflineAI;

public enum OfflineAiTier
{
    Compact,
    Balanced,
    Professional,
}

public sealed record OfflineAiModelProfile(
    OfflineAiTier Tier,
    string DisplayName,
    string Model,
    int MaximumImageDimension,
    int ContextLength,
    int MaximumOutputTokens,
    string KeepAlive,
    ulong MinimumAvailableMemoryBytes);

public sealed record OfflineAiHardwareSnapshot(
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    bool HasDiscreteGpu,
    ulong DedicatedVideoMemoryBytes,
    string GpuDescription)
{
    public string AccessibleSummary =>
        $"{FormatGiB(TotalMemoryBytes)} system memory, {FormatGiB(AvailableMemoryBytes)} currently available, " +
        (HasDiscreteGpu
            ? $"and {FormatGiB(DedicatedVideoMemoryBytes)} detected dedicated video memory"
            : "with no qualifying discrete graphics memory detected");

    private static string FormatGiB(ulong bytes) =>
        $"{bytes / 1_073_741_824d:0.0} gigabytes";
}

public static class OfflineAiProfileSelector
{
    private const ulong GiB = 1_073_741_824;

    public static OfflineAiModelProfile Select(OfflineAiHardwareSnapshot hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        // Vision models need substantially more working memory than the size of
        // their weight file. Keep a large reserve for Windows, JAWS, WPF and the
        // rendered image buffers; an optimistic tier selection can otherwise
        // make the entire desktop unstable rather than merely fail a request.
        if (hardware.TotalMemoryBytes >= 32 * GiB &&
            hardware.AvailableMemoryBytes >= 16 * GiB &&
            hardware.HasDiscreteGpu &&
            hardware.DedicatedVideoMemoryBytes >= 10 * GiB)
        {
            return new(OfflineAiTier.Professional, "Professional", "qwen3-vl:8b-instruct",
                1280, 3072, 448, "0", 16 * GiB);
        }

        if (hardware.TotalMemoryBytes >= 16 * GiB &&
            hardware.AvailableMemoryBytes >= 9 * GiB &&
            hardware.HasDiscreteGpu &&
            hardware.DedicatedVideoMemoryBytes >= 6 * GiB)
        {
            return new(OfflineAiTier.Balanced, "Balanced", "qwen3-vl:4b-instruct",
                1024, 2048, 384, "0", 9 * GiB);
        }

        // Qwen may occasionally need slightly more than 384 tokens to close a
        // response even under the concise-description instruction. A 512-token
        // ceiling prevents incomplete output without materially changing the
        // compact tier's memory footprint; the model is still unloaded after
        // every stage.
        return new(OfflineAiTier.Compact, "Compact", "qwen3-vl:2b-instruct",
            768, 1536, 512, "0", 4 * GiB);
    }

    public static bool HasSafeHeadroom(OfflineAiModelProfile profile, OfflineAiHardwareSnapshot hardware) =>
        hardware.AvailableMemoryBytes >= profile.MinimumAvailableMemoryBytes &&
        hardware.TotalMemoryBytes >= profile.MinimumAvailableMemoryBytes + 4 * GiB;

    public static void EnsureSafeHeadroom(OfflineAiModelProfile profile,
        OfflineAiHardwareSnapshot hardware, string operation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(hardware);
        if (HasSafeHeadroom(profile, hardware)) return;

        throw new InvalidOperationException(
            $"Offline AI Describe stopped before {operation} because only " +
            $"{hardware.AvailableMemoryBytes / (double)GiB:0.0} gigabytes of memory is available. " +
            $"The conservative minimum for {profile.Model} is " +
            $"{profile.MinimumAvailableMemoryBytes / (double)GiB:0.0} gigabytes. " +
            "At least four additional gigabytes of total system memory must remain reserved for Windows, the screenreader, and TATAPP. " +
            "Close memory-intensive applications, then try again. No partial description cache was kept.");
    }
}

public static class OfflineAiHardwareDetector
{
    private const ulong FallbackMemoryBytes = 4UL * 1_073_741_824;

    public static OfflineAiHardwareSnapshot Detect()
    {
        var (total, available) = DetectMemory();
        var gpuNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var detected = ReadRegistryGraphics(gpuNames);
        detected = Merge(detected, ReadNvidiaGraphics(gpuNames));
        var description = gpuNames.Count == 0
            ? "No graphics adapter details detected"
            : string.Join("; ", gpuNames.Order(StringComparer.OrdinalIgnoreCase));
        return new(total, available, detected.IsDiscrete, detected.MemoryBytes, description);
    }

    public static (ulong Total, ulong Available) DetectMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
            return (status.TotalPhysical, status.AvailablePhysical);

        var fallback = (ulong)Math.Max(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, (long)FallbackMemoryBytes);
        return (fallback, fallback / 2);
    }

    private static GraphicsMemory ReadRegistryGraphics(HashSet<string> names)
    {
        var result = new GraphicsMemory(false, 0);
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null) return result;
            foreach (var adapterName in video.GetSubKeyNames())
            {
                using var adapter = video.OpenSubKey(adapterName);
                if (adapter is null) continue;
                foreach (var instanceName in adapter.GetSubKeyNames())
                {
                    using var instance = adapter.OpenSubKey(instanceName);
                    var name = Convert.ToString(instance?.GetValue("DriverDesc"), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    names.Add(name);
                    if (!IsProbablyDiscrete(name)) continue;
                    var memory = ConvertMemory(instance?.GetValue("HardwareInformation.qwMemorySize"));
                    if (memory == 0)
                        memory = ConvertMemory(instance?.GetValue("HardwareInformation.MemorySize"));
                    result = Merge(result, new GraphicsMemory(true, memory));
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Hardware detection is advisory. A conservative profile is selected if Windows withholds details.
        }
        return result;
    }

    private static GraphicsMemory ReadNvidiaGraphics(HashSet<string> names)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return new(false, 0);
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return new(false, 0);
            }
            if (process.ExitCode != 0) return new(false, 0);

            ulong largest = 0;
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var comma = line.LastIndexOf(',');
                if (comma <= 0) continue;
                var name = line[..comma].Trim();
                if (name.Length > 0) names.Add(name);
                if (ulong.TryParse(line[(comma + 1)..].Trim(), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var mebibytes))
                    largest = Math.Max(largest, mebibytes * 1024 * 1024);
            }
            return new(largest > 0, largest);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
                                             or IOException or UnauthorizedAccessException)
        {
            return new(false, 0);
        }
    }

    private static bool IsProbablyDiscrete(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("nvidia", StringComparison.Ordinal) ||
               value.Contains("radeon rx", StringComparison.Ordinal) ||
               value.Contains("radeon pro", StringComparison.Ordinal) ||
               value.Contains("intel arc", StringComparison.Ordinal);
    }

    private static ulong ConvertMemory(object? value)
    {
        try
        {
            return value switch
            {
                byte[] bytes when bytes.Length >= sizeof(ulong) => BitConverter.ToUInt64(bytes),
                int number => unchecked((uint)number),
                long number => unchecked((ulong)number),
                not null => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
                _ => 0,
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static GraphicsMemory Merge(GraphicsMemory left, GraphicsMemory right) =>
        new(left.IsDiscrete || right.IsDiscrete, Math.Max(left.MemoryBytes, right.MemoryBytes));

    private readonly record struct GraphicsMemory(bool IsDiscrete, ulong MemoryBytes);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
