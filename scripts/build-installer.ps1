<#
.SYNOPSIS
    Build Trion Control Panel and produce an Inno Setup installer (.exe).

.DESCRIPTION
    Full pipeline in one command:

      1. Version management  -  bump VERSION, sync into .csproj files + TrionSetup.iss
      2. dotnet publish      -  self-contained win-x64 single-file for Desktop + Updater
      3. Inno Setup (ISCC)   -  compile TrionSetup.iss into TrionSetup-X.X.X.exe
      4. Collect output       -  installer + version.txt copied to dist\

    ISCC is discovered automatically in:
      - PATH
      - C:\Program Files (x86)\Inno Setup 6\ISCC.exe
      - C:\Program Files\Inno Setup 6\ISCC.exe

    Install Inno Setup 6 from https://jrsoftware.org/isdl.php

.PARAMETER Bump
    Version component to increment: release | features | bugfixes
    Build number always auto-increments. Omit to increment build number only.

.PARAMETER Output
    Destination folder for the finished installer (default: dist\).

.PARAMETER NoBump
    Do NOT change any version numbers — useful for rebuilding without a new release.

.PARAMETER SkipPublish
    Skip dotnet publish and reuse the existing publish output in dist\publish\.

.PARAMETER Help
    Show this help and exit.

.EXAMPLE
    .\scripts\build-installer.ps1
    .\scripts\build-installer.ps1 -Bump features
    .\scripts\build-installer.ps1 -Bump bugfixes
    .\scripts\build-installer.ps1 -Bump release
    .\scripts\build-installer.ps1 -NoBump
    .\scripts\build-installer.ps1 -SkipPublish
    .\scripts\build-installer.ps1 -Output C:\Releases
#>

