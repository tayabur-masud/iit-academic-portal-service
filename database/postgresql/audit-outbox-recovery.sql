-- Authorized recovery for security audit events that used every automatic delivery attempt.
--
-- Apply once as the schema owner (the account that applies the EF Core migrations), after the migrations:
--   psql -d iit_academic_portal -f database/postgresql/audit-outbox-recovery.sql
--
-- It creates one function. Only the schema owner can run it: execution is revoked from everyone else, including
-- the API's runtime role, so recovery is a deliberate operator action and never something the application does.
--
--   -- Put exhausted items back in the queue so the outbox worker retries them:
--   SELECT audit_outbox_recover('retry', '<operator user id>', '<why>');
--   -- Mark exhausted items as dealt with outside the system (for example, after restoring the event from the
--   -- fallback sink); the envelope stays in the table:
--   SELECT audit_outbox_recover('handled', '<operator user id>', '<why>');
--   -- Either action can be limited to one event:
--   SELECT audit_outbox_recover('retry', '<operator user id>', '<why>', '<event id>');
--
-- It returns the number of items changed. It requires an operator ID and a reason. It only ever changes items in
-- the Exhausted state, never deletes anything, never edits an event envelope, and never touches the formal
-- "AuditEvents" table. The operator, time, and reason of the last recovery action are kept on the item.

CREATE OR REPLACE FUNCTION audit_outbox_recover(
    p_action text,
    p_operator_id text,
    p_reason text,
    p_event_id uuid DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    affected integer;
BEGIN
    IF p_action IS NULL OR p_action NOT IN ('retry', 'handled') THEN
        RAISE EXCEPTION 'The action must be retry or handled.';
    END IF;

    IF coalesce(btrim(p_operator_id), '') = '' THEN
        RAISE EXCEPTION 'An operator ID is required.';
    END IF;

    IF coalesce(btrim(p_reason), '') = '' THEN
        RAISE EXCEPTION 'A reason is required.';
    END IF;

    IF p_action = 'retry' THEN
        UPDATE "SecurityAuditOutbox"
        SET "State" = 'Pending',
            "AttemptCount" = 0,
            "NextAttemptAtUtc" = NULL,
            "LeaseUntilUtc" = NULL,
            "HandledAtUtc" = now(),
            "HandledBy" = left(p_operator_id, 450),
            "HandledReason" = left(p_reason, 500)
        WHERE "State" = 'Exhausted'
          AND (p_event_id IS NULL OR "EventId" = p_event_id);
    ELSE
        UPDATE "SecurityAuditOutbox"
        SET "State" = 'Handled',
            "LeaseUntilUtc" = NULL,
            "HandledAtUtc" = now(),
            "HandledBy" = left(p_operator_id, 450),
            "HandledReason" = left(p_reason, 500)
        WHERE "State" = 'Exhausted'
          AND (p_event_id IS NULL OR "EventId" = p_event_id);
    END IF;

    GET DIAGNOSTICS affected = ROW_COUNT;
    RETURN affected;
END;
$$;

REVOKE ALL ON FUNCTION audit_outbox_recover(text, text, text, uuid) FROM PUBLIC;
