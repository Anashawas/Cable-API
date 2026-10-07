# =============================================================================
# Builds the Cable Admin SPA (embedded-under-API variant, base=/admin/) and
# syncs it into the API's wwwroot-admin, so the admin portal ships with
# every API publish, served at /admin — separate from the landing site's
# wwwroot and from the standalone Cable-Admin deployment.
#
#   .\Scripts\build-admin.ps1            build + sync
#   .\Scripts\build-admin.ps1 -SkipBuild sync only (reuse existing build\ output)
#
# wwwroot-admin is generated output (gitignored) — the Cable-Admin repo is
# the source of truth. Does NOT touch Cable-Admin's own standalone
# production build/config (.env.production, config.production.js) — this
# uses a distinct VITE_APP_ENVIRONMENT=admin-embed so build output and
# runtime config are fully separate from that deployment.
# =============================================================================
param([switch]$SkipBuild)

$adminRepo    = "D:\NewOpenware-Server\Cable-Admin"
$wwwrootAdmin = "D:\Cable\Cable\WebApi\wwwroot-admin"

if (-not $SkipBuild) {
    Write-Host ">> Building Cable Admin (base=/admin/)..." -ForegroundColor Cyan
    Push-Location $adminRepo
    $env:BASE_URL = "/admin/"
    $env:VITE_APP_ENVIRONMENT = "admin-embed"
    npm run build:prod
    $buildFailed = $LASTEXITCODE -ne 0
    Remove-Item Env:\BASE_URL, Env:\VITE_APP_ENVIRONMENT -ErrorAction SilentlyContinue
    Pop-Location
    if ($buildFailed) { Write-Error "Admin build FAILED - wwwroot-admin not touched."; exit 1 }
}

$out = Get-ChildItem (Join-Path $adminRepo "build") -Directory -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -like "Cable-Admin-*-admin-embed" } |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1 -ExpandProperty FullName

if (-not $out -or -not (Test-Path (Join-Path $out "index.html"))) {
    Write-Error "No admin-embed build output found under build\Cable-Admin-*-admin-embed - build did not produce a valid output."
    exit 1
}

Write-Host ">> Syncing $out -> WebApi\wwwroot-admin ..." -ForegroundColor Cyan
if (Test-Path $wwwrootAdmin) { Remove-Item $wwwrootAdmin -Recurse -Force }
Copy-Item $out $wwwrootAdmin -Recurse

# sanity check
$required = @("index.html", "config\config.admin-embed.js")
$missing = $required | Where-Object { -not (Test-Path (Join-Path $wwwrootAdmin $_)) }
if ($missing) { Write-Error ("Sync incomplete - missing: " + ($missing -join ', ')); exit 1 }

$count = (Get-ChildItem $wwwrootAdmin -Recurse -File).Count
Write-Host ">> Done: $count files in WebApi\wwwroot-admin (admin ships at /admin on the next API publish)" -ForegroundColor Green