[CmdletBinding()]
param(
    [ValidateSet('release', 'features', 'bugfixes', '')]
    [string]$Bump = '',
    [string]$Output = '',
    [switch]$NoBump,
    [switch]$SkipPublish,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

# ── Paths ─────────────────────────────────────────────────────────────────────
$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot     = Split-Path -Parent $ScriptDir
$DesktopProj  = Join-Path $RepoRoot 'src\Trion.Desktop\Trion.Desktop.csproj'
$UpdaterProj  = Join-Path $RepoRoot 'src\Trion.Updater\Trion.Updater.csproj'
$InstallerIss = Join-Path $RepoRoot 'installer\TrionSetup.iss'
$VersionFile  = Join-Path $RepoRoot 'VERSION'

if (-not $Output) { $Output = Join-Path $RepoRoot 'dist' }

$PublishDir  = Join-Path $Output 'publish'
$BuildDir    = Join-Path $RepoRoot 'Build'

# ── Installer bitmap generation (TrionBlue Dark — matches TrionBlueDarkTheme.xaml) ──
function Build-InstallerAssets {
    param(
        [string]$AssetsDir,
        [string]$LogoPath,
        [string]$VersionStr
    )

    Add-Type -AssemblyName System.Drawing
    New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null

    # Exact colours from TrionBlueDarkTheme.xaml
    $cSidebar = [System.Drawing.Color]::FromArgb(0x0F, 0x0F, 0x0F)  # Sidebar.Background
    $cHeader  = [System.Drawing.Color]::FromArgb(0x1C, 0x1C, 0x1C)  # Background.Secondary
    $cAccent  = [System.Drawing.Color]::FromArgb(0x00, 0xD4, 0xFF)  # Accent.Primary
    $cBorder  = [System.Drawing.Color]::FromArgb(0x2E, 0x2E, 0x2E)  # Border.Default
    $cFgSec   = [System.Drawing.Color]::FromArgb(0x9A, 0x9A, 0x9A)  # Foreground.Secondary
    $cFgMuted = [System.Drawing.Color]::FromArgb(0x6A, 0x6A, 0x6A)  # Foreground.Muted

    # ColorMatrix: zero out all RGB channels, then add cyan offset
    # This mimics the WPF OpacityMask + Accent.Primary fill trick:
    # any opaque pixel becomes #00D4FF; transparent pixels stay transparent
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix00 = 0.0   # R->R = 0
    $cm.Matrix11 = 0.0   # G->G = 0
    $cm.Matrix22 = 0.0   # B->B = 0
    $cm.Matrix33 = 1.0   # A->A unchanged
    $cm.Matrix44 = 1.0   # W->W unchanged
    $cm.Matrix40 = 0.0   # R offset = 0  (#00)
    $cm.Matrix41 = [float](0xD4 / 255.0)  # G offset = 0.831 (#D4)
    $cm.Matrix42 = 1.0   # B offset = 1.0 (#FF)
    $ia = New-Object System.Drawing.Imaging.ImageAttributes
    $ia.SetColorMatrix($cm,
        [System.Drawing.Imaging.ColorMatrixFlag]::Default,
        [System.Drawing.Imaging.ColorAdjustType]::Bitmap)

    # Load logo (may be null if file missing)
    $logo = $null
    if (Test-Path $LogoPath) {
        try { $logo = [System.Drawing.Image]::FromFile($LogoPath) } catch {}
    }

    # ── Sidebar  164 × 314  ────────────────────────────────────────────────
    $sw = 164; $sh = 314
    $bmpSide = New-Object System.Drawing.Bitmap($sw, $sh)
    $g = [System.Drawing.Graphics]::FromImage($bmpSide)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear($cSidebar)

    # 2 px cyan accent line on the right edge (mirrors the Sidebar.SelectedBorder)
    $g.FillRectangle([System.Drawing.SolidBrush]::new($cAccent), $sw - 2, 0, 2, $sh)

    # Logo centred in the top 55 % (tinted cyan to match WPF OpacityMask fill)
    $logoY = 44
    $logoS = 76
    $logoX = [int](($sw - 2 - $logoS) / 2)
    if ($null -ne $logo) {
        $destRect = [System.Drawing.Rectangle]::new($logoX, $logoY, $logoS, $logoS)
        $g.DrawImage($logo, $destRect, 0, 0, $logo.Width, $logo.Height,
                     [System.Drawing.GraphicsUnit]::Pixel, $ia)
    } else {
        $fFall = New-Object System.Drawing.Font('Segoe UI', 46, [System.Drawing.FontStyle]::Bold)
        $sz = $g.MeasureString('T', $fFall)
        $g.DrawString('T', $fFall, [System.Drawing.SolidBrush]::new($cAccent),
                       [float]$logoX, [float]$logoY)
        $fFall.Dispose()
    }

    # Separator
    $g.FillRectangle([System.Drawing.SolidBrush]::new($cBorder), 16, $sh - 112, $sw - 32, 1)

    # Text: "TRION" / "Control Panel" / version
    $fBold = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $fNorm = New-Object System.Drawing.Font('Segoe UI',  9, [System.Drawing.FontStyle]::Regular)
    $g.DrawString('TRION',         $fBold, [System.Drawing.SolidBrush]::new($cAccent),  [float]16, [float]($sh - 102))
    $g.DrawString('Control Panel', $fNorm, [System.Drawing.SolidBrush]::new($cFgSec),   [float]16, [float]($sh -  76))
    $g.DrawString("v$VersionStr",  $fNorm, [System.Drawing.SolidBrush]::new($cFgMuted), [float]16, [float]($sh -  58))

    $bmpSide.Save("$AssetsDir\WizardSidebar.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmpSide.Dispose(); $fBold.Dispose(); $fNorm.Dispose()

    # ── Small header image  55 × 58  ──────────────────────────────────────
    $iw = 55; $ih = 58
    $bmpSmall = New-Object System.Drawing.Bitmap($iw, $ih)
    $g2 = [System.Drawing.Graphics]::FromImage($bmpSmall)
    $g2.SmoothingMode    = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.Clear($cHeader)

    if ($null -ne $logo) {
        $m = 8
        $destRect2 = [System.Drawing.Rectangle]::new($m, $m, $iw - $m * 2, $ih - $m * 2)
        $g2.DrawImage($logo, $destRect2, 0, 0, $logo.Width, $logo.Height,
                      [System.Drawing.GraphicsUnit]::Pixel, $ia)
    } else {
        $fT = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold)
        $sz2 = $g2.MeasureString('T', $fT)
        $g2.DrawString('T', $fT, [System.Drawing.SolidBrush]::new($cAccent),
                        ($iw - $sz2.Width) / 2, ($ih - $sz2.Height) / 2)
        $fT.Dispose()
    }

    $bmpSmall.Save("$AssetsDir\WizardSmall.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g2.Dispose(); $bmpSmall.Dispose()

    if ($null -ne $logo) { $logo.Dispose() }
    $ia.Dispose()
}

# ── Colour helpers ────────────────────────────────────────────────────────────
function Section([string]$msg) {
    Write-Host ''
    Write-Host '==========================================' -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host '==========================================' -ForegroundColor Cyan
}
function Ok([string]$msg)   { Write-Host "  [+]  $msg" -ForegroundColor Green  }
function Info([string]$msg) { Write-Host "  [·]  $msg" -ForegroundColor Cyan   }
function Warn([string]$msg) { Write-Host "  [!]  $msg" -ForegroundColor Yellow }
function Die([string]$msg)  { Write-Host "`n  [x]  ERROR: $msg`n" -ForegroundColor Red; exit 1 }

# ── Pre-flight ────────────────────────────────────────────────────────────────
if (-not (Test-Path $VersionFile))   { Die "VERSION file not found at $VersionFile" }
if (-not (Test-Path $DesktopProj))   { Die "Trion.Desktop.csproj not found at $DesktopProj" }
if (-not (Test-Path $UpdaterProj))   { Die "Trion.Updater.csproj not found at $UpdaterProj" }
if (-not (Test-Path $InstallerIss))  { Die "TrionSetup.iss not found at $InstallerIss" }

if (-not $SkipPublish) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Die ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
    }
}

