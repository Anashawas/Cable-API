# =============================================================================
# Builds the Cable landing site and syncs the static export into the API's
# wwwroot, so the marketing site ships with every API publish.
#
#   .\Scripts\build-landing.ps1            build + sync
#   .\Scripts\build-landing.ps1 -SkipBuild sync only (reuse existing out\)
#
# wwwroot is generated output (gitignored) — the landing repo is the source.
# =============================================================================
param([switch]$SkipBuild)

$landing = "D:\Cable\cable_landing"
$out     = Join-Path $landing "out"
$wwwroot = "D:\Cable\Cable\WebApi\wwwroot"

if (-not $SkipBuild) {
    Write-Host ">> Building landing site..." -ForegroundColor Cyan
    Push-Location $landing
    npm run build
    $buildFailed = $LASTEXITCODE -ne 0
    Pop-Location
    if ($buildFailed) { Write-Error "Landing build FAILED - wwwroot not touched."; exit 1 }
}

if (-not (Test-Path (Join-Path $out "index.html"))) {
    Write-Error "out\index.html missing - build did not produce a valid export."; exit 1
}

Write-Host ">> Syncing out\ -> WebApi\wwwroot\ ..." -ForegroundColor Cyan
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item $out $wwwroot -Recurse

# sanity check
$required = @("index.html", "404.html", "ar\index.html", "en\index.html", "_next")
$missing = $required | Where-Object { -not (Test-Path (Join-Path $wwwroot $_)) }
if ($missing) { Write-Error ("Sync incomplete - missing: " + ($missing -join ', ')); exit 1 }

$count = (Get-ChildItem $wwwroot -Recurse -File).Count
Write-Host ">> Done: $count files in WebApi\wwwroot (landing rides the next API publish)" -ForegroundColor Green
