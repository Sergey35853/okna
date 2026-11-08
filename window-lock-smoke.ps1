param(
    [string]$OutputPath = (Join-Path $env:TEMP ("OknaWindowLockSmoke-" + [guid]::NewGuid().ToString("N") + ".exe"))
)

$ErrorActionPreference = "Stop"
$removeOutput = -not $PSBoundParameters.ContainsKey("OutputPath")
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

$src = Join-Path $PSScriptRoot "src"
$wpf = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$files = Get-ChildItem -Path $src -Filter *.cs |
    Where-Object { $_.Name -ne "Program.cs" } |
    ForEach-Object { $_.FullName }
$files += Join-Path $PSScriptRoot "tests\WindowBoundsLockSmoke.cs"

$arguments = @(
    "/nologo",
    "/target:exe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/unsafe",
    "/codepage:65001",
    "/main:Okna.Tests.WindowBoundsLockSmoke",
    "/out:$OutputPath",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Web.Extensions.dll",
    "/r:$wpf\UIAutomationClient.dll",
    "/r:$wpf\UIAutomationTypes.dll",
    "/r:$wpf\WindowsBase.dll"
) + $files

try {
    & $csc $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Smoke-test compilation failed with exit code $LASTEXITCODE"
    }

    & $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Window-lock smoke test failed with exit code $LASTEXITCODE"
    }
}
finally {
    if ($removeOutput -and (Test-Path -LiteralPath $OutputPath)) {
        Remove-Item -LiteralPath $OutputPath -Force
    }
}
