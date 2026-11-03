# Bob1 API: modular monolith (.NET 10)

One ASP.NET Core API, one SQLite database file (one set of tables per module), strict module boundaries.

## Run
```bash
dotnet tool install --global dotnet-ef    # once

# Create the first migration of every module (bash / PowerShell)
./scripts/migrations.sh Initial
./scripts/migrations.ps1 Initial

dotnet run --project src/Bob1.Api         # Development: applies migrations, seeds data, listens on :5000
```
The database is a SQLite file named `bob1.db` (relative to the working directory). Override it with
`ConnectionStrings:Default`, e.g. `Data Source=/var/lib/bob1/bob1.db`.

Dev accounts: `parent@bob1.local / Parent123!`, `staff@bob1.local / Staff123!`, `admin@bob1.local / Admin123!`.
Try the API with `requests.http` (VS Code REST Client / Rider / Visual Studio). OpenAPI: `/openapi/v1.json`.

Changed an entity? `./scripts/migrations.sh <Name>`. Early on, the simplest reset is:
delete `bob1.db`, delete every module's `Migrations/` folder, recreate `Initial`.

## Modules and their dependencies
```
Club ◄── Identity ◄── Notifications ◄──┐
  ▲          ▲                         │
  └─ Matches ┤                         │
        ▲    └─ Carpool ───────────────┤
        │          ▲                   │
        └──── Participation ───────────┤
                   ▲                   │
                   └──────── Ranking ──┘   (arrows = "depends on", never circular)
```
| Module | Owns |
|---|---|
| Club | divisions, teams, venues |
| Identity | users, families, children, JWT + refresh tokens, family invitations |
| Matches | calendar, match admin, cancel, result |
| Carpool | rides, bookings (seat contention handled with an optimistic concurrency token) |
| Participation | presence confirmation, child registration, volunteer/staff slots, commitments, J-4 reminder job |
| Notifications | FCM device tokens, in-app notification list, push (Firebase), admin broadcast |
| Ranking | points ledger, scoring rules, leaderboard, penalties |

Rules: a module exposes its `*Module` class and `Public/` types; everything else is `internal`.
Modules never touch each other's tables: they call `Public` interfaces or react to domain events
(`PresenceConfirmed`, `SlotJoined/Left`, `RideOffered/Cancelled`, `MatchCancelled`).

## Endpoints (`/api/v1`)
**Auth**: `POST /auth/register | login | refresh | logout`
**Me**: `GET/PUT /me`, `PUT /me/password`, `POST/DELETE /me/push-tokens`, `GET /me/commitments`
**Family** (Parent): `GET /family`, `POST /family/invitations`, `GET/POST /family/children`, `PUT/DELETE /family/children/{id}`
**Club**: `GET /divisions | /teams | /venues`
**Matches**: `GET /matches?divisionId=&childId=&from=&to=`, `GET /matches/{id}`, `GET /matches/{id}/participation`
**Participation** (Parent): `POST/DELETE /matches/{m}/children/{c}/registration`, `POST /matches/{id}/presence/confirm`; any role: `POST/DELETE /slots/{id}/join`
**Carpool**: `GET/POST /matches/{id}/rides`, `PUT/DELETE /rides/{id}`, `POST/DELETE /rides/{id}/booking`
**Staff**: `GET /staff/open-slots`, `GET /staff/assignments`
**Ranking**: `GET /rankings?category=&season=`, `GET /rankings/me/entries`, `GET /ranking-rules`
**Notifications**: `GET /notifications`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all`
**Admin**: `/admin/people` (list, roles, active), `/admin/divisions | teams | venues` (CRUD), `/admin/matches` (create, update, cancel, result),
`/admin/matches/{id}/slots` + `/admin/slots/{id}` + `/admin/slots/urgent`, `/admin/matches/{id}/participation` (roster),
`/admin/ranking-rules/{key}`, `/admin/rankings`, `/admin/penalties` (+ `/quick`, `/{id}/revoke`), `/admin/notifications` (broadcast)

Errors are ProblemDetails with a stable `code` (e.g. `slot.full`, `ride.full`, `match.already_started`, `auth.invalid_credentials`).

## Push notifications (Firebase)
Set `Firebase:CredentialsPath` to your service-account JSON. Without it, pushes are only logged.
The mobile app registers its FCM token with `POST /me/push-tokens`.

## Not done yet (TODO)
- 2FA (TOTP), password reset by email
- Refuse deleting divisions/venues still referenced by children/matches; cancel a removed child's registrations
- Notify on match rescheduling; carpool/slot reminders for admins
- Tests: architecture tests (NetArchTest) for the module boundaries, Testcontainers integration tests
- Rate limiting on `/auth/*`, CORS if you add a web admin