# Locate ISCC (Inno Setup Compiler)
$IsccCandidates = @(
    'ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$Iscc = $IsccCandidates | Where-Object {
    $cmd = Get-Command $_ -ErrorAction SilentlyContinue
    $cmd -or (Test-Path $_)
} | Select-Object -First 1

if (-not $Iscc) {
    Die "Inno Setup Compiler (ISCC.exe) not found.`n`n  Install Inno Setup 6 from https://jrsoftware.org/isdl.php`n  Then re-run this script."
}
Info "ISCC found: $Iscc"

# ── 1. Version management ─────────────────────────────────────────────────────
Section '1/4  Version management'

$current = (Get-Content $VersionFile -Raw).Trim()
$parts   = $current -split '\.'

[int]$r = 0; [int]$f = 0; [int]$b = 0; [int]$n = 0
$valid = ($parts.Count -eq 4) `
      -and [int]::TryParse($parts[0], [ref]$r) `
      -and [int]::TryParse($parts[1], [ref]$f) `
      -and [int]::TryParse($parts[2], [ref]$b) `
      -and [int]::TryParse($parts[3], [ref]$n)
if (-not $valid) {
    Die "Invalid VERSION file: '$current'  -  expected RELEASE.FEATURES.BUGFIXES.BUILD"
}

if (-not $NoBump) {
    $n++
    switch ($Bump) {
        'release'  { $r++; $f = 0; $b = 0; $n = 1 }
        'features' { $f++;         $b = 0; $n = 1 }
        'bugfixes' {        $b++;          $n = 1 }
    }
    $version = "$r.$f.$b.$n"
    Set-Content -Path $VersionFile -Value $version -NoNewline
    Ok "Version bumped: $current -> $version"

    # Sync into Trion.Desktop.csproj
    $xml  = Get-Content $DesktopProj -Raw
    $xml  = $xml -replace '<Version>[^<]*</Version>',                 "<Version>$version</Version>"
    $xml  = $xml -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
    $xml  = $xml -replace '<FileVersion>[^<]*</FileVersion>',         "<FileVersion>$version</FileVersion>"
    Set-Content -Path $DesktopProj -Value $xml -NoNewline
    Ok "Synced into Trion.Desktop.csproj"

    # Sync into Trion.Updater.csproj
    $xmlU = Get-Content $UpdaterProj -Raw
    $xmlU = $xmlU -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
    $xmlU = $xmlU -replace '<FileVersion>[^<]*</FileVersion>',         "<FileVersion>$version</FileVersion>"
    Set-Content -Path $UpdaterProj -Value $xmlU -NoNewline
    Ok "Synced into Trion.Updater.csproj"

} else {
    $version = $current
    Warn "Version NOT bumped (-NoBump): $version"
}

# Full 4-part version used in the installer so it matches the Trion binary exactly.
$issVersion = $version

