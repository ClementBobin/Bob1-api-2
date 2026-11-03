#!/usr/bin/env bash
# Creates a migration for every module. Usage: scripts/migrations.sh Initial
set -euo pipefail
NAME="${1:?migration name required, e.g. Initial}"
for m in Club:ClubDbContext Identity:IdentityDbContext Matches:MatchesDbContext Carpool:CarpoolDbContext \
         Notifications:NotificationsDbContext Participation:ParticipationDbContext Ranking:RankingDbContext; do
  module="${m%%:*}"; ctx="${m##*:}"
  dotnet ef migrations add "$NAME" -c "$ctx" -p "src/Modules/Bob1.$module" -s src/Bob1.Api
done
