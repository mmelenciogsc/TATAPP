param(
    [string]$OutputDirectory = "",
    [string]$FfmpegPath = "C:\ProgramData\ffmpeg20250324git\bin\ffmpeg.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class TATAPPOfflineRecordingWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    public static void TapControl() {
        const byte control = 0x11;
        const uint keyUp = 0x0002;
        keybd_event(control, 0, 0, UIntPtr.Zero);
        keybd_event(control, 0, keyUp, UIntPtr.Zero);
    }
}
"@

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository "artifacts\walkthrough"
}
$application = Join-Path $repository "artifacts\publish\win-x64\TATAPP.exe"
$wolf = Join-Path $OutputDirectory "assets\wolf_head.png"
$rawCapture = Join-Path $OutputDirectory "offline-jaws-capture.mkv"
$timelinePath = Join-Path $OutputDirectory "offline-jaws-timeline.json"

foreach ($required in @($application, $FfmpegPath, $wolf)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required walkthrough file is missing: $required" }
}
if (-not (Get-Process jfw -ErrorAction SilentlyContinue)) {
    throw "JAWS for Windows is not running. Start JAWS before recording the walkthrough."
}
if (Test-Path -LiteralPath $rawCapture) {
    Move-Item -LiteralPath $rawCapture -Destination ($rawCapture + ".previous-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}

$events = [System.Collections.Generic.List[object]]::new()
$clock = [System.Diagnostics.Stopwatch]::new()
$root = [System.Windows.Automation.AutomationElement]::RootElement
$appProcess = $null
$recorder = $null

function Mark([string]$id, [string]$description) {
    $events.Add([pscustomobject]@{
        id = $id
        seconds = [math]::Round($clock.Elapsed.TotalSeconds, 3)
        description = $description
    })
    Write-Output ("{0,8:0.000}  {1}: {2}" -f $clock.Elapsed.TotalSeconds, $id, $description)
}

function Find-AppElement([string]$automationId, [int]$attempts = 50) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    for ($index = 0; $index -lt $attempts; $index++) {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element -and $element.Current.ProcessId -eq $appProcess.Id) { return $element }
        Start-Sleep -Milliseconds 200
    }
    throw "TATAPP element was not found: $automationId"
}

function Invoke-AppButton([string]$automationId) {
    $element = Find-AppElement $automationId
    $element.SetFocus()
    Start-Sleep -Milliseconds 500
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Wait-ForStatus([string]$needle, [int]$timeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $status = Find-AppElement "StatusText" 5
        if ($status.Current.Name.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $status.Current.Name
        }
        Start-Sleep -Milliseconds 300
    }
    throw "TATAPP status did not contain '$needle' within $timeoutSeconds seconds."
}

function Choose-Wolf {
    Invoke-AppButton "SelectPhotoButton"
    Start-Sleep -Seconds 2
    [System.Windows.Forms.SendKeys]::SendWait("%n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($wolf)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    [void](Wait-ForStatus "wolf_head.png" 30)
}

function Start-Recorder {
    $availableKilobytes = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($availableKilobytes -lt 1572864) {
        throw "JAWS demo recording stopped because less than 1.5 GiB of memory is available."
    }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FfmpegPath
    $start.Arguments = '-hide_banner -loglevel error -threads 2 -filter_threads 1 -y -f gdigrab -framerate 30 -draw_mouse 1 -i desktop -thread_queue_size 32 -f dshow -i audio="virtual-audio-capturer" -c:v libx264 -threads 2 -preset ultrafast -crf 16 -pix_fmt yuv420p -c:a aac -b:a 192k "' + $rawCapture + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $script:recorder = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $script:recorder) { throw "FFmpeg screen recorder did not start." }
    Start-Sleep -Seconds 2
}

function Stop-Recorder {
    if ($null -eq $script:recorder -or $script:recorder.HasExited) { return }
    $script:recorder.StandardInput.WriteLine("q")
    if (-not $script:recorder.WaitForExit(20000)) { $script:recorder.Kill() }
    if ($script:recorder.ExitCode -ne 0) { throw "FFmpeg recorder exited with code $($script:recorder.ExitCode)." }
}

function Set-AiDescriptionStage([double]$value, [string]$marker, [string]$levelLabel) {
    $slider = Find-AppElement "DevelopmentSlider"
    Mark $marker ("JAWS begins the cached vivid AI description for " + $levelLabel + ".")
    # No focus move: the cached-description live region is the only speech.
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 750
    $description = Find-AppElement "OfflineAiDescriptionText" 10
    Mark ($marker + "-text") $description.Current.Name
    Start-Sleep -Seconds 18
    [TATAPPOfflineRecordingWindow]::TapControl()
    Start-Sleep -Milliseconds 750
    Mark ($marker + "-end") ("JAWS completed the cached vivid AI description for " + $levelLabel + ".")
}

try {
    $appProcess = Start-Process -FilePath $application -PassThru
    $window = $null
    for ($index = 0; $index -lt 50 -and $null -eq $window; $index++) {
        Start-Sleep -Milliseconds 200
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        $window = $windows | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
            $_.Current.Name.StartsWith("TATAPP", [StringComparison]::Ordinal)
        } | Select-Object -First 1
    }
    if ($null -eq $window) { throw "The TATAPP main window was not found." }
    [void][TATAPPOfflineRecordingWindow]::ShowWindowAsync([IntPtr]$window.Current.NativeWindowHandle, 3)
    [void][TATAPPOfflineRecordingWindow]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    Start-Sleep -Seconds 1

    # Preload before starting FFmpeg so Qwen receives the maximum safe memory
    # headroom. Only the already-cached, instant JAWS demonstration is recorded.
    Choose-Wolf
    $offlineCheckBox = Find-AppElement "OfflineAIDescribeCheckBox"
    $offlineCheckBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $deadline = (Get-Date).AddMinutes(24)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $status = Find-AppElement "StatusText" 5
        if ($status.Current.Name.IndexOf("cached", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $ready = $true
            break
        }
        $toggle = $offlineCheckBox.GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
        if ($toggle -eq [System.Windows.Automation.ToggleState]::Off) {
            throw "Offline AI Describe stopped before the cache was complete. $($status.Current.Name)"
        }
    }
    if (-not $ready) { throw "Offline AI description preparation exceeded twenty-four minutes." }

    Start-Sleep -Seconds 6
    $slider = Find-AppElement "DevelopmentSlider"
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(51)
    Start-Sleep -Milliseconds 750
    [TATAPPOfflineRecordingWindow]::TapControl()
    Start-Sleep -Milliseconds 750

    Start-Recorder
    $clock.Start()
    Mark "capture-start" "JAWS-only cached description capture started."
    Start-Sleep -Seconds 1
    Set-AiDescriptionStage 0 "ai-level-1-description" "level 1, Original image"
    Set-AiDescriptionStage 51 "ai-level-8-description" "level 8, Fine outline"
    Set-AiDescriptionStage 59 "ai-level-9-description" "level 9, Placement fine outline"
    Set-AiDescriptionStage 100 "ai-level-16-description" "level 16, Placement full color"
    Mark "capture-end" "Four cached wolf descriptions completed."
    Start-Sleep -Seconds 1
}
finally {
    $clock.Stop()
    try { Stop-Recorder } catch { Write-Error $_ }
    $events | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $timelinePath -Encoding UTF8
    if ($null -ne $appProcess -and -not $appProcess.HasExited) {
        [void]$appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(3000)) { $appProcess.Kill() }
    }
}

Write-Output "Offline JAWS capture: $rawCapture"
Write-Output "Offline JAWS timeline: $timelinePath"
