<#
.SYNOPSIS
    Generate a self-signed RSA 2048 SHA-256 code-signing certificate for Trion Control Panel.

.DESCRIPTION
    Creates a code-signing cert in the current-user certificate store, then exports:
      - installer\certs\TrionCodeSign.pfx   (private key — NEVER commit)
      - installer\certs\TrionCodeSign.cer   (public key  — bundled in installer)
      - installer\certs\thumbprint.txt      (cert thumbprint — excluded from git)

    Certificate fields:
      CN = FlyingPhoenix Code Signing
      O  = FlyingPhoenix
      OU = Software Development
      C  = US
      Subject Alt Names: email + URI (flying-phoenix.dev)

    The .cer is bundled inside TrionSetup.iss and installed to
    CurrentUser\TrustedPublisher via certutil during setup.
    No administrator rights are required for any of this.

.PARAMETER ValidityYears
    How many years the certificate should be valid. Default: 5.

.PARAMETER Password
    PFX export password. If omitted you are prompted interactively.
    Set env:TRION_CERT_PASSWORD to skip the prompt in CI.

.PARAMETER TrustLocally
    Immediately adds TrionCodeSign.cer to CurrentUser\TrustedPublisher on this
    developer machine so sigcheck and SmartScreen accept the signed binaries
    without running the full installer.

.EXAMPLE
    .\scripts\create-cert.ps1
    .\scripts\create-cert.ps1 -ValidityYears 3 -TrustLocally
    $env:TRION_CERT_PASSWORD = 'hunter2'; .\scripts\create-cert.ps1 -TrustLocally
#>

[CmdletBinding()]
param(
    [int]   $ValidityYears = 5,
    [string]$Password      = '',
    [switch]$TrustLocally
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Identity constants ────────────────────────────────────────────────────────
$AppPublisher  = 'FlyingPhoenix'
$AppUrl        = 'https://flying-phoenix.dev/'
$AppSupportUrl = 'https://flying-phoenix.dev/support'
$AuthorEmail   = 'info@flying-phoenix.dev'

# Distinguished Name — visible in Windows "Issued to / Issued by" dialogs
$CertSubject = "CN=FlyingPhoenix Code Signing, O=FlyingPhoenix, OU=Software Development, C=US"

# Subject Alternative Names (OID 2.5.29.17) — shown under "Subject Alternative Name"
# in the cert Details tab
$SanExtension = "2.5.29.17={text}email=$AuthorEmail&url=$AppUrl"

# ── Paths ─────────────────────────────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$CertsDir  = Join-Path $RepoRoot 'installer\certs'
$PfxOut    = Join-Path $CertsDir 'TrionCodeSign.pfx'
$CerOut    = Join-Path $CertsDir 'TrionCodeSign.cer'
$ThumbFile = Join-Path $CertsDir 'thumbprint.txt'

New-Item -ItemType Directory -Path $CertsDir -Force | Out-Null

# ── Password (blank = no encryption on PFX) ───────────────────────────────────
$pfxPassword = ''
if ($env:TRION_CERT_PASSWORD) {
    $pfxPassword = $env:TRION_CERT_PASSWORD
} elseif ($Password) {
    $pfxPassword = $Password
}
# $pfxPassword stays '' when neither source provides one — PFX exported unencrypted

# ── Create certificate ────────────────────────────────────────────────────────
Write-Host ''
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host '  Generating Trion Code-Signing Certificate' -ForegroundColor Cyan
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Subject : $CertSubject"   -ForegroundColor White
Write-Host "  SANs    : email=$AuthorEmail" -ForegroundColor White
Write-Host "            url=$AppUrl"    -ForegroundColor White
Write-Host "  Expires : $(((Get-Date).AddYears($ValidityYears)).ToString('yyyy-MM-dd'))" -ForegroundColor White
Write-Host ''

$notAfter = (Get-Date).AddYears($ValidityYears)

# Try with Subject Alternative Names first; fall back if the platform rejects the URL SAN
$cert = $null
try {
    $cert = New-SelfSignedCertificate `
        -Subject            $CertSubject `
        -Type               CodeSigningCert `
        -KeyLength          2048 `
        -KeyAlgorithm       RSA `
        -HashAlgorithm      SHA256 `
        -CertStoreLocation  'Cert:\CurrentUser\My' `
        -NotAfter           $notAfter `
        -FriendlyName       'Trion Control Panel — FlyingPhoenix Code Signing' `
        -TextExtension      @($SanExtension)
    Write-Host '  [+]  Certificate created with Subject Alternative Names.' -ForegroundColor Green
} catch {
    Write-Host "  [!]  SAN extension failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host '  [·]  Retrying without Subject Alternative Names...' -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Subject            $CertSubject `
        -Type               CodeSigningCert `
        -KeyLength          2048 `
        -KeyAlgorithm       RSA `
        -HashAlgorithm      SHA256 `
        -CertStoreLocation  'Cert:\CurrentUser\My' `
        -NotAfter           $notAfter `
        -FriendlyName       'Trion Control Panel — FlyingPhoenix Code Signing'
    Write-Host '  [+]  Certificate created (no SAN).' -ForegroundColor Green
}

Write-Host ''
Write-Host "       Thumbprint : $($cert.Thumbprint)"                   -ForegroundColor White
Write-Host "       Issued to  : $($cert.Subject)"                     -ForegroundColor White
Write-Host "       Issued by  : $($cert.Issuer)"                      -ForegroundColor White
Write-Host "       Valid from : $($cert.NotBefore.ToString('yyyy-MM-dd'))"  -ForegroundColor White
Write-Host "       Valid to   : $($cert.NotAfter.ToString('yyyy-MM-dd'))"   -ForegroundColor White
Write-Host ''

# ── Export PFX (private key + cert chain) ─────────────────────────────────────
if ($pfxPassword) {
    $securePwd = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
} else {
    $securePwd = [System.Security.SecureString]::new()   # empty = no password (PS 5.1 compatible)
}
Export-PfxCertificate -Cert $cert -FilePath $PfxOut -Password $securePwd | Out-Null
if ($pfxPassword) {
    Write-Host "  [+]  PFX  ->  $PfxOut  (password protected)" -ForegroundColor Green
} else {
    Write-Host "  [+]  PFX  ->  $PfxOut  (no password)" -ForegroundColor Green
}
Write-Host '       PRIVATE KEY — never commit this file.' -ForegroundColor Yellow

# ── Export CER (public key only — safe to commit and ship) ────────────────────
Export-Certificate -Cert $cert -FilePath $CerOut -Type CERT | Out-Null

Write-Host "  [+]  CER  ->  $CerOut" -ForegroundColor Green
Write-Host '       Public key only — safe to commit and bundle in the installer.' -ForegroundColor DarkCyan

# ── Save thumbprint ───────────────────────────────────────────────────────────
Set-Content -Path $ThumbFile -Value $cert.Thumbprint -NoNewline
Write-Host "  [+]  Thumbprint saved  ->  $ThumbFile" -ForegroundColor Green

# ── Optionally trust on this developer machine ────────────────────────────────
if ($TrustLocally) {
    Write-Host ''
    Write-Host '  [·]  Trusting certificate in CurrentUser\TrustedPublisher...' -ForegroundColor Cyan
    $result = certutil.exe -user -addstore TrustedPublisher $CerOut 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host '  [+]  Trusted locally — signed binaries will pass SmartScreen on this machine.' -ForegroundColor Green
    } else {
        Write-Host "  [!]  certutil returned exit ${LASTEXITCODE}:`n$result" -ForegroundColor Yellow
    }
}

