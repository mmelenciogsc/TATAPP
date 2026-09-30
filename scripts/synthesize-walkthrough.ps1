param(
    [string]$OutputDirectory = "",
    [string]$PiperDirectory = "$env:LOCALAPPDATA\TATAPP\WalkthroughRuntime\piper"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repository "artifacts\walkthrough"
}
$scriptPath = Join-Path $OutputDirectory "narration-script.json"
$narrationDirectory = Join-Path $OutputDirectory "narration"
$executable = Join-Path $PiperDirectory "piper.exe"
$model = Join-Path $PiperDirectory "en_US-lessac-medium.onnx"
$configuration = $model + ".json"

foreach ($required in @($scriptPath, $executable, $model, $configuration)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required narration file is missing: $required" }
}
New-Item -ItemType Directory -Force -Path $narrationDirectory | Out-Null
$stagingDirectory = Join-Path $narrationDirectory (".staging-" + $PID)
New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null

$cues = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8 | ConvertFrom-Json
try {
    foreach ($cue in $cues) {
        $output = Join-Path $stagingDirectory ($cue.id + ".wav")
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $executable
        $start.WorkingDirectory = $PiperDirectory
        $start.Arguments = '--model "' + $model + '" --config "' + $configuration +
            '" --output_file "' + $output + '" --length_scale 1.030928 --sentence_silence 0.20'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($start)
        if ($null -eq $process) { throw "Piper did not start for $($cue.id)." }
        $process.StandardInput.WriteLine([string]$cue.text)
        $process.StandardInput.Close()
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        if (-not $process.WaitForExit(180000)) {
            $process.Kill()
            throw "Piper timed out for $($cue.id)."
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) {
            throw "Piper failed for $($cue.id): $standardError $standardOutput"
        }
        Write-Output "Synthesized $($cue.id): $output"
    }

    foreach ($cue in $cues) {
        $staged = Join-Path $stagingDirectory ($cue.id + ".wav")
        $destination = Join-Path $narrationDirectory ($cue.id + ".wav")
        Move-Item -LiteralPath $staged -Destination $destination -Force
    }
    (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant() |
        Set-Content -LiteralPath (Join-Path $narrationDirectory ".script.sha256") -Encoding ASCII
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
