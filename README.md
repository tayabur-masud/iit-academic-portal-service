# IIT Academic Portal Service

ASP.NET Core 10 Web API (C# 14) for the IIT Academic Portal: a layered monolith using ASP.NET Core
Identity 10, EF Core 10, and PostgreSQL 18 through Npgsql 10.x.

| Project | Layer |
|---|---|
| `src/IitAcademicPortal.Domain` | Entities and rules with no infrastructure: `PortalUser`, `PortalRoles`, `AuthSession`, record-boundary interfaces |
| `src/IitAcademicPortal.Application` | Use cases: sign-in and session state, password recovery, password policy, record-access rules, audit recording and review, Admin configuration (catalogs, batches, geography) |
| `src/IitAcademicPortal.Infrastructure` | EF Core `PortalDbContext`, migrations, session repository, SMTP email, recovery queue, audit store, security-event outbox worker and fallback sink |
| `src/IitAcademicPortal.Api` | Thin controllers, session-cookie authentication, anti-forgery, policies, rate limiting, problem details, audit review endpoints and health check |
| `tests/IitAcademicPortal.Api.Tests` | xUnit API tests over the real pipeline (in-memory SQLite) plus unit tests |

## Development

Requires the .NET 10 SDK and PostgreSQL 18. Run these from this folder.

**1. Database connection.** `appsettings.Development.json` points at a local `iit_academic_portal` database as
`postgres`/`postgres`. If your local credentials differ, override them in user secrets. The Api project already
has a `UserSecretsId`, so secrets stay on your machine, outside the repository.

```powershell
dotnet user-secrets --project src/IitAcademicPortal.Api set "ConnectionStrings:Portal" "Host=localhost;Port=5432;Database=iit_academic_portal;Username=postgres;Password=<password>"
```

**2. Create or upgrade the database.** `dotnet ef` connects with `ConnectionStrings:PortalMigrations` when it is
set (the schema-owner account) and otherwise falls back to `ConnectionStrings:Portal`, so a single local
`postgres` account keeps working in Development. It creates the database if it does not exist.
Deployments use two accounts; see [Database accounts](#database-accounts).

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
| `ConnectionStrings:PortalMigrations` | Schema-owner connection used only by `dotnet ef`; the API never uses it |
| `RateLimiting:Authentication:*` | Per-client `PermitLimit` and `Window` for sign-in and recovery (default 10 per minute) |
| `ForwardedHeaders:KnownProxies` | IP addresses of trusted reverse proxies. Only requests from these proxies may supply the client address (see below) |
| `AuditFallback:Sink`, `AuditFallback:Directory` | Durable sink for security events whose outbox write failed. Required outside Development (see below) |
| `AuditDelivery:*` | Outbox worker: `WorkerEnabled`, `PollInterval`, `BatchSize`, `MaxAttempts` (8), `BaseRetryDelay`, `MaxRetryDelay`, `LeaseDuration` |

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

## Audit

The service records important events as append-only audit history, and Admins review it at `GET /api/audit-events`
(search) and `GET /api/audit-events/{eventId}` (detail). Search pages either by `cursor` (no count, no shifting while new events arrive) or by `page` (1-based; also returns `totalCount` and `totalPages`; the frontend uses this). The two cannot be combined. Both require the **active** Admin role; `/admin/audit` in
the frontend uses them. Contract: `specs/002-audit-system/contracts/audit.openapi.json`.

- **Business events** are written in the same transaction as the business change, so both commit or neither does.
- **Security events** (sign-in, sign-out and revocation, password reset, role switch, 403 denials, audit reads and
  attempted audit changes) go through a PostgreSQL outbox and a background worker, so a failure never changes
  the response a user gets.
- Anonymous 401 responses are counted (`audit.anonymous_unauthorized`), not recorded.
- `POST`, `PUT`, `PATCH`, and `DELETE` on the audit routes return `405` with `Allow: GET` and are recorded.
- Events never contain passwords, reset proofs, tokens, cookies, session handles, or non-approved personal
  values. The source is the client IP, taken only from trusted forwarded headers.

### Forwarded headers

Behind a reverse proxy, list its address so the audit source and rate limiting see the real client:

```json
"ForwardedHeaders": { "KnownProxies": ["10.0.0.5"] }
```

Without a known proxy, forwarded headers are ignored and the source is the direct connection address, so a client
cannot invent its own address.

### Database accounts

Use two PostgreSQL accounts outside local development:

| Account | Used by | Connection string | Rights |
|---|---|---|---|
| Schema owner | `dotnet ef` and the scripts below | `ConnectionStrings:PortalMigrations` | Owns the tables |
| Runtime role | the API | `ConnectionStrings:Portal` | Reads and writes data; audit events are `SELECT` and `INSERT` only; no `DELETE` on the outbox |

After the migrations, as the schema owner (the runtime role must not own any table):

```powershell
dotnet ef database update -p src/IitAcademicPortal.Infrastructure -s src/IitAcademicPortal.Api
psql -v runtime_role=iit_portal_app -d iit_academic_portal -f database/postgresql/runtime-grants.sql
psql -d iit_academic_portal -f database/postgresql/audit-outbox-recovery.sql
```

`runtime-grants.sql` also sets default privileges, so tables created by later migrations work without another
grant. Re-running it is safe. The PostgreSQL tests (`AUDIT_TEST_POSTGRES` set to an administrator connection
string) build a disposable database, apply these scripts, and prove the permissions; they are skipped without it.

### Durable fallback sink

If the outbox write itself fails, the event is written to a durable sink instead and a critical alert is logged.
Outside Development the service **refuses to start** unless the sink is configured; console logging is not durable.

```json
"AuditFallback": { "Sink": "File", "Directory": "/var/lib/iit-portal/audit-fallback" }
```

The `File` sink appends JSON lines (one safe event envelope per line) to that folder. Use a persistent volume.
Development uses `.audit-fallback/` next to the API (ignored by git).

### Recovering exhausted security events

When delivery fails `AuditDelivery:MaxAttempts` times, the item is kept as `Exhausted` (never discarded) and
`/health/audit` reports it. An authorized operator, connected as the schema owner, either puts the items back in
the queue or marks them dealt with, always with an operator ID and a reason:

```sql
SELECT audit_outbox_recover('retry',   '<operator user id>', '<why>');
SELECT audit_outbox_recover('handled', '<operator user id>', '<why>', '<event id>');  -- one event
```

It returns the number of items changed, only touches `Exhausted` items, and never changes formal audit events.
The runtime role cannot run it.

### Monitoring and alerts

| Signal | Where | Meaning |
|---|---|---|
| `audit.outbox.write_failures` | meter `IitAcademicPortal.Audit` | A security event could not be queued; the fallback sink was used |
| `audit.outbox.retry_exhausted` | same | An outbox item used every attempt |
| `audit.fallback.failures` | same | The fallback sink also failed |
| `audit.event_loss_risk` | same | An event may have been lost: alert immediately |
| `audit.anonymous_unauthorized` | same | Anonymous 401 responses (not audited as events) |
| Critical structured logs | application logs | One per failure above; they carry the exception type, never its message or payload |
| `GET /health/audit` | HTTP | Admin-only; `503` while any item is `Exhausted` |

Export the meter (for example, with OpenTelemetry) and route alerts on the first four signals to the protected
destination the deployment owner chooses. Alerting never writes another audit event.

### Retention

Nothing in the service deletes, archives, or rewrites audit events, and the runtime role cannot. Any future
lifecycle job needs an approved retention policy first. Delivered outbox rows are working state, not audit
retention.

### Audit integration for feature owners

Each feature records its own events as it is built. Nothing is captured automatically, and there is no public
endpoint for writing events.

1. **Declare the event** in your feature, next to its use case, with the changed fields whose values may be
   recorded:

   ```csharp
   public static readonly AuditEventDefinition StudentUpdated =
       AuditEventDefinition.Business("student.updated", "status", "programme");
   ```

   Fields not listed are omitted, or recorded as "changed" when you use `WithChangeIndicators`. The secret guard
   still masks credential-like names and token-like values even in allowlisted fields.
2. **Stage a business event in the same unit of work** as the change, then save once:

   ```csharp
   recorder.Stage(new AuditEventRequest(StudentUpdated, AuditOutcome.Success)
   {
       EntityType = "Student",
       EntityId = student.Id.ToString(),
       Changes = [new AuditFieldChange("status", oldStatus, newStatus)],
   });
   await db.SaveChangesAsync();   // the change and its event commit together or not at all
   ```

   The actor, correlation ID, and source come from the request, never from client input: if you leave
   `ActorUserId` unset, `Stage` records the signed-in session's user. (Security events do not default it; a failed
   sign-in records no account.)
3. **Record a security event** with `await recorder.RecordSecurityAsync(...)`. It never throws and never changes
   the response.
4. Keep metadata small (4 KB) and free of personal data you do not need; change summaries are limited to 16 KB.

## Entity conventions: who created it and who changed it, and when

**Every business entity records who created it and when, and who last changed it and when.** To get this, derive the entity from
`AuditedEntity` (`IitAcademicPortal.Domain`). Nothing else is needed:

- The four columns (`CreatedAtUtc`, `CreatedByUserId`, `LastModifiedAtUtc`, `LastModifiedByUserId`) are mapped for you, so a new
  migration adds them. The times are required and `timestamptz`; the account ids are optional and at most 450 characters.
- The database context stamps them on **every** save, from any code path, using the server clock and the signed-in account of the current
  request. **Never set them yourself, never accept them in a request model, and never add your own copies.** (Unknown JSON fields are
  rejected, so a client cannot send them.)
- On insert the last-modified pair equals the created pair, so it is never empty. An update, including a deactivate and a soft delete,
  moves only the last-modified pair.
- The "by" values are the stable account id, the same one audit history keeps (no email is copied into the row). They are `null` when no
  signed-in account made the change: a background job, or rows loaded by a SQL seed script (the times then take the database default,
  `now()`). The portal shows a `null` account as "System". A row that existed before the columns were added shows the time of that
  migration and no account.
- Expose them to clients as `createdAtUtc`, `createdByUserId`, `createdBy`, `lastModifiedAtUtc`, `lastModifiedByUserId`, and
  `lastModifiedBy`, where `createdBy` and `lastModifiedBy` are the accounts' current emails resolved when the record is read (absent if the
  account was removed). The Configuration API does this, and its edit screens show a "Record history" panel
  (`specs/design-system.md`). This does not replace audit events: the audit history says *what* changed and the columns say who and when
  *last*.

