param(
    [string]$Exe = (Join-Path $PSScriptRoot "dist\Okna.exe"),
    [switch]$StopRunning
)

# Runs the real engine against its own window, drags the edges with the mouse
# and writes %LOCALAPPDATA%\Okna\self-test.txt. Needs an interactive desktop.
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { throw "Build first: .\build.ps1" }

$running = Get-Process Okna -ErrorAction SilentlyContinue
if ($running) {
    if (-not $StopRunning) {
        throw "Okna is running. Close it first, or pass -StopRunning."
    }
    Stop-Process -Name Okna -Force
    Start-Sleep -Milliseconds 800
}

$dir = Join-Path $env:TEMP "okna-self-test"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item $Exe (Join-Path $dir "Okna.exe") -Force

$process = Start-Process -FilePath (Join-Path $dir "Okna.exe") `
    -ArgumentList "--self-test" -WorkingDirectory $dir -Wait -PassThru

$report = Join-Path $env:LOCALAPPDATA "Okna\self-test.txt"
if (Test-Path $report) {
    Get-Content $report
} else {
    Write-Output "No report at $report"
}
exit $process.ExitCode