Write-Host ''
Write-Host "  Version : $version" -ForegroundColor White
Info "$r  release status  (0 = alpha/beta)"
Info "$f  features"
Info "$b  bugfixes"
Info "$n  build number"

# ── 2. dotnet publish (self-contained, win-x64, single-file) ─────────────────
Section '2/4  dotnet publish  (self-contained win-x64)'

if ($SkipPublish) {
    if (-not (Test-Path "$PublishDir\Trion.Desktop.exe")) {
        Die "No publish output at $PublishDir\Trion.Desktop.exe — remove -SkipPublish to rebuild."
    }
    Info "Reusing existing publish output at $PublishDir"
} else {
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    # ── Trion.Desktop ─────────────────────────────────────────────────────────
    # Note: AfterTargets="Build" in the csproj also builds Trion.Updater and
    # copies it to the output; we overwrite it with a proper publish below.
    Write-Host ''
    Info "Publishing Trion.Desktop..."
    $desktopArgs = @(
        'publish', $DesktopProj,
        '--configuration', 'Release',
        '--runtime',       'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        "-p:Version=$version",
        '--output', $PublishDir,
        '--nologo'
    )
    & dotnet @desktopArgs
    if ($LASTEXITCODE -ne 0) { Die "dotnet publish Trion.Desktop failed (exit $LASTEXITCODE)." }
    Ok "Trion.Desktop.exe  ->  $PublishDir"

    # ── Trion.Updater ─────────────────────────────────────────────────────────
    Write-Host ''
    Info "Publishing Trion.Updater..."
    $updaterArgs = @(
        'publish', $UpdaterProj,
        '--configuration', 'Release',
        '--runtime',       'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        "-p:AssemblyVersion=$version",
        "-p:FileVersion=$version",
        '--output', $PublishDir,
        '--nologo'
    )
    & dotnet @updaterArgs
    if ($LASTEXITCODE -ne 0) { Die "dotnet publish Trion.Updater failed (exit $LASTEXITCODE)." }
    Ok "Trion.Updater.exe  ->  $PublishDir"
}

# Verify both exes are present
if (-not (Test-Path "$PublishDir\Trion.Desktop.exe")) { Die "Trion.Desktop.exe missing from $PublishDir" }
if (-not (Test-Path "$PublishDir\Trion.Updater.exe")) { Die "Trion.Updater.exe missing from $PublishDir" }

$desktopSize = [math]::Round((Get-Item "$PublishDir\Trion.Desktop.exe").Length / 1MB, 1)
$updaterSize = [math]::Round((Get-Item "$PublishDir\Trion.Updater.exe").Length / 1MB, 1)
Info "Trion.Desktop.exe  ${desktopSize} MB"
Info "Trion.Updater.exe  ${updaterSize} MB"

# Write version.txt into the publish dir so the ISS can include it
Set-Content -Path "$PublishDir\version.txt" -Value $version -NoNewline
Ok "version.txt  ($version)"

