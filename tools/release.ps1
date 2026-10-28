<#
.SYNOPSIS
  Builds, packs and publishes a SkyCareer release to GitHub Releases (Velopack).

.EXAMPLE
  tools/release.ps1 -Version 0.2.0 -Notes notes.md
  tools/release.ps1 -Version 0.2.0 -PackOnly      # build the installer locally, upload nothing

.NOTES
  Needs the MSFS 2024 SDK (so the release contains SimConnect) and, unless -PackOnly, a GitHub token with
  "Contents: Read and write" on the repo in the SKYCAREER_GH_TOKEN environment variable.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Notes,
    [switch]$PackOnly,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Velopack installs to %LOCALAPPDATA%\<PackId> and deletes that folder on uninstall. It must differ from
# %LOCALAPPDATA%\SkyCareer, where the careers database lives.
$packId = 'SkyCareerApp'
$publishDir = Join-Path $root 'publish'
$releaseDir = Join-Path $root 'releases'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Invoke-Checked { & $args[0] $args[1..($args.Count - 1)]; if ($LASTEXITCODE -ne 0) { throw "Fehlgeschlagen: $($args -join ' ')" } }

$repoUrl = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.SkyCareerRepoUrl
if (-not $repoUrl -or $repoUrl -match 'OWNER') { throw "SkyCareerRepoUrl in Directory.Build.props ist nicht gesetzt." }
$token = $env:SKYCAREER_GH_TOKEN
if (-not $PackOnly -and -not $token) { throw "Umgebungsvariable SKYCAREER_GH_TOKEN fehlt." }
if ($Notes -and -not (Test-Path $Notes)) { throw "Release-Notes $Notes nicht gefunden." }
if (-not $PackOnly -and (git status --porcelain)) { throw "Es gibt nicht committete Änderungen – erst committen." }

Step 'Werkzeuge'
Invoke-Checked dotnet tool restore

if (-not $SkipTests) {
    Step 'Tests'
    Invoke-Checked dotnet test SkyCareer.sln -c Release
}

Step "Veröffentlichen $Version"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Checked dotnet publish src/SkyCareer.App -c Release -r win-x64 --self-contained false "-p:Version=$Version" -o $publishDir
# A release without SimConnect would only work in replay mode.
if (-not (Test-Path (Join-Path $publishDir 'SimConnect.dll'))) { throw "SimConnect.dll fehlt im Build – MSFS 2024 SDK installiert?" }
if (-not (Test-Path (Join-Path $publishDir 'content\missions'))) { throw "Mitgelieferte Inhalte (content) fehlen im Build." }

if (-not $PackOnly) {
    Step 'Letztes Release laden (für Delta-Updates)'
    Invoke-Checked dotnet vpk download github --repoUrl $repoUrl --token $token -o $releaseDir
}

Step 'Paket bauen'
$pack = @('vpk', 'pack', '-u', $packId, '-v', $Version, '-p', $publishDir, '-e', 'SkyCareer.App.exe', '-o', $releaseDir,
          '--packTitle', 'SkyCareer', '--packAuthors', 'SkyCareer',
          '--icon', 'src/SkyCareer.App/Assets/SkyCareer.ico',
          '--framework', 'net10-x64-desktop,webview2')
if ($Notes) { $pack += @('--releaseNotes', $Notes) }
Invoke-Checked dotnet @pack

if ($PackOnly) {
    Write-Host "`nInstaller: $releaseDir\$packId-win-Setup.exe" -ForegroundColor Green
    return
}

Step 'Auf GitHub hochladen'
Invoke-Checked dotnet vpk upload github --repoUrl $repoUrl --token $token -o $releaseDir --publish `
    --releaseName "SkyCareer $Version" --tag "v$Version"

Write-Host "`nRelease v$Version veröffentlicht: $repoUrl/releases/tag/v$Version" -ForegroundColor Green
