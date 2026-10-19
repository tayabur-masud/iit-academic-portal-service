# IIT Academic Portal Service

ASP.NET Core 10 Web API (C# 14) for the IIT Academic Portal: a layered monolith using ASP.NET Core
Identity 10, EF Core 10, and PostgreSQL 18 through Npgsql 10.x.

| Project | Layer |
|---|---|
| `src/IitAcademicPortal.Domain` | Entities and rules with no infrastructure: `PortalUser`, `PortalRoles`, `AuthSession`, record-boundary interfaces |
| `src/IitAcademicPortal.Application` | Use cases: sign-in and session state, password recovery, password policy, record-access rules, audit recording and review, Admin configuration (catalogs, batches, geography), teacher management, notifications (templates, history, resend, reminders) |
| `src/IitAcademicPortal.Infrastructure` | EF Core `PortalDbContext`, migrations, session repository, email adapters (SMTP, and Brevo for Development only), the durable notification store and dispatch worker, audit store, security-event outbox worker and fallback sink |
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

In Development, emails (password recovery and every other notification) are written as `.eml` files to
`src/IitAcademicPortal.Api/.mail/` instead of being sent, unless you select the Brevo provider (see "Notifications").

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
| `PasswordRecovery:RequestLifetime` | How long an accepted recovery request waits to be processed before it is discarded unprocessed (default `01:00:00`) |
| `AccountCredentialSetup:SetupPageUrl` | Absolute URL of the frontend account-setup page. Empty means `{Notifications:PortalUrl}/account-setup`; Production must use the public https portal address |
| `AccountCredentialSetup:ProofLifespan` | How long a setup link stays valid after it is generated (default `3.00:00:00`). Links are also single-use and stop working after a reissue or an email change |
| `Smtp:*` | `Host`, `Port`, `EnableSsl`, `UserName`, `Password`, `FromAddress`; or `PickupDirectory` for a local sink |
| `Email:Provider` | `Smtp` (default) or `Brevo`. Brevo is Development only: startup fails in any other environment (see "Notifications") |
| `Brevo:*` | `ApiKey` and `SenderEmail` (user secrets or environment only), `AllowedRecipients` (exact test mailboxes; empty rejects every send), `SenderName`, `RequestTimeout` |
| `Notifications:PortalUrl` | The sign-in address messages point to (the `portalUrl` placeholder) |
| `Notifications:ShowLogo` | `true` (default) shows the logo image; `false` shows the portal name as text instead. Set it to `false` when `PortalUrl` is http (Development), because the service refuses to start with an http logo address |
| `Notifications:LogoUrl` | Absolute https address of the logo at the top of every email (default `{PortalUrl}/images/iit-logo.png`). Mail clients load it from the internet, so it must be publicly reachable |
| `Notifications:BrandColor`, `PortalName`, `LogoAlt`, `FooterText`, `ButtonLabel` | Email branding (defaults are the IIT values). `BrandColor` is a six-digit hex value such as `#2E3192` with at least 4.5:1 contrast against white; the text values are single lines of at most 300 characters. An invalid value stops the service from starting |
| `NotificationDispatch:*` | Notification worker: `WorkerEnabled`, `PollInterval` (5 s), `BatchSize` (50), `MaxAttempts` (8), `BaseRetryDelay` (5 s), `MaxRetryDelay` (15 min), `LeaseDuration` (2 min) |
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
- Only an account whose `AccountStatus` is `Active` can sign in or keep using a session. Accounts that existed before the column are `Active`; Teacher Management creates accounts as `PendingCredentialSetup` (no password, no email; they cannot sign in until a later credential-setup feature) and deactivation sets `Deactivated` and revokes every active session. A refused sign-in looks exactly like a wrong password.
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

## Notifications (Admin)

One channel-independent capability sends every account and academic email (specs: `specs/005-notification-management/`). A feature
asks for a **business event** and its recipients; it never names a channel, a provider, or message wording.

- **Producers** call `INotificationRequestService.RequestAsync(new NotificationEventRequest(eventType, occurrenceId, recipientUserIds, context))`
  after their own action has committed. It never throws and never fails the action: it fixes the template revision and the address, snapshots
  ordinary content, and stores the work durably. The `occurrenceId` must be stable and rebuildable from the owner's own data: replaying the same
  occurrence, recipient, and channel creates nothing new, which is also how an owner reconciles after the operational alarm
  (`INotificationFailureSignal`, a critical log plus the `notifications.persistence_failures` counter) says work was not retained.
- **Eight events**, each with one email template: account setup, teacher invitation, student invitation, email confirmation, password reset
  (security messages, always enabled) and results published, feedback window opened, feedback closing reminder (ordinary; an Admin may disable them, and a disabled
  event is recorded as Suppressed and never replayed).
- **Delivery** is a background worker (`NotificationDispatchWorker`) over PostgreSQL: leased claims, at most 8 attempts with a 5-second base delay
  doubling to 15 minutes, then Failed (resendable). A provider outcome that may have been accepted (a timeout, or a worker that died after the call
  started) is **Uncertain**: never retried and never claimed as sent. `Sent` means the provider accepted it, not that it reached an inbox.
- **Security content is never stored.** For a security event the owning workflow provides an `ISecurityContentProvider` that decides whether the
  message is authorized and produces the link for one delivery attempt, in memory. History keeps only safe facts and `contentWithheld: true`;
  previews show a fixed, nonfunctional link placeholder. Two providers are real: password reset (`PasswordResetContentProvider`, which
  also refuses an account still pending setup) and account setup (`AccountCredentialSetupSecurityContentProvider`, see User Management below),
  each with its own Identity token provider and purpose. The older per-role invitation events and email confirmation have no producer and no
  link provider, so a notification for them **fails visibly** (`SetupLinkProviderMissing`, `ConfirmationProviderMissing`) and nothing is sent.
  Never reuse a password-reset proof for account setup, or the reverse.
- **Password recovery** keeps its generic public response. An accepted request is retained durably (address protected with Data Protection) until
  the worker resolves the account; an unknown address leaves no permanent record. The old in-memory queue and worker are gone.
- **Admin API** (`/api/admin/notification-templates`, `/api/admin/notifications`, `/api/admin/notification-settings/feedback-reminder`): edit
  templates (each change is an immutable revision; stale edits get 409), preview, search history (keyset batches, 25 by default and 100 at most,
  UTC half-open date range), safe detail, resend a Failed record (new linked record, `Idempotency-Key`, predecessor untouched), and the single
  portal-wide reminder lead time. History is append-only: the runtime database role cannot update or delete `NotificationRecords`,
  `NotificationTemplateRevisions`, or `NotificationAttempts` (see `database/postgresql/runtime-grants.sql`).
- **Academic producers** (`IAcademicNotificationProducers`, and the reminder scheduler over `IFeedbackWindowSource`) are contract-only: no results
  or feedback workflow exists yet, so they are tested with fake owners and nothing calls them in production. Each owner must call its contract after
  its own commit and prove that with its own tests before the behavior is claimed complete.

**Branded layout.** Every email is sent as plain text plus one IIT-branded HTML layout (logo, the subject as heading, the message, a footer). Admins
edit only the plain-text wording; the layout is built in memory at send time (`EmailLayout`), every value is HTML-encoded, only whole http(s)
addresses become links (an address on its own line becomes a button), and nothing HTML is stored. Each event keeps its own template; the layout is
shared so all messages look alike. The logo address, brand color, portal name, logo alt text, footer, and button label are configuration (see the table), validated at startup, and applied to messages not yet sent; they are not editable in the portal. A preview shows the same layout with sample data.

**Email provider.** `Email:Provider` selects the adapter behind `IEmailSender`; no producer changes when it changes. `Smtp` is the default.
`Brevo` is **Development only**, for synthetic test identities: put the key and sender in user secrets
(`dotnet user-secrets set "Brevo:ApiKey" "<key>"`, `dotnet user-secrets set "Brevo:SenderEmail" "<verified sender>"`,
`dotnet user-secrets set "Brevo:AllowedRecipients:0" "<test mailbox>"`), never in source. Every destination must be in `Brevo:AllowedRecipients`
(exact addresses, case-insensitive); a destination outside the list, or an empty list, is rejected before any HTTP request. The adapter uses only
the transactional send endpoint, never Brevo's content-retrieval or tracking features. In any other environment the service refuses to start if
Brevo is selected or a Brevo key is present. A different provider must be chosen and its content retention and access verified against the
no-stored-security-content rule before Production.

**Channels.** Notifications are delivered through `IDeliveryChannel` adapters; production has one, email (`EmailDeliveryChannel`, which builds the
branded HTML and calls the provider). A new channel is a new adapter plus a seeded template: producers never name a channel, so none of them changes.
`NotificationChannel.Test` exists only for the conformance test in `NotificationChannelIndependenceTests` and is never seeded or registered in production.

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

## Teacher management (Admin)

Admins add, update, find, and deactivate teachers under `/api/admin/teachers` (contract: `specs/004-teacher-management/contracts/`). A teacher is one `PortalUser` plus one `TeacherProfile`; roles stay in Identity.

- Creating a teacher makes a **pending** account with the Teacher role as its default. No password is set; after the commit the common `AccountCredentialSetup` email is requested (see User Management), and the account cannot sign in until the teacher uses it. Reactivating a deactivated teacher is not offered.
- Role rules: Teacher stays while the profile is active, Admin may be assigned or removed, Student is refused, Coordinator is not editable here, and the default role must be assigned. The last active Admin can neither lose Admin nor be deactivated (409); the check runs under a PostgreSQL advisory lock inside the change's transaction.
- Deactivation never deletes anything: it makes the profile inactive, disables the account, and revokes all sessions, in one transaction with its audit event.
- Every multi-step change runs in one transaction with its staged `teacher.*` audit event (`TeacherAuditEventDefinitions`; email and phone are recorded as "changed" only).
- **For the future batch-coordinator feature:** call `ITeacherCoordinatorRoleSynchronizer.SynchronizeCoordinatorRoleAsync(teacherUserId, hasActiveCoordinatorAssignments, ct)` in the same unit of work as every assignment create, change, or end, after working out whether any active assignment remains, and save once. It stages the Coordinator role and its audit event without saving. Add your own tests proving each assignment operation calls it; Teacher Management only tests the synchronizer itself.

## User management (Admin) and account setup

Admins manage every portal account under `/api/admin/users` (contract: `specs/006-user-management/contracts/`). There is no new account table:
accounts are `PortalUser` with Identity roles, `AccountStatus`, and `DefaultRole`.

- **Directory.** Search by registered email (contains, case-insensitive) or exact user ID, filter by role and status, 25 per page by default
  and 100 at most, ordered by normalized email then user ID. Responses never carry a password, hash, security stamp, proof, or link;
  `concurrencyStamp` is Identity's optimistic-concurrency value, sent back with an email change.
- **Provisioning.** `POST /api/admin/users` creates only **Admin** or **Student** accounts: pending, no password, that single role as the
  default, committed with an `account.created` audit event. Teacher accounts are created by Teacher Management (409 here) and Coordinator
  comes from batch-coordinator assignments (409 here). A duplicate normalized email is a 409.
- **Maintenance.** Email changes need `expectedConcurrencyStamp` (stale or duplicate: 409) and rotate the security stamp. Role changes follow the
  policy: a Student account is Student-only, Coordinator is never accepted, Teacher needs a teacher profile, the default must be assigned.
  Deactivation keeps the account, roles, and history, revokes every session (`AccountDeactivated`), refuses the caller's own account and the
  last active Admin. For an account with a teacher profile, email, role, and deactivation requests are delegated to Teacher Management.
  Everything that can remove an active Admin takes the same PostgreSQL advisory lock as Teacher Management (`ITeacherStore.LockAdminInvariantAsync`).
- **Account setup (every new account).** Each account owner requests `AccountCredentialSetupService.InitialRequest(userId)` after its own
  commit: generic provisioning and Teacher Management do; the future batch-coordinator owner must too. The security-content provider re-checks
  that the account still exists, still has the accepted address, and is still pending, then generates a token from the dedicated
  `PortalAccountCredentialSetup` provider and purpose (lifespan `AccountCredentialSetup:ProofLifespan`, 3 days by default) and builds
  `{AccountCredentialSetup:SetupPageUrl}?userId=...&proof=...` (default page: `{Notifications:PortalUrl}/account-setup`). Nothing about the
  proof is stored or logged.
- **Link check.** `POST /api/auth/account-credential-setup-checks` (public, anti-forgery, rate-limited) takes `userId` and `proof` and answers
  only `usable`; the setup page uses it so a used or replaced link goes straight to a sign-in message instead of a password form.
- **Completion.** `POST /api/auth/account-credential-setups` (public, anti-forgery, rate-limited) takes `userId`, `proof`, `newPassword`. In one
  transaction it adds the password, makes the account Active, rotates the security stamp (so the link cannot be replayed), and stages
  `account.credential-setup.completed`. Every invalid, expired, used, or stale link gets the same generic 400 and an
  `auth.credential-setup.rejected` security event with no account.
- **Reissue.** `POST /api/admin/users/{userId}/credential-setup-reissues` works only for a pending account: it rotates the security stamp (old
  links stop working), audits `account.credential-setup.reissued`, and requests a new occurrence. It is not the generic failed-notification resend.

## Student management (Admin)

Admins enter admissions from the paper form under `/api/admin/students` (contract: `specs/007-student-management/contracts/`). A person has
one `StudentProfile` linked to one Student account (the account keeps the email); each program enrollment is a `StudentAdmissionRecord`
(one per Degree and Batch); prior education is `StudentAcademicHistory` on the profile, with every value kept as text.

- **First admission** for an email creates a pending, passwordless Student account, the profile, and the admission in one transaction with
  their audit events, then requests the common `AccountCredentialSetup` email. A **returning Student's** email reuses the account and
  profile and never requests setup again; an email of any other kind of account is a 409. `GET /api/admin/students/lookup?email=` tells
  the form which case applies and returns the profile to prefill.
- **Hall and registration number** are optional at admission and updated independently; a newly chosen Hall must be active; the
  registration number is unique across all admissions (case- and space-insensitive).
- **Addresses** store free-text details and an Upazila; District and Division are always derived from it.
- **Academic history** chooses the school or college, university, institution, board, and group from the Configuration catalogs (institutions
  filtered by type), storing the reference plus a name snapshot that later renames never rewrite; a value not in a catalog is typed and kept
  as text. Other history values are text as written on the form.
- **Graduation eligibility** (`GET /api/admin/students/{admissionId}/graduation-eligibility`) compares the Degree's
  `CreditsToBeCompleted` with the passed-course credits of that admission from `ICompletedCreditsProvider`, and needs the Hall and
  registration number. Nothing is stored and results are never blocked. **For the academic-results feature:** register an
  `ICompletedCreditsProvider` keyed by admission ID; until then `UnavailableCompletedCreditsProvider` reports the credits as unavailable,
  so no admission is eligible.
- Deleting a Degree, Batch, Hall, Upazila, educational institution, education board, or education group that a student uses is refused
  (deactivate it instead).

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

- Persist the ASP.NET Core Data Protection key ring, for example to shared storage. Recovery proofs,
  anti-forgery tokens, and the protected addresses of retained password-recovery requests depend on it. Session cookies do not.
- Notifications need: a Production email provider other than Brevo (the service will not start with Brevo outside Development), the
  runtime grants script re-run after the `NotificationManagement` migration, and a named owner for the `notifications.persistence_failures`
  and `notifications.retry_exhausted` signals (replaying a source occurrence reconciles the first).
- Behind a reverse proxy, set `ForwardedHeaders:KnownProxies` so rate limiting and the audit source use the real
  client address.
- Decided (2026-10-09): production uses the `File` fallback sink on a persistent volume, and 1,000,000 events is the
  confirmed volume for the search target. Console logging does not meet the durable-sink requirement.
- **Still to decide before go-live**: the protected alert destination for the audit signals and the person
  authorized to recover exhausted security events.