It is enforced: `AuditedEntityConventionTests` fails the build if an entity in the model neither derives from `AuditedEntity` nor is
listed, with a reason, as exempt. Today the exempt ones are ASP.NET Core Identity's tables and `PortalUser`, `AuthSession`
(its own created, activity, and revocation times), `AuditEvent` (append-only, with its own time and actor), and
`SecurityAuditOutboxItem` (a delivery queue). Add to that list only for infrastructure that records the same facts another way.

## Configuration (Admin reference data)

Admins maintain the reference data other features depend on under `/api/admin/configuration`. Every route needs
the session's **active** Admin role (401 anonymous, 403 any other active role), and every write needs the
`X-CSRF-Token`. Contract: `specs/003-configuration/contracts/configuration-api.md`.

| Resource | Route | Fields besides `name` and `isActive` |
|---|---|---|
| Educational institutions | `institutions` | `type` (School, College, University), optional `code`, optional `address` |
| Education boards, degrees, designations | `education-boards`, `degrees`, `designations` | optional `code` |
| Halls (university residential halls) | `halls` | optional `code`, optional `address` |
| Education groups, examination types, semesters, academic sessions | `education-groups`, `examination-types`, `semesters`, `academic-sessions` | none |
| Batches | `batches` | `degreeId`, `startingSemesterId`, `academicSessionId`, all required |
| Divisions, districts, upazilas | `geography/divisions`, `geography/districts`, `geography/upazilas` | `divisionId` (district), `districtId` (upazila); no codes |