# ── 2.5. Code-sign executables (optional) ────────────────────────────────────
# Uses signtool.exe (Windows SDK) instead of Set-AuthenticodeSignature.
# Set-AuthenticodeSignature has a PS 5.1 + Set-StrictMode -Version Latest bug:
# it throws InvalidCastFromStringToInteger on [string[]] parameter binding
# regardless of whether foreach or ForEach-Object is used as the loop.
$ThumbFile = Join-Path $RepoRoot 'installer\certs\thumbprint.txt'
if (Test-Path $ThumbFile) {
    Section '2.5/4  Code-signing'

    $thumbprint = (Get-Content $ThumbFile -Raw).Trim()

    # Locate signtool.exe — search Windows SDK x64 bin dirs, then PATH
    $signtool = $null
    $wkBin = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path $wkBin) {
        $signtool = Get-ChildItem $wkBin -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                    Where-Object  { $_.FullName -match '\\x64\\' } |
                    Sort-Object   FullName -Descending |
                    Select-Object -ExpandProperty FullName -First 1
    }
    if (-not $signtool) {
        $stCmd = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
        if ($stCmd) { $signtool = $stCmd.Source }
    }

    if (-not $signtool) {
        Warn 'signtool.exe not found — skipping signing.'
        Warn 'Install "Windows SDK Signing Tools" from the Windows SDK installer.'
    } else {
        $certInfo = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
        if ($null -eq $certInfo) {
            Warn "Cert thumbprint $thumbprint not found in CurrentUser\My."
            Warn "Run: .\scripts\create-cert.ps1 -TrustLocally  to reinstall."
        } else {
            Info "Signing with: $($certInfo.Subject)"
            # No array + no loop — PS 5.1 + Set-StrictMode -Version Latest throws
            # InvalidCastFromStringToInteger at the foreach variable-binding step for
            # ANY string array iteration, regardless of the loop body content.
            # Two explicit blocks are simpler and completely avoid the engine bug.
            if (Test-Path "$PublishDir\Trion.Desktop.exe") {
                & $signtool sign /fd SHA256 /sha1 $thumbprint "$PublishDir\Trion.Desktop.exe"
                if ($LASTEXITCODE -eq 0) { Ok 'Signed  Trion.Desktop.exe' }
                else { Warn "signtool exit $LASTEXITCODE for Trion.Desktop.exe — continuing" }
            }
            if (Test-Path "$PublishDir\Trion.Updater.exe") {
                & $signtool sign /fd SHA256 /sha1 $thumbprint "$PublishDir\Trion.Updater.exe"
                if ($LASTEXITCODE -eq 0) { Ok 'Signed  Trion.Updater.exe' }
                else { Warn "signtool exit $LASTEXITCODE for Trion.Updater.exe — continuing" }
            }
        }
    }
} else {
    Warn 'No thumbprint.txt found — skipping code-signing.'
    Warn 'Run: .\scripts\create-cert.ps1 -TrustLocally  to generate and install the cert.'
}

# ── 3. Compile Inno Setup installer ──────────────────────────────────────────
Section '3/4  Inno Setup  (ISCC)'

# Generate branded sidebar + small-header BMPs before calling ISCC
$AssetsDir = Join-Path $RepoRoot 'installer\Assets'
$LogoPath  = Join-Path $RepoRoot 'res\icons\trion-logo\TrionLogoNew.png'
Info "Generating installer assets in $AssetsDir"
Build-InstallerAssets -AssetsDir $AssetsDir -LogoPath $LogoPath -VersionStr $issVersion
Ok "WizardSidebar.bmp + WizardSmall.bmp generated"

New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

$IsccArgs = @(
    "/DSourceDir=$PublishDir",
    "/DAppVersion=$issVersion",
    "/O$BuildDir",
    $InstallerIss
)

Write-Host ''
Info "Running: $Iscc $($IsccArgs -join ' ')"
Write-Host ''

& $Iscc @IsccArgs
if ($LASTEXITCODE -ne 0) { Die "ISCC failed (exit $LASTEXITCODE)." }

$InstallerExe = Get-ChildItem $BuildDir -Filter "TrionSetup-*.exe" |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1

if (-not $InstallerExe) { Die "ISCC finished but no TrionSetup-*.exe found in $BuildDir" }

# ── 4. Collect output ─────────────────────────────────────────────────────────
Section '4/4  Collecting output'

New-Item -ItemType Directory -Path $Output -Force | Out-Null

$DestExe = Join-Path $Output $InstallerExe.Name
Copy-Item $InstallerExe.FullName $DestExe -Force

$installerSizeMB = [math]::Round($InstallerExe.Length / 1MB, 1)
Ok "Installer:   $($InstallerExe.Name)  (${installerSizeMB} MB)"

Set-Content -Path (Join-Path $Output 'version.txt') -Value $version -NoNewline
Ok "version.txt  ($version)"

# Clean publish temp
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Ok "Cleaned $PublishDir"

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '==========================================' -ForegroundColor Green
Write-Host "  Build complete!  Version $version" -ForegroundColor Green
Write-Host '==========================================' -ForegroundColor Green
Write-Host ''
Write-Host "  Installer    $DestExe" -ForegroundColor White
Write-Host "               ${installerSizeMB} MB  - self-contained, no .NET required on target"
Write-Host ''
Write-Host "  version.txt  $(Join-Path $Output 'version.txt')" -ForegroundColor White
Write-Host ''
Write-Host 'Distribute:' -ForegroundColor Cyan
Write-Host "  Share $($InstallerExe.Name) with users."
Write-Host "  Upload version.txt to your update server for the in-app update check."
Write-Host ''
