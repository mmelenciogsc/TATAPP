param(
    [string]$Photo = "",
    [string]$Screenshot = "",
    [string]$ContextScreenshot = "",
    [string]$SavedPlacement = "",
    [string]$FfmpegPath = "C:\ProgramData\ffmpeg20250324git\bin\ffmpeg.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class TATAPPSmokeWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
}
"@

$repository = Split-Path -Parent $PSScriptRoot
$application = Join-Path $repository "artifacts\publish\win-x64\TATAPP.exe"
if ([string]::IsNullOrWhiteSpace($Photo)) {
    $Photo = Join-Path $repository "artifacts\walkthrough\assets\wolf_head.png"
}
if ([string]::IsNullOrWhiteSpace($Screenshot)) {
    $Screenshot = Join-Path $repository "artifacts\walkthrough\runtime-anatomy.png"
}
if ([string]::IsNullOrWhiteSpace($ContextScreenshot)) {
    $ContextScreenshot = Join-Path $repository "artifacts\walkthrough\runtime-anatomy-context.png"
}
if ([string]::IsNullOrWhiteSpace($SavedPlacement)) {
    $SavedPlacement = Join-Path $repository "artifacts\walkthrough\saved\smoke-anatomy.png"
}
foreach ($required in @($application, $Photo)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required file is missing: $required" }
}
if (Test-Path -LiteralPath $SavedPlacement) { Remove-Item -LiteralPath $SavedPlacement -Force }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$process = Start-Process -FilePath $application -PassThru

function Find-AppElement([string]$automationId, [int]$attempts = 50) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $condition = New-Object System.Windows.Automation.AndCondition($processCondition, $idCondition)
    for ($attempt = 0; $attempt -lt $attempts; $attempt++) {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 200
    }
    throw "TATAPP element was not found: $automationId"
}

try {
    $viewport = Find-AppElement "AnatomyViewport"
    $male = Find-AppElement "MaleBodyRadioButton"
    $region = Find-AppElement "BodyRegionComboBox"
    $size = Find-AppElement "BodySizeSlider"
    $tone = Find-AppElement "SkinToneSlider"

    $maleState = $male.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
    if (-not $maleState) { throw "Male is not selected by default." }
    $selected = $region.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selected.Count -ne 1 -or -not $selected[0].Current.Name.StartsWith("Left upper arm", [StringComparison]::Ordinal)) {
        throw "The default left-upper-arm region is not selected."
    }
    if ($size.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 163) {
        throw "The default body size is not 163 centimeters."
    }
    if ($tone.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 55) {
        throw "The default complexion value is not 55."
    }

    $select = Find-AppElement "SelectPhotoButton"
    $select.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    [System.Windows.Forms.SendKeys]::SendWait("%n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($Photo)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Seconds 5

    $development = Find-AppElement "DevelopmentSlider"
    $development.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(100)
    $process.Refresh()
    [void][TATAPPSmokeWindow]::ShowWindowAsync($process.MainWindowHandle, 3)
    [void][TATAPPSmokeWindow]::SetForegroundWindow($process.MainWindowHandle)
    Start-Sleep -Milliseconds 250
    if (Test-Path -LiteralPath $FfmpegPath) {
        & $FfmpegPath -hide_banner -loglevel error -y -f gdigrab -draw_mouse 0 -i desktop -frames:v 1 $ContextScreenshot
        if ($LASTEXITCODE -ne 0) { throw "FFmpeg context screenshot failed with exit code $LASTEXITCODE." }
        Start-Sleep -Seconds 3
        & $FfmpegPath -hide_banner -loglevel error -y -f gdigrab -draw_mouse 0 -i desktop -frames:v 1 $Screenshot
        if ($LASTEXITCODE -ne 0) { throw "FFmpeg screenshot failed with exit code $LASTEXITCODE." }
    }
    $save = Find-AppElement "SaveButton"
    $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    [System.Windows.Forms.SendKeys]::SendWait("%n")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($SavedPlacement)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Seconds 4
    if (-not (Test-Path -LiteralPath $SavedPlacement)) { throw "Anatomical placement was not saved." }
    Write-Output "TATAPP UI smoke test passed. Anatomical context screenshot: $ContextScreenshot"
    Write-Output "Anatomical detail screenshot: $Screenshot"
    Write-Output "Saved anatomical placement: $SavedPlacement"
}
finally {
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    }
}
