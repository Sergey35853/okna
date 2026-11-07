param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

# Signs the given files when a certificate is configured, and does nothing
# (successfully) when it is not. The build calls this unconditionally, so a
# local build without a certificate still works while a CI release with
# SIGN_PFX_BASE64 / SIGN_PFX_PASSWORD (or OKNA_SIGN_*) produces a signed exe.
#
# Supported configuration:
#   SIGN_PFX_BASE64 + SIGN_PFX_PASSWORD      base64 PFX and its password
#   SIGN_CERT_SHA1                           thumbprint in CurrentUser\My
#   SIGN_TIMESTAMP                           timestamp URL
#   OKNA_SIGN_PFX_BASE64, OKNA_SIGN_PFX_PASSWORD,
#   OKNA_SIGN_CERT_SHA1, OKNA_SIGN_TIMESTAMP  aliases
$ErrorActionPreference = "Stop"

function Get-EnvValue([string[]]$names) {
    foreach ($name in $names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    return $null
}

$pfxBase64 = Get-EnvValue @("SIGN_PFX_BASE64", "OKNA_SIGN_PFX_BASE64")
$pfxPassword = Get-EnvValue @("SIGN_PFX_PASSWORD", "OKNA_SIGN_PFX_PASSWORD")
$certSha1 = Get-EnvValue @("SIGN_CERT_SHA1", "OKNA_SIGN_CERT_SHA1")
$stamp = Get-EnvValue @("SIGN_TIMESTAMP", "OKNA_SIGN_TIMESTAMP")
if ($stamp) { $TimestampUrl = $stamp }

if (-not $pfxBase64 -and -not $certSha1) {
    Write-Output "Signing skipped: no certificate configured (set SIGN_PFX_BASE64/SIGN_PFX_PASSWORD or SIGN_CERT_SHA1)."
    exit 0
}

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin",
        "${env:ProgramFiles(x86)}\Windows Kits\8.1\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $found = Get-ChildItem -Path $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

$signtool = Find-SignTool
if (-not $signtool) {
    throw "Signing was requested but signtool.exe was not found. Install the Windows SDK or remove the signing secrets."
}

$tempPfx = $null
try {
    $common = @("sign", "/fd", "sha256", "/tr", $TimestampUrl, "/td", "sha256")
    if ($pfxBase64) {
        if (-not $pfxPassword) { throw "SIGN_PFX_PASSWORD is not set." }
        $tempPfx = Join-Path ([System.IO.Path]::GetTempPath()) ("okna-sign-" + [guid]::NewGuid().ToString("N") + ".pfx")
        [System.IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String($pfxBase64))
        $common += @("/f", $tempPfx, "/p", $pfxPassword)
    } else {
        $common += @("/sha1", $certSha1)
    }

    foreach ($file in $Path) {
        if (-not (Test-Path $file)) { throw "File to sign not found: $file" }
        Write-Output "Signing $file"
        & $signtool @common $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file with exit code $LASTEXITCODE" }
        $signature = Get-AuthenticodeSignature $file
        Write-Output ("  status: " + $signature.Status)
    }
} finally {
    if ($tempPfx -and (Test-Path $tempPfx)) {
        Remove-Item -LiteralPath $tempPfx -Force -ErrorAction SilentlyContinue
    }
}
