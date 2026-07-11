<#
.SYNOPSIS
    Build Trion Control Panel and produce distributable archives.

.DESCRIPTION
    Produces in dist\:
      TrionControlPanel-X.X.X.X.zip          <- full install zip (all files)
      TrionControlPanel-X.X.X.X-update.zip   <- update zip (exe + updater, for auto-update)
      TrionSetup-X.X.X.X.exe                 <- Inno Setup installer (if ISCC found)
      update.json                             <- upload to Phoenix storage
      version.txt                             <- current version string

    The self-update flow (DesktopAutoUpdater) downloads TrionControlPanel.exe directly.
    update.json is uploaded to Phoenix storage at  packages/trion/default/update.json.
    Set the downloadUrl in that file to point to where you host the raw exe.

    Version format: RELEASE.FEATURES.BUGFIXES.BUILD
      RELEASE   0 = alpha/beta  |  1+ = stable
      FEATURES  increments when new features land
      BUGFIXES  increments for bugfix-only releases
      BUILD     auto-incremented every run

.PARAMETER Bump
    Component to increment: release | features | bugfixes
    Build number always auto-increments.

.PARAMETER Output
    Output directory (default: dist\)

.PARAMETER NoBump
    Do NOT increment any version digits (dry run).

.PARAMETER SkipBuild
    Skip dotnet publish and reuse the existing bin\Release output.

.PARAMETER SkipInstaller
    Skip Inno Setup even if ISCC is found.

.PARAMETER Help
    Show this help and exit.

.EXAMPLE
    .\package-release.ps1
    .\package-release.ps1 -Bump features
    .\package-release.ps1 -Bump bugfixes
    .\package-release.ps1 -Bump release
    .\package-release.ps1 -NoBump
    .\package-release.ps1 -SkipBuild
    .\package-release.ps1 -SkipInstaller
#>

[CmdletBinding()]
param(
    [ValidateSet('release', 'features', 'bugfixes', '')]
    [string]$Bump = '',
    [string]$Output = '',
    [switch]$NoBump,
    [switch]$SkipBuild,
    [switch]$SkipInstaller,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Help) {
    Get-Help $MyInvocation.MyCommand.Path -Detailed
    exit 0
}

# ── Paths ─────────────────────────────────────────────────────────────────────
$RepoRoot      = Split-Path -Parent $MyInvocation.MyCommand.Path
$DesktopProj   = Join-Path $RepoRoot 'src\Trion.Desktop\Trion.Desktop.csproj'
$UpdaterProj   = Join-Path $RepoRoot 'src\Trion.Updater\Trion.Updater.csproj'
$InstallerIss  = Join-Path $RepoRoot 'installer\TrionSetup.iss'
$VersionFile   = Join-Path $RepoRoot 'VERSION'

if (-not $Output) { $Output = Join-Path $RepoRoot 'dist' }

# ── Colour helpers ────────────────────────────────────────────────────────────
function Section([string]$msg) {
    Write-Host ''
    Write-Host '══════════════════════════════════════════' -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host '══════════════════════════════════════════' -ForegroundColor Cyan
}
function Ok([string]$msg)   { Write-Host "  [+]  $msg" -ForegroundColor Green  }
function Info([string]$msg) { Write-Host "  [·]  $msg" -ForegroundColor Cyan   }
function Warn([string]$msg) { Write-Host "  [!]  $msg" -ForegroundColor Yellow }
function Die([string]$msg)  { Write-Host "`n  [x]  ERROR: $msg`n" -ForegroundColor Red; exit 1 }

# ── Pre-flight ────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Die ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
    }
}
if (-not (Test-Path $VersionFile)) { Die "VERSION file not found at $VersionFile" }

