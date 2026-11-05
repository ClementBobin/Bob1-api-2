# Creates a migration for every module, in a fixed order.
#   ./scripts/migrations.ps1 Initial            -> add "Initial" to each module
#   ./scripts/migrations.ps1 Initial -Reset     -> delete every module's Infrastructure/Migrations folder first (dev only!)
param(
  [Parameter(Mandatory = $true)][string]$Name,
  [switch]$Reset
)

$modules = @(
  @{ Module = 'Club';          Context = 'ClubDbContext' },
  @{ Module = 'Identity';      Context = 'IdentityDbContext' },
  @{ Module = 'Matches';       Context = 'MatchesDbContext' },
  @{ Module = 'Carpool';       Context = 'CarpoolDbContext' },
  @{ Module = 'Notifications'; Context = 'NotificationsDbContext' },
  @{ Module = 'Participation'; Context = 'ParticipationDbContext' },
  @{ Module = 'Ranking';       Context = 'RankingDbContext' }
)

# dotnet-ef must be installed: dotnet tool install --global dotnet-ef
dotnet ef --version *> $null
if ($LASTEXITCODE -ne 0) { Write-Host "dotnet-ef not found. Run: dotnet tool install --global dotnet-ef" -ForegroundColor Red; exit 1 }

# Delete ALL old migrations before building anything: a single stale migration breaks the build of every module.
if ($Reset) {
  foreach ($m in $modules) {
    $dir = "src/Bob1.Modules.$($m.Module)/Infrastructure/Migrations"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force; Write-Host "Deleted $dir" -ForegroundColor Yellow }
  }
}

# Restore + build ONCE and show the real errors. If this fails, nothing below can work.
Write-Host "==> restore + build" -ForegroundColor Cyan
dotnet build Bob1.slnx
if ($LASTEXITCODE -ne 0) {
  Write-Host "`nBuild failed - fix the errors above first (dotnet ef needs a green build)." -ForegroundColor Red
  exit 1
}

$failed = @()
foreach ($m in $modules) {
  Write-Host "==> $($m.Module)" -ForegroundColor Cyan
  # --no-build: reuse the build above (we only add migration files, other modules are unaffected)
  $out = dotnet ef migrations add $Name -c $m.Context -p "src/Bob1.Modules.$($m.Module)" -s src/Bob1.Api -o Infrastructure/Migrations --no-build 2>&1 | Out-String
  if ($LASTEXITCODE -ne 0) { $failed += $m.Module; Write-Host $out -ForegroundColor Red }
  else { Write-Host "Done." }
}

if ($failed.Count -gt 0) {
  Write-Host "`nFailed: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}
Write-Host "`nAll migrations created. Rebuild before running the API." -ForegroundColor Green