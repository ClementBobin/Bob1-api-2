# Creates a migration for every module, in a fixed order.
#   ./scripts/migrations.ps1 Initial            -> add "Initial" to each module
#   ./scripts/migrations.ps1 Initial -Reset     -> delete every module's Migrations folder first (dev only!)
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

# Delete ALL old migrations before building anything: a single stale migration breaks the build of every module.
if ($Reset) {
  foreach ($m in $modules) {
    $dir = "src/Modules/Bob1.$($m.Module)/Migrations"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force; Write-Host "Deleted $dir" -ForegroundColor Yellow }
  }
}

$failed = @()
foreach ($m in $modules) {
  Write-Host "==> $($m.Module)" -ForegroundColor Cyan
  dotnet ef migrations add $Name -c $m.Context -p "src/Modules/Bob1.$($m.Module)" -s src/Bob1.Api 2>&1 |
    Where-Object { $_ -notmatch 'NU190\d' }   # hide the NuGet audit warnings
  if ($LASTEXITCODE -ne 0) { $failed += $m.Module }
}

if ($failed.Count -gt 0) {
  Write-Host "`nFailed: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}
Write-Host "`nAll migrations created." -ForegroundColor Green