# ── 1. Version management ─────────────────────────────────────────────────────
Section '1/5  Version management'

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

    # Sync version into Trion.Desktop.csproj
    $xml = Get-Content $DesktopProj -Raw
    $xml = $xml -replace '<Version>[^<]*</Version>',                 "<Version>$version</Version>"
    $xml = $xml -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
    $xml = $xml -replace '<FileVersion>[^<]*</FileVersion>',         "<FileVersion>$version</FileVersion>"
    Set-Content -Path $DesktopProj -Value $xml -NoNewline
    Ok "Synced into Trion.Desktop.csproj"

    # Sync version into Trion.Updater.csproj
    $xmlU = Get-Content $UpdaterProj -Raw
    $xmlU = $xmlU -replace '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
    $xmlU = $xmlU -replace '<FileVersion>[^<]*</FileVersion>',         "<FileVersion>$version</FileVersion>"
    Set-Content -Path $UpdaterProj -Value $xmlU -NoNewline
    Ok "Synced into Trion.Updater.csproj"

    # Sync version into Inno Setup (full 4-part: RELEASE.FEATURES.BUGFIXES.BUILD,
    # matching Trion.Desktop.exe's own FileVersion/AssemblyVersion)
    $issVersion = $version
    $iss = Get-Content $InstallerIss -Raw
    $iss = $iss -replace '#define AppVersion\s+"[^"]*"', "#define AppVersion   `"$issVersion`""
    Set-Content -Path $InstallerIss -Value $iss -NoNewline
    Ok "Synced $issVersion into TrionSetup.iss"
} else {
    $version = $current
    Warn "Version NOT bumped (-NoBump): $version"
}

Write-Host ''
Write-Host "Version: $version" -ForegroundColor White
Info "$r  release status  (0 = alpha/beta)"
Info "$f  features"
Info "$b  bugfixes"
Info "$n  build number"

# ── 2. dotnet publish ─────────────────────────────────────────────────────────
Section '2/5  dotnet publish (win-x64, self-contained)'

$TfmDir    = 'net10.0-windows10.0.17763.0'
$BinRelDir = Join-Path $RepoRoot "src\Trion.Desktop\bin\Release\$TfmDir"

if ($SkipBuild) {
    if (-not (Test-Path "$BinRelDir\Trion.Desktop.exe")) {
        Die "No binary at $BinRelDir\Trion.Desktop.exe  -  remove -SkipBuild to rebuild."
    }
    Info "Using existing build at $BinRelDir"
} else {
    # Build + copy Trion.Desktop (AfterTargets="Build" automatically builds Trion.Updater)
    $buildArgs = @(
        'build', $DesktopProj,
        '--configuration', 'Release',
        '-p:Platform=x64',
        "--p:Version=$version",
        '--nologo'
    )
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { Die "dotnet build failed (exit $LASTEXITCODE)." }
    Ok "Built Trion.Desktop $version + Trion.Updater"
}

# Confirm both exes are present
if (-not (Test-Path "$BinRelDir\Trion.Desktop.exe"))  { Die "Trion.Desktop.exe not found in $BinRelDir" }
if (-not (Test-Path "$BinRelDir\Trion.Updater.exe"))  { Die "Trion.Updater.exe not found in $BinRelDir" }

# ── 3. Collect output files ───────────────────────────────────────────────────
Section '3/5  Assembling archives'

New-Item -ItemType Directory -Path $Output -Force | Out-Null

$FullZipName   = "TrionControlPanel-$version"
$UpdateZipName = "TrionControlPanel-$version-update"
$FullZipPath   = Join-Path $Output "$FullZipName.zip"
$UpdateZipPath = Join-Path $Output "$UpdateZipName.zip"

# Files included in both archives
$exeFiles = @(
    "$BinRelDir\Trion.Desktop.exe",
    "$BinRelDir\Trion.Updater.exe"
)

# Localisation files
$locFiles = Get-ChildItem "$BinRelDir\Resources\Localization" -Filter '*.json' `
            -ErrorAction SilentlyContinue

# ── Full install zip ──────────────────────────────────────────────────────────
$stageDir = Join-Path $Output "stage\$FullZipName"
if (Test-Path (Split-Path $stageDir -Parent)) { Remove-Item (Split-Path $stageDir -Parent) -Recurse -Force }
New-Item -ItemType Directory -Path "$stageDir\Resources\Localization" -Force | Out-Null

foreach ($exe in $exeFiles) {
    Copy-Item $exe $stageDir
    Ok "$FullZipName\$(Split-Path $exe -Leaf)"
}

# appsettings.json (only if not already installed — Inno Setup handles this via onlyifdoesntexist)
$appSettings = "$BinRelDir\appsettings.json"
if (Test-Path $appSettings) {
    Copy-Item $appSettings $stageDir
    Ok "$FullZipName\appsettings.json"
}

foreach ($loc in $locFiles) {
    Copy-Item $loc.FullName "$stageDir\Resources\Localization\"
    Ok "$FullZipName\Resources\Localization\$($loc.Name)"
}

Set-Content "$stageDir\version.txt" $version -NoNewline
Ok "$FullZipName\version.txt  ($version)"

if (Test-Path $FullZipPath) { Remove-Item $FullZipPath -Force }
Compress-Archive -Path $stageDir -DestinationPath $FullZipPath -CompressionLevel Optimal
$fullSize = [math]::Round((Get-Item $FullZipPath).Length / 1MB, 2)
Ok "Full archive:    $FullZipName.zip  (${fullSize} MB)"

