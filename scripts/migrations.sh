#!/usr/bin/env bash
# Creates a migration for every module. Usage: scripts/migrations.sh Initial
set -euo pipefail
NAME="${1:?migration name required, e.g. Initial}"
dotnet build Bob1.slnx   # restore + build once; dotnet ef needs a green build
for m in Club:ClubDbContext Identity:IdentityDbContext Matches:MatchesDbContext Carpool:CarpoolDbContext \
         Notifications:NotificationsDbContext Participation:ParticipationDbContext Ranking:RankingDbContext; do
  module="${m%%:*}"; ctx="${m##*:}"
  dotnet ef migrations add "$NAME" -c "$ctx" -p "src/Bob1.Modules.$module" -s src/Bob1.Api -o Infrastructure/Migrations --no-build
done