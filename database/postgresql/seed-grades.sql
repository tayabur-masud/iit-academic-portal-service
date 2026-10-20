-- Seeds the initial Grade scale into "Grades" (Configuration > IIT academics > Grades): the ten bands shared by every degree.
--
-- Run as the schema owner (the account that applies the EF Core migrations) after the migrations:
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-grades.sql
--
-- The identifiers are fixed. Each Id is a version 5 UUID under the namespace
-- 028436bf-43b6-5975-9aac-7820e49ed20d (the same namespace as the geography seeds) of "grade|LETTER", using the normalized
-- (upper case) letter grade, and is written out below, so every new database gets exactly the same Ids.
--
-- The bands must stay one complete scale: the active bands cover every percentage from 0 through 100 exactly once. Adding the missing
-- rows one by one, as the geography seeds do, could break that next to a scale an Admin has changed, so this script seeds only an EMPTY
-- table. If "Grades" holds any row, live or deleted, it adds nothing and says so; edits and deletions made in the Configuration screen are
-- therefore never overwritten or undone. To reseed, remove the rows first with DELETE FROM "Grades" (nothing else refers to a grade);
-- deleting them in the screen only marks them deleted, which still counts as rows.
--
-- Each lower bound is inclusive and each upper bound exclusive, except that A+ includes 100. Numeric grades are on the 4.00 scale.
-- Created and last-modified (who and when): the times take the database default (the moment the script runs) and the created-by and
-- last-modified-by columns stay empty, because no signed-in account loaded these rows; the portal shows that as System. This script does
-- not go through the application, so it writes no audit events. The values are the project owner's initial scale (feature 003 spec).

BEGIN;

-- Serializes with a Grade scale replacement made in the portal at the same moment (the same advisory lock the application takes).
SELECT pg_advisory_xact_lock(3003014);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "Grades") THEN
        RAISE NOTICE 'Grades already holds rows, so nothing was added. To reseed, remove them with DELETE FROM "Grades" and run this script again.';
        RETURN;
    END IF;

    INSERT INTO "Grades" ("Id", "Name", "NormalizedName", "IsActive", "MarksLowerBound", "MarksUpperBound", "UpperBoundInclusive", "NumericGrade", "Comment")
    SELECT v.id::uuid, v.letter, v.normalized, true, v.lower, v.upper, v.inclusive, v.numeric, v.comment
    FROM (VALUES
        ('0ebd496e-c1c2-572e-9973-00adb99eee78', 'A+', 'A+', 80, 100, true, 4.00, 'Excellent'),
        ('85eac169-0145-55d8-8790-8d348e1a80e7', 'A', 'A', 75, 80, false, 3.75, 'Better'),
        ('1d39661a-064c-58e1-a8c2-afe3ce1bbf23', 'A-', 'A-', 70, 75, false, 3.50, 'Good'),
        ('d5e267a5-356d-5590-8f7f-030b01c5ec15', 'B+', 'B+', 65, 70, false, 3.25, 'Above Average'),
        ('f1327f34-5a08-56af-af5d-df17a64c024a', 'B', 'B', 60, 65, false, 3.00, 'Average'),
        ('688bb921-42b9-5f84-9958-ca41db03dfc9', 'B-', 'B-', 55, 60, false, 2.75, 'Below Average'),
        ('afcee7ed-eda7-5e03-844a-01aea8ffe60f', 'C+', 'C+', 50, 55, false, 2.50, 'Satisfactory'),
        ('f58dc5e2-77d5-52be-9126-e96171d660a4', 'C', 'C', 45, 50, false, 2.25, 'Not satisfactory'),
        ('5ee0ac05-278c-502e-a735-505d915788b1', 'D', 'D', 40, 45, false, 2.00, 'Pass'),
        ('309422f3-8bd8-53db-93b3-43be71326a9d', 'F', 'F', 0, 40, false, 0.00, 'Fail')
    ) AS v (id, letter, normalized, lower, upper, inclusive, numeric, comment);
END $$;

COMMIT;

SELECT count(*) AS grades_in_table FROM "Grades" WHERE NOT "IsDeleted";
