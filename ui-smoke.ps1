param(
    [string]$Exe = (Join-Path $PSScriptRoot "dist\Okna.exe"),
    [switch]$StopRunning
)

# Opens the real settings window and clicks through the real tray menu.
# Uses an isolated data folder under the smoke temp directory.
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { throw "Build first: .\build.ps1" }

$running = Get-Process Okna -ErrorAction SilentlyContinue
if ($running -and $StopRunning) {
    Stop-Process -Name Okna -Force
    Start-Sleep -Milliseconds 800
}

$dir = Join-Path $env:TEMP "okna-ui-smoke"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item $Exe (Join-Path $dir "Okna.exe") -Force
$dataDir = Join-Path $dir "data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

$previousDataDir = $env:OKNA_UI_SMOKE_DIR
try {
    $env:OKNA_UI_SMOKE_DIR = $dataDir
    $process = Start-Process -FilePath (Join-Path $dir "Okna.exe") `
        -ArgumentList "--ui-smoke" -WorkingDirectory $dir -Wait -PassThru
} finally {
    if ($null -eq $previousDataDir) {
        Remove-Item Env:\OKNA_UI_SMOKE_DIR -ErrorAction SilentlyContinue
    } else {
        $env:OKNA_UI_SMOKE_DIR = $previousDataDir
    }
}

$report = Join-Path $dataDir "ui-smoke.txt"
if (Test-Path $report) {
    Get-Content $report
} else {
    Write-Output "No report at $report"
}
exit $process.ExitCode