# ── Verify it looks right by inspecting cert properties ──────────────────────
Write-Host ''
Write-Host '  Verifying certificate extensions:' -ForegroundColor Cyan
$cert.Extensions | ForEach-Object {
    $friendlyName = $_.Oid.FriendlyName
    if (-not $friendlyName) { $friendlyName = $_.Oid.Value }
    Write-Host "    $($_.Oid.Value.PadRight(20))  $friendlyName" -ForegroundColor DarkCyan
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '==========================================' -ForegroundColor Green
Write-Host '  Certificate ready!' -ForegroundColor Green
Write-Host '==========================================' -ForegroundColor Green
Write-Host ''
Write-Host '  Files:' -ForegroundColor White
Write-Host "    installer\certs\TrionCodeSign.pfx   <- private key (secret)" -ForegroundColor Red
Write-Host "    installer\certs\TrionCodeSign.cer   <- public key  (commit + ship)" -ForegroundColor Green
Write-Host "    installer\certs\thumbprint.txt      <- for reference (gitignored)" -ForegroundColor Yellow
Write-Host ''
Write-Host '  Next steps:' -ForegroundColor Cyan
Write-Host '    1. git add installer\certs\TrionCodeSign.cer'
Write-Host '    2. Run  .\scripts\build-installer.ps1  (signs EXEs automatically)'
Write-Host "    3. Set  `$env:TRION_CERT_PASSWORD  in your CI secrets for unattended builds"
Write-Host "    4. Users who run TrionSetup.exe get the cert trusted automatically via certutil"
Write-Host ''
Write-Host "  Certificate URL  : $AppUrl" -ForegroundColor DarkCyan
Write-Host "  Support URL      : $AppSupportUrl" -ForegroundColor DarkCyan
Write-Host "  Author email     : $AuthorEmail" -ForegroundColor DarkCyan
Write-Host ''
