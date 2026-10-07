# =============================================================================
# Builds the Cable Partner SPA (base=/partner/) and syncs it into the API's
# wwwroot-partner, so the partner portal ships with every API publish and is
# served at /partner — separate from the landing site's wwwroot and from
# wwwroot-admin.
#
#   .\Scripts\build-partner.ps1              build + sync
#   .\Scripts\build-partner.ps1 -SkipBuild   sync only (reuse existing dist\)
#
# The `production` mode is deliberate for every environment, dev included.
# Its runtime config points the API at "/" — relative to whatever host serves
# the page — so one build works on dev.cable and on production without a
# rebuild. The `staging` mode hardcodes the dev host and would need rebuilding
# per environment.
#
# wwwroot-partner is generated output (gitignored); the cable-partner repo is
# the source of truth.
# =============================================================================
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"

$partnerRepo    = "D:\Cable\cable-partner"
$distDir        = Join-Path $partnerRepo "dist"
$wwwrootPartner = "D:\Cable\Cable\WebApi\wwwroot-partner"

if (-not (Test-Path $partnerRepo)) {
    Write-Error "cable-partner not found at $partnerRepo"
    exit 1
}

if (-not $SkipBuild) {
    Write-Host ">> Building Cable Partner (base=/partner/, mode=production)..." -ForegroundColor Cyan
    Push-Location $partnerRepo
    # build:production runs the unit tests, then tsc -b, then vite build.
    npm run build:production
    $buildFailed = $LASTEXITCODE -ne 0
    Pop-Location
    if ($buildFailed) {
        Write-Error "Partner build FAILED - wwwroot-partner not touched."
        exit 1
    }
}

if (-not (Test-Path (Join-Path $distDir "index.html"))) {
    Write-Error "No build output at $distDir - build did not produce a valid bundle."
    exit 1
}

Write-Host ">> Syncing $distDir -> WebApi\wwwroot-partner ..." -ForegroundColor Cyan
if (Test-Path $wwwrootPartner) { Remove-Item $wwwrootPartner -Recurse -Force }
Copy-Item $distDir $wwwrootPartner -Recurse

# Sanity check: the runtime config and the locale files are loaded over HTTP at
# startup, so a sync that drops them produces an app that builds fine and then
# renders untranslated with no API host.
$required = @(
    "index.html",
    "config\config.production.js",
    "locales\ar\common.json",
    "locales\en\common.json",
    "images\Cable-Logo.png"
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $wwwrootPartner $_)) }
if ($missing) {
    Write-Error ("Sync incomplete - missing: " + ($missing -join ', '))
    exit 1
}

# The bundle must reference /partner/ for assets, or every script 404s once the
# app is served from the sub-path.
$index = Get-Content (Join-Path $wwwrootPartner "index.html") -Raw
if ($index -notmatch '/partner/assets/') {
    Write-Error "index.html does not reference /partner/assets/ - built with the wrong BASE_URL."
    exit 1
}

$count = (Get-ChildItem $wwwrootPartner -Recurse -File).Count
Write-Host ">> Done: $count files in WebApi\wwwroot-partner (partner ships at /partner on the next API publish)" -ForegroundColor Green
