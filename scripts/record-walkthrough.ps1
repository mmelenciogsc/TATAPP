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
public static class TATAPPRecordingWindow {
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
$assets = Join-Path $OutputDirectory "assets"
$rawCapture = Join-Path $OutputDirectory "raw-capture.mkv"
$timelinePath = Join-Path $OutputDirectory "capture-timeline.json"
$savedDirectory = Join-Path $OutputDirectory "saved"
$savedImage = Join-Path $savedDirectory "sugar-skull-left-upper-arm-full-color.jpg"

foreach ($required in @(
    $application,
    $FfmpegPath,
    (Join-Path $assets "wolf_head.png"),
    (Join-Path $assets "clock_roses.jpg"),
    (Join-Path $assets "sugar_skull.jpg"))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required walkthrough file is missing: $required" }
}
if (-not (Get-Process jfw -ErrorAction SilentlyContinue)) {
    throw "JAWS for Windows is not running. Start JAWS before recording the walkthrough."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory, $savedDirectory | Out-Null
if (Test-Path -LiteralPath $rawCapture) {
    $backup = Join-Path $OutputDirectory ("raw-capture.previous-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".mkv")
    Move-Item -LiteralPath $rawCapture -Destination $backup
}
if (Test-Path -LiteralPath $savedImage) {
    $backup = Join-Path $savedDirectory ("sugar-skull-left-upper-arm-full-color.previous-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".jpg")
    Move-Item -LiteralPath $savedImage -Destination $backup
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

function AppCondition {
    return New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
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
    Start-Sleep -Milliseconds 900
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-Slider([double]$value, [string]$marker) {
    $slider = Find-AppElement "DevelopmentSlider"
    $slider.SetFocus()
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
    Mark $marker ("Slider moved to {0} percent." -f $value)
    Start-Sleep -Seconds 3
}

function Set-AiDescriptionStage([double]$value, [string]$marker, [string]$levelLabel) {
    # Deliberately leave keyboard focus off the slider. The only JAWS speech
    # generated for this walkthrough beat should be the cached AI live-region
    # description, not a slider name, value, or control hint.
    $slider = Find-AppElement "DevelopmentSlider"
    Mark $marker ("JAWS begins the cached vivid AI description for " + $levelLabel + ".")
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 750
    $description = Find-AppElement "OfflineAiDescriptionText" 10
    Mark ($marker + "-text") $description.Current.Name
    Start-Sleep -Seconds 18
    [TATAPPRecordingWindow]::TapControl()
    Start-Sleep -Milliseconds 750
    Mark ($marker + "-end") ("JAWS completed the cached vivid AI description for " + $levelLabel + ".")
}

function Select-BodyRegion([string]$namePrefix, [string]$marker) {
    $combo = Find-AppElement "BodyRegionComboBox"
    $combo.SetFocus()
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 500
    $item = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name.StartsWith($namePrefix, [StringComparison]::Ordinal)
        } | Select-Object -First 1
    if ($null -eq $item) { throw "Body region was not found: $namePrefix" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Mark $marker ("Selected body region: " + $item.Current.Name)
    Start-Sleep -Seconds 4
}

function Set-AnatomySlider([string]$automationId, [double]$value, [string]$marker) {
    $slider = Find-AppElement $automationId
    $slider.SetFocus()
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
    Mark $marker ("Set " + $automationId + " to " + $value)
    Start-Sleep -Seconds 4
}

function Select-BodySex([string]$automationId, [string]$label, [string]$marker) {
    $radio = Find-AppElement $automationId
    $radio.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait(" ")
    Mark $marker ("Selected " + $label + " anatomical model.")
    Start-Sleep -Seconds 4
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

function Choose-File([string]$path, [string]$marker) {
    Mark ($marker + "-dialog") "Select photo dialog opened."
    Invoke-AppButton "SelectPhotoButton"
    Start-Sleep -Seconds 2
    [System.Windows.Forms.SendKeys]::SendWait("%n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($path)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    [void](Wait-ForStatus (Split-Path -Leaf $path) 30)
    Mark ($marker + "-loaded") ("Loaded " + (Split-Path -Leaf $path) + ".")
    Start-Sleep -Seconds 5
}

function Save-CurrentImage([string]$path) {
    Mark "save-dialog" "Save current look dialog opened."
    Invoke-AppButton "SaveButton"
    Start-Sleep -Seconds 2
    [System.Windows.Forms.SendKeys]::SendWait("%n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($path)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    [void](Wait-ForStatus "Saved" 60)
    Mark "image-saved" "The full-color sugar skull placement was saved in its original JPEG format."
    Start-Sleep -Seconds 4
}

function Start-Recorder {
    $availableKilobytes = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($availableKilobytes -lt 1572864) {
        throw "Recording stopped before FFmpeg launch because less than 1.5 GiB of physical memory is available. Close memory-intensive applications and retry."
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
    if ($script:recorder.ExitCode -ne 0) { throw "FFmpeg screen recorder exited with code $($script:recorder.ExitCode)." }
}

try {
    $appProcess = Start-Process -FilePath $application -PassThru
    $window = $null
    for ($index = 0; $index -lt 50 -and $null -eq $window; $index++) {
        Start-Sleep -Milliseconds 200
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (AppCondition))
        $window = $windows | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and
            $_.Current.Name.StartsWith("TATAPP", [StringComparison]::Ordinal)
        } | Select-Object -First 1
    }
    if ($null -eq $window) { throw "The TATAPP main window was not found." }
    [void][TATAPPRecordingWindow]::ShowWindowAsync([IntPtr]$window.Current.NativeWindowHandle, 3)
    [void][TATAPPRecordingWindow]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    Start-Sleep -Seconds 2

    Start-Recorder
    $clock.Start()
    Mark "capture-start" "Maximized TATAPP window; JAWS is running."
    Start-Sleep -Seconds 4

    (Find-AppElement "TakePhotoButton").SetFocus()
    Mark "take-photo-focus" "Take photo receives keyboard focus."
    Start-Sleep -Seconds 3
    (Find-AppElement "SelectPhotoButton").SetFocus()
    Mark "select-photo-focus" "Select photo receives keyboard focus."
    Start-Sleep -Seconds 3

    Choose-File (Join-Path $assets "wolf_head.png") "wolf-head"
    Set-Slider 7 "wolf-color-fade"
    Set-Slider 14 "wolf-grayscale"
    Set-Slider 21 "wolf-binarized"
    Set-Slider 28 "wolf-line-art"
    Set-Slider 35 "wolf-thick-outline"
    Set-Slider 43 "wolf-medium-outline"
    Set-Slider 51 "wolf-fine-outline"
    Set-Slider 59 "wolf-placement-fine"
    Set-Slider 65 "wolf-placement-medium"
    Set-Slider 71 "wolf-placement-thick"
    Set-Slider 77 "wolf-placement-line-art"
    Set-Slider 83 "wolf-placement-black-white"
    Set-Slider 89 "wolf-placement-grayscale"
    Set-Slider 95 "wolf-placement-returning-color"
    Set-Slider 100 "wolf-placement-full-color"
    Set-Slider 0 "wolf-original-return"

    $offlineCheckBox = Find-AppElement "OfflineAIDescribeCheckBox"
    $offlineCheckBox.SetFocus()
    Mark "offline-ai-focus" "Offline AI Describe receives keyboard focus."
    Start-Sleep -Seconds 3
    $offlineCheckBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Mark "offline-ai-start" "Offline AI Describe enabled; all sixteen descriptions begin preloading."

    $deadline = (Get-Date).AddMinutes(24)
    $nextProgressReport = (Get-Date).AddSeconds(15)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $status = Find-AppElement "StatusText" 5
        if ($status.Current.Name.IndexOf("cached", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $ready = $true
            break
        }
        if ((Get-Date) -ge $nextProgressReport) {
            Mark "offline-ai-progress" $status.Current.Name
            $nextProgressReport = (Get-Date).AddSeconds(15)
        }
        $toggle = (Find-AppElement "OfflineAIDescribeCheckBox" 5).GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
        if ($toggle -eq [System.Windows.Automation.ToggleState]::Off) {
            throw "Offline AI Describe turned off before the cache was complete. $($status.Current.Name)"
        }
    }
    if (-not $ready) { throw "Offline AI description preparation exceeded twenty-four minutes." }
    Mark "offline-ai-ready" "All sixteen vivid descriptions are cached and the slider is unlocked."
    Start-Sleep -Seconds 8

    # Prime a different stage, stop its announcement, and return to Original so
    # level 1 receives a fresh live-region event after the automatic ready state.
    $slider = Find-AppElement "DevelopmentSlider"
    $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(51)
    Start-Sleep -Milliseconds 750
    [TATAPPRecordingWindow]::TapControl()
    Start-Sleep -Milliseconds 750
    Mark "ai-description-prime-complete" "JAWS is silent before the four-description demonstration."

    Set-AiDescriptionStage 0 "ai-level-1-description" "level 1, Original image"
    Set-AiDescriptionStage 51 "ai-level-8-description" "level 8, Fine outline"
    Set-AiDescriptionStage 59 "ai-level-9-description" "level 9, Placement fine outline"
    Set-AiDescriptionStage 100 "ai-level-16-description" "level 16, Placement full color"

    $offlineCheckBox = Find-AppElement "OfflineAIDescribeCheckBox"
    $offlineCheckBox.SetFocus()
    $offlineCheckBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Mark "offline-ai-off" "Offline AI Describe disabled after the accessibility demonstration."
    Start-Sleep -Seconds 3

    Choose-File (Join-Path $assets "clock_roses.jpg") "clock-roses"
    Set-Slider 0 "clock-original"
    Set-Slider 21 "clock-binarized"
    Set-Slider 28 "clock-line-art"
    Set-Slider 51 "clock-fine-outline"
    Set-Slider 59 "clock-placement-fine"
    Select-BodySex "FemaleBodyRadioButton" "female" "clock-female-model"
    Select-BodyRegion "Upper left chest" "clock-upper-left-chest"
    Set-AnatomySlider "BodySizeSlider" 168 "clock-body-size"
    Set-AnatomySlider "SkinToneSlider" 65 "clock-skin-tone"
    Invoke-AppButton "RotateRightButton"
    Mark "clock-rotate" "Rotated the anatomical model right by fifteen degrees."
    Start-Sleep -Seconds 4
    Invoke-AppButton "ZoomInButton"
    Mark "clock-zoom" "Magnified the selected placement for inspection."
    Start-Sleep -Seconds 4
    Set-Slider 100 "clock-placement-full-color"

    Choose-File (Join-Path $assets "sugar_skull.jpg") "sugar-skull"
    Set-Slider 0 "skull-original"
    Set-Slider 14 "skull-grayscale"
    Set-Slider 28 "skull-line-art"
    Set-Slider 51 "skull-fine-outline"
    Set-Slider 59 "skull-placement-fine"
    Select-BodySex "MaleBodyRadioButton" "male" "skull-male-model"
    Select-BodyRegion "Left upper arm" "skull-left-upper-arm"
    Set-AnatomySlider "BodySizeSlider" 163 "skull-default-size"
    Set-AnatomySlider "SkinToneSlider" 55 "skull-default-skin-tone"
    Set-Slider 100 "skull-placement-full-color"
    Save-CurrentImage $savedImage

    $blackWidow = Find-AppElement "BlackWidowTattooButton"
    $blackWidow.SetFocus()
    Mark "black-widow-focus" "BLACK WIDOW TATTOO receives keyboard focus."
    Start-Sleep -Seconds 6
    Mark "capture-end" "Walkthrough capture completed."
    Start-Sleep -Seconds 3
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

Write-Output "Raw walkthrough capture: $rawCapture"
Write-Output "Capture timeline: $timelinePath"
Write-Output "Saved demonstration image: $savedImage"
