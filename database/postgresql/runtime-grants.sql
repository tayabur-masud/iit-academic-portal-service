-- Grants the restricted runtime role what the API needs, and limits audit history to append-only.
--
-- Run as the schema owner (the account that applies the EF Core migrations), after the migrations, with:
--   psql -v runtime_role=iit_portal_app -d iit_academic_portal -f database/postgresql/runtime-grants.sql
-- Re-running is safe. The runtime role must not own any table: ownership would let it change its own grants.
--
-- The API connects as the runtime role (ConnectionStrings:Portal). `dotnet ef` connects as the schema owner
-- (ConnectionStrings:PortalMigrations).

GRANT USAGE ON SCHEMA public TO :"runtime_role";

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"runtime_role";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"runtime_role";

-- Audit history is append-only for the runtime role: it can add and read events, never change or remove them.
REVOKE UPDATE, DELETE, TRUNCATE ON "AuditEvents" FROM :"runtime_role";

-- The outbox changes delivery state, but a row is never deleted by the runtime role, so an event cannot be
-- discarded silently. Operator recovery runs as the schema owner (audit-outbox-recovery.sql).
REVOKE DELETE, TRUNCATE ON "SecurityAuditOutbox" FROM :"runtime_role";

-- Migration history belongs to the schema owner.
REVOKE ALL ON "__EFMigrationsHistory" FROM :"runtime_role";

-- Tables and sequences created by later migrations (run by this same schema owner) are usable immediately.
-- A migration that adds another append-only table must revoke UPDATE and DELETE on it, as above.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"runtime_role";