Each collection has `GET` (list), `POST` (create), and `GET`, `PUT`, `DELETE` on `/{id}`. A `PUT` takes the full
writable record; set `isActive: false` to deactivate.

- **Names** are compared after trimming, collapsing repeated whitespace, and upper-casing (so `" sci   ENCE"` equals
  `"Science"`); the name as typed (trimmed) is what is stored and shown. Normalized values are never returned.
- **Uniqueness**: a catalog name is unique within its catalog (the same name may exist in different catalogs);
  non-empty codes are unique within their catalog, and a blank code counts as no code; a batch name is unique within
  its degree whatever its session; district names are unique within a division and upazila names within a district.
  PostgreSQL unique indexes are the last guard when two requests race.
- **References**: a new or changed reference must exist and be active (`400`, keyed by field). An unchanged reference to
  a record that was deactivated later is accepted, so the existing link can still be edited.
- **Deactivate, never cascade**: deactivating a record keeps every link to it and does not touch dependents; it only
  stops new assignments.
- **Delete is a soft delete** and works only while no live record refers to the record (`204`); otherwise it returns `409` and the
  record is unchanged. The row stays in the database, marked with `IsDeleted`, `DeletedAtUtc`, and `DeletedByUserId`, and is hidden from
  every list, search, lookup, and selector; reading, editing, or deleting it again returns `404`. Nothing in the portal restores it. Its
  name and code can be used again, because the unique indexes cover only live rows. A deactivated record that refers to another still
  blocks that delete; a deleted one does not. Every foreign key is still `RESTRICT`, so the database refuses a physical delete of a
  referenced row.
- **Lists** take `search`, `status` (`active` default, `inactive`, `all`), `pageSize` (25, up to 100), and, for districts and
  upazilas, `divisionId` or `districtId`. Page them by `page` (1-based; the response then adds `page`, `totalCount`, and
  `totalPages`, which the frontend uses) or by `cursor` (no count, and the pages never shift while new records arrive). The
  two cannot be combined. Lists return `{ items, nextCursor }` plus the totals in page mode.
