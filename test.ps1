param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "dist")
)

# Builds the headless checks into dist\OknaTests.exe and runs them.
# The test assembly compiles the real sources (minus the GUI entry point),
# so it exercises exactly the code that ships.
$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

$src = Join-Path $PSScriptRoot "src"
$tests = Join-Path $PSScriptRoot "tests"
$wpf = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$files = Get-ChildItem -Path $src -Filter *.cs |
    Where-Object { $_.Name -ne "Program.cs" } |
    ForEach-Object { $_.FullName }
$files += Join-Path $tests "Tests.cs"

$arguments = @(
    "/nologo",
    "/target:exe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/unsafe",
    "/codepage:65001",
    "/out:$OutputDir\OknaTests.exe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Web.Extensions.dll",
    "/r:$wpf\UIAutomationClient.dll",
    "/r:$wpf\UIAutomationTypes.dll",
    "/r:$wpf\WindowsBase.dll"
) + $files

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed with exit code $LASTEXITCODE"
}

& "$OutputDir\OknaTests.exe"
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}
