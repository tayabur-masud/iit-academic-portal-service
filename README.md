# IIT Academic Portal Service

ASP.NET Core 10 Web API (C# 14) for the IIT Academic Portal: a layered monolith using ASP.NET Core
Identity 10, EF Core 10, and PostgreSQL 18 through Npgsql 10.x.

| Project | Layer |
|---|---|
| `src/IitAcademicPortal.Domain` | Entities and rules with no infrastructure: `PortalUser`, `PortalRoles`, `AuthSession`, record-boundary interfaces |
| `src/IitAcademicPortal.Application` | Use cases: sign-in and session state, password recovery, password policy, record-access rules |
| `src/IitAcademicPortal.Infrastructure` | EF Core `PortalDbContext`, migrations, session repository, SMTP email, recovery queue |
| `src/IitAcademicPortal.Api` | Thin controllers, session-cookie authentication, anti-forgery, policies, rate limiting, problem details |
| `tests/IitAcademicPortal.Api.Tests` | xUnit API tests over the real pipeline (in-memory SQLite) plus unit tests |

## Development

Requires the .NET 10 SDK and PostgreSQL 18. Run these from this folder.

**1. Database connection.** `appsettings.Development.json` points at a local `iit_academic_portal` database as
`postgres`/`postgres`. If your local credentials differ, override them in user secrets. The Api project already
has a `UserSecretsId`, so secrets stay on your machine, outside the repository.

```powershell
dotnet user-secrets --project src/IitAcademicPortal.Api set "ConnectionStrings:Portal" "Host=localhost;Port=5432;Database=iit_academic_portal;Username=postgres;Password=<password>"
```

**2. Create or upgrade the database.** `dotnet ef` uses the same configuration as the API, and creates the
database if it does not exist.

```powershell
dotnet tool restore
dotnet ef database update -p src/IitAcademicPortal.Infrastructure -s src/IitAcademicPortal.Api
```

**3. Test accounts (optional, Development only).** Set a password; the accounts are created the next time the
API starts. Existing accounts are left as they are, except that one without a default role gets one.

```powershell
dotnet user-secrets --project src/IitAcademicPortal.Api set "DevelopmentSeed:Password" "<8+ chars with a letter and a number>"
```

**4. Run and test.**

```powershell
dotnet run --project src/IitAcademicPortal.Api --launch-profile https   # https://localhost:7286, opens Swagger
dotnet test                                                             # no database needed (in-memory SQLite)
```

Use the `https` profile when running the frontend. Its dev server proxies `/api` to `https://localhost:7286`,
and a plain `dotnet run` picks the `http` profile, which listens only on `http://localhost:5258`.

In Development, password-recovery emails are written as `.eml` files to `src/IitAcademicPortal.Api/.mail/`
instead of being sent.

**Swagger UI** opens automatically at `https://localhost:7286/swagger` when you run a launch profile
(`dotnet run`, or F5 in Visual Studio or VS Code). It renders the built-in OpenAPI document
(`/openapi/v1.json`) and is available only in Development. Calls go to the same origin, so after you call
`POST /api/auth/sessions` the browser keeps the session cookie. Swagger also adds the required
`X-CSRF-Token` header to every POST, PUT, and DELETE automatically.

Seeded development accounts, all using the `DevelopmentSeed:Password` value:

| Email | Role(s) |
|---|---|
| `admin@iit.test` | Admin |
| `student.personal@example.test` | Student |
| `teacher@iit.test` | Teacher |
| `coordinator@iit.test` | Coordinator |
| `teacher.coordinator@iit.test` | Teacher and Coordinator; default role Teacher (switch with `PUT /api/auth/sessions/current/active-role`) |

### Changing an account's default role

Until administrator user management exists, set `AspNetUsers.DefaultRole` directly in SQL. The value must be
`Admin`, `Student`, `Teacher`, or `Coordinator`, matching capitalization, and one of the account's assigned
roles; otherwise sign-in falls back to the order Admin, Coordinator, Teacher, Student. `NULL` also means
"use the fallback". The change applies at the user's next sign-in, and the seeder never overwrites it.

```sql
UPDATE "AspNetUsers" u
SET    "DefaultRole" = 'Coordinator'
WHERE  u."NormalizedEmail" = UPPER('teacher.coordinator@iit.test')
AND    EXISTS (SELECT 1 FROM "AspNetUserRoles" ur JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
               WHERE ur."UserId" = u."Id" AND r."Name" = 'Coordinator');
```

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:Portal` | PostgreSQL connection string (keep it in user secrets or environment variables) |
| `PasswordRecovery:ResetPageUrl` | Absolute URL of the frontend reset page; required at startup |
| `PasswordRecovery:ProofLifespan` | How long a recovery proof stays valid (default `01:00:00`) |
| `Smtp:*` | `Host`, `Port`, `EnableSsl`, `UserName`, `Password`, `FromAddress`; or `PickupDirectory` for a local sink |
| `RateLimiting:Authentication:*` | Per-client `PermitLimit` and `Window` for sign-in and recovery (default 10 per minute) |

## Authentication model

- `POST /api/auth/sessions` checks the email and password with Identity, then creates an `AuthSessions` row. The
  browser receives an opaque handle in the `__Host-iit-session` cookie (`HttpOnly`, `Secure`,
  `SameSite=Strict`, host-only). Only the handle's SHA-256 digest is stored.
- Every request checks the session server-side: it must not be revoked, and its active role must still be
  assigned to the account. Only the active role is issued as a role claim, so a multi-role user never gets the
  union of their roles.
- **Sessions end after three hours without authenticated activity.** Each authenticated request refreshes the
  window, and an idle session is revoked on its next use. Sign-out revokes only the current session; a password
  reset revokes only the session that performed it.
- Each session starts in the account's default role (`AspNetUsers.DefaultRole` while assigned, otherwise the
  first assigned role in the order Admin, Coordinator, Teacher, Student).
- State-changing requests need the `X-CSRF-Token` header from `GET /api/auth/anti-forgery-token`.
- Module access uses the `AdminModule`, `StudentModule`, `TeacherModule`, and `CoordinatorModule` policies.
  Record access uses `IAuthorizationService.AuthorizeAsync(User, record, PortalPolicies.RecordAccess)` with
  records that implement `IStudentOwnedRecord`, `ITeacherAssignedRecord`, or `ICoordinatorAssignedRecord`.

## Deployment notes

- Persist the ASP.NET Core Data Protection key ring, for example to shared storage. Recovery proofs and
  anti-forgery tokens depend on it. Session cookies do not.
- Behind a reverse proxy, configure forwarded headers so rate limiting partitions by the real client address.