# ── Update zip (exe + updater only) ──────────────────────────────────────────
# DesktopAutoUpdater downloads only TrionControlPanel.exe for hot-swap.
# This zip lets users manually extract and replace both exes when needed.
$updateStageDir = Join-Path $Output "stage\$UpdateZipName"
New-Item -ItemType Directory -Path $updateStageDir -Force | Out-Null

foreach ($exe in $exeFiles) {
    Copy-Item $exe $updateStageDir
}
Set-Content "$updateStageDir\version.txt" $version -NoNewline

if (Test-Path $UpdateZipPath) { Remove-Item $UpdateZipPath -Force }
Compress-Archive -Path $updateStageDir -DestinationPath $UpdateZipPath -CompressionLevel Optimal
$updateSize = [math]::Round((Get-Item $UpdateZipPath).Length / 1MB, 2)
Ok "Update archive:  $UpdateZipName.zip  (${updateSize} MB)"

# Cleanup staging
Remove-Item (Join-Path $Output 'stage') -Recurse -Force -ErrorAction SilentlyContinue

# ── version.txt + update.json ─────────────────────────────────────────────────
$versionTxt = Join-Path $Output 'version.txt'
Set-Content -Path $versionTxt -Value $version -NoNewline
Ok "version.txt  ($version)"

# update.json is uploaded to Phoenix storage: packages/trion/default/update.json
# DesktopAutoUpdater reads downloadUrl and saves the file as TrionControlPanel.exe.
# Set downloadUrl to wherever you host the raw Trion.Desktop.exe for direct download.
$today = Get-Date -Format 'yyyy-MM-dd'
$updateJson = [ordered]@{
    version     = $version
    releaseDate = $today
    downloadUrl = ""   # <- set to direct URL for Trion.Desktop.exe
    changelog   = @()
    mandatory   = $false
} | ConvertTo-Json -Depth 3
Set-Content -Path (Join-Path $Output 'update.json') -Value $updateJson -Encoding UTF8
Warn "update.json written  -  fill in downloadUrl before uploading to Phoenix storage"

# ── 4. Inno Setup installer ───────────────────────────────────────────────────
Section '4/5  Inno Setup installer'

$isccPaths = @(
    'ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$iscc = $isccPaths | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1

if ($SkipInstaller) {
    Warn "Skipped (-SkipInstaller)"
} elseif (-not $iscc) {
    Warn "ISCC not found  -  skipping installer. Install Inno Setup 6 or add ISCC to PATH."
} else {
    $issVersion = $version
    Info "Building TrionSetup-$issVersion.exe with $iscc"
    & $iscc $InstallerIss
    if ($LASTEXITCODE -ne 0) { Die "ISCC failed (exit $LASTEXITCODE)." }

    $setupExe = Join-Path $RepoRoot "Build\TrionSetup-$issVersion.exe"
    if (Test-Path $setupExe) {
        $destExe = Join-Path $Output "TrionSetup-$issVersion.exe"
        Copy-Item $setupExe $destExe -Force
        $setupSize = [math]::Round((Get-Item $destExe).Length / 1MB, 2)
        Ok "TrionSetup-$issVersion.exe  (${setupSize} MB)"
    } else {
        Warn "Installer not found at $setupExe after ISCC run"
    }
}

# ── 5. Summary ────────────────────────────────────────────────────────────────
Section '5/5  Done'

Write-Host ''
Write-Host "Packages ready!  Version $version" -ForegroundColor Green
Write-Host ''
Write-Host "  Full archive     $FullZipPath" -ForegroundColor White
Write-Host "                   ${fullSize} MB  -  all files, for first-time install"
Write-Host ''
Write-Host "  Update archive   $UpdateZipPath" -ForegroundColor White
Write-Host "                   ${updateSize} MB  -  exe + updater, for manual upgrade"
Write-Host ''
Write-Host "  version.txt      $versionTxt" -ForegroundColor White
Write-Host ''
Write-Host "  update.json      $(Join-Path $Output 'update.json')" -ForegroundColor White
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Host Trion.Desktop.exe at a stable URL (e.g. your CDN or storage server)'
Write-Host '  2. Edit dist\update.json  and set  "downloadUrl"  to that URL'
Write-Host '  3. Upload update.json to Phoenix storage:  packages/trion/default/update.json'
Write-Host '  4. Upload version.txt to the same directory (used by the update check endpoint)'
Write-Host ''
Write-Host 'To produce an Inno Setup installer, install Inno Setup 6 and re-run without -SkipInstaller.' -ForegroundColor Cyan
Write-Host ''