- **Errors**: `400` with field-keyed `errors` for missing or invalid values, duplicates, and bad references; fields a
  resource does not use are rejected, and so is any unknown JSON field (a client cannot name the audit actor).
- **Audit**: each successful create, update, deactivate, and delete stages one `configuration.{resource}.{created|updated|
  deactivated|deleted}` event with allowlisted before/after values and commits it with the change; a failed audit write
  rolls the change back. An update that changes nothing writes nothing and records nothing.

### Seed data: halls

`database/postgresql/seed-halls.sql` loads the 23 residential halls and hostels of the University of Dhaka into `Halls`, each with the
default address "University of Dhaka, Dhaka-1000, Bangladesh" and no code. Run it as the schema owner after the migrations:

```powershell
psql -d iit_academic_portal -f database/postgresql/seed-halls.sql
```

- **Fixed identifiers**: each hall's `Id` is a version 5 UUID of its normalized name under a fixed namespace, written out in the script,
  so every new database gets the same ids (no `gen_random_uuid()`).
- **Safe to run again**: `ON CONFLICT DO NOTHING` skips a hall that already exists (same id or same live normalized name) and never
  overwrites a row, so later edits made in the Configuration screen are kept. A seeded hall an Admin deleted stays deleted: its row (and
  fixed id) is still there, so the script does not bring it back. To change a seeded hall, edit it in the screen.
- **Not audited**: a SQL script bypasses the application, so it writes no audit events. Halls the Admin creates in the screen are audited as usual.
- **Created and last modified**: the times take the database default (when the script runs) and the "by" columns stay empty, shown as "System".
- To add or rename a hall in the script itself, edit the names in `database/postgresql/seed-halls.sql` and add the new row with its own
  version 5 id; do not change an existing id.

### Seed data: divisions, districts, upazilas

Three SQL scripts in `database/postgresql/` load Bangladesh's geography, built from the owner's own files (`bd-divisions.json`,
`bd-districts.json`, `bd-upazilas.json`; no official source was cited): `seed-divisions.sql` (8), `seed-districts.sql` (64), and
`seed-upazilas.sql` (492). Run them as the schema owner after the migrations, **in this order**:

```powershell
psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-divisions.sql
psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-districts.sql
psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-upazilas.sql
```

- **Fixed identifiers**: each `Id` is a version 5 UUID of its level and normalized names under a fixed namespace, written out in the
  script, so every new database gets the same ids.
- **Safe to run again**: `ON CONFLICT DO NOTHING` skips a row that already exists (same id, or the same live normalized name within its parent)
  and never overwrites one, so later edits in the Configuration screen are kept. A seeded row an Admin deleted stays deleted: its row and
  fixed id are still there, so the script does not bring it back.
- **Parents are found by name**: a district's division and an upazila's district are looked up by normalized name among rows that are not
  deleted (the scripts are plain SQL, so they check `IsDeleted` themselves), so they also work when a parent already exists under another
  id, and a deleted division or district is never used as a parent. If a parent is missing or was deleted, the script stops with an error
  and adds nothing.
- **Not audited**: SQL scripts bypass the application, so they write no audit events.
- Two exact repeats in Natore (source upazila ids 361 and 362) are left out, and "Raipura, Narsingdi" is stored as "Raipura".
- The tables must not hold other, different data for the same names; the scripts add what is missing and leave the rest.
- Once these scripts have run, the geography tables are not empty, so the import endpoint below refuses to run (409). Use it only on
  empty tables, for example to load an official file that replaces this data after the tables have been emptied.

### Geography import

The SQL scripts above are the repeatable way to load the geography the owner supplied. This endpoint is for a JSON file with an approved source citation (schema:
`specs/003-configuration/contracts/geographic-seed.schema.json`) with a source citation and version. In Swagger,
signed in as an Admin:

1. `POST /api/admin/configuration/geography/import/validate` reports the source, per-level counts, `targetEmpty`, and
   every problem with its path (`400` with the report if the document is invalid). It writes nothing.
2. `POST /api/admin/configuration/geography/import` validates again and, only if the three geography tables are empty,
   adds the whole hierarchy and one `configuration.geography.imported` event in one transaction. A non-empty target
   returns `409`; existing rows are never overwritten.

## Deployment notes

- Persist the ASP.NET Core Data Protection key ring, for example to shared storage. Recovery proofs and
  anti-forgery tokens depend on it. Session cookies do not.
- Behind a reverse proxy, set `ForwardedHeaders:KnownProxies` so rate limiting and the audit source use the real
  client address.
- Decided (2026-10-09): production uses the `File` fallback sink on a persistent volume, and 1,000,000 events is the
  confirmed volume for the search target. Console logging does not meet the durable-sink requirement.
- **Still to decide before go-live**: the protected alert destination for the audit signals and the person
  authorized to recover exhausted security events.
