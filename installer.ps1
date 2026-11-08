param(
    [string]$Version = "",
    [switch]$SkipIfMissing
)

# Builds the Inno Setup installer from dist\Okna.exe. CI installs Inno Setup
# first; a local machine without it can pass -SkipIfMissing to keep the rest
# of the release checks running.
$ErrorActionPreference = "Stop"

if (-not $Version) {
    $assembly = Join-Path $PSScriptRoot "src\AssemblyInfo.cs"
    $text = Get-Content -LiteralPath $assembly -Raw
    if ($text -match 'AssemblyVersion\("([0-9.]+)"\)') { $Version = $Matches[1] }
}
if (-not $Version) { throw "Could not determine the version." }
$Version = $Version.TrimStart("v", "V")

function Find-Iscc {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6",
        "$env:ProgramFiles\Inno Setup 6",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6",
        "${env:ProgramFiles(x86)}\Inno Setup 5",
        "$env:ProgramFiles\Inno Setup 5"
    )
    foreach ($root in $roots) {
        $candidate = Join-Path $root "ISCC.exe"
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    if ($SkipIfMissing) {
        Write-Warning "Inno Setup was not found; the installer was skipped."
        exit 0
    }
    throw "Inno Setup (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php."
}

$exe = Join-Path $PSScriptRoot "dist\Okna.exe"
if (-not (Test-Path $exe)) { throw "Build the program first: $exe is missing." }

$script = Join-Path $PSScriptRoot "installer.iss"
Write-Output "Building installer $Version"
& $iscc "/DMyAppVersion=$Version" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $PSScriptRoot "dist\Okna-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "The installer was not created: $setup" }

$sign = Join-Path $PSScriptRoot "sign.ps1"
if (Test-Path $sign) { & $sign -Path $setup }

$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLower()
$name = Split-Path -Leaf $setup
"$hash  $name" | Out-File -FilePath "$setup.sha256" -Encoding ascii
Write-Output "Built $setup"
