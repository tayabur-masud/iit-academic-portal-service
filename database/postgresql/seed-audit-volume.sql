-- DEVELOPMENT AND MEASUREMENT ONLY. Never run this against a shared, staging, or production database.
--
-- Loads synthetic audit events to measure first-page search time (SC-006). Run it against a disposable database
-- that has the migrations applied, as the schema owner:
--   psql -v volume=1000000 -d <disposable database> -f database/postgresql/seed-audit-volume.sql
--
-- Events are spread over 400 days with a realistic mix of types, outcomes, and actors. Every row is marked with
-- the source '198.51.100.1' (a documentation address) so it can be told apart from real events.

\if :{?volume}
\else
  \set volume 1000000
\endif

INSERT INTO "AuditEvents"
    ("Id", "OccurredAtUtc", "Category", "EventType", "Outcome", "ActorUserId", "EntityType", "EntityId",
     "CorrelationId", "Source", "MetadataJson", "ChangesJson")
SELECT
    gen_random_uuid(),
    now() - (g * 400.0 * 86400 / :volume) * interval '1 second',
    CASE WHEN g % 4 = 0 THEN 'business' ELSE 'security' END,
    (ARRAY['auth.sign-in', 'auth.session.revoked', 'auth.role.switched', 'access.denied',
           'audit.review.accessed', 'student.updated', 'course.enrolled'])[1 + g % 7],
    (ARRAY['success', 'success', 'success', 'failure', 'denied'])[1 + g % 5],
    CASE WHEN g % 10 = 0 THEN NULL ELSE 'seed-user-' || (g % 5000) END,
    CASE WHEN g % 4 = 0 THEN 'Student' END,
    CASE WHEN g % 4 = 0 THEN (g % 20000)::text END,
    'seed-' || g,
    '198.51.100.1',
    NULL,
    NULL
FROM generate_series(1, :volume) AS g;

ANALYZE "AuditEvents";
SELECT count(*) AS seeded_events FROM "AuditEvents" WHERE "Source" = '198.51.100.1';
