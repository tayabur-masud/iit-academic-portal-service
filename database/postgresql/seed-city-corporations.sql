-- Seeds the 13 city corporations of Bangladesh into "CityCorporations" (Configuration > Geography).
--
-- Run as the schema owner (the account that applies the EF Core migrations) after the migrations and the other geography seeds:
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-divisions.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-districts.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-upazilas.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-city-corporations.sql
--
-- The identifiers are fixed. Each Id is a version 5 UUID under the namespace
-- 028436bf-43b6-5975-9aac-7820e49ed20d (uuid5 of the URL urn:iit-academic-portal:configuration:geography)
-- of "city-corporation|DIVISION|DISTRICT|NAME", using normalized (upper case) names, and is written out below, so
-- every new database gets exactly the same Ids and nothing depends on gen_random_uuid().
--
-- It is safe to run again, here or on any other database: ON CONFLICT DO NOTHING skips a row that is already there (same Id, or the
-- same live normalized name within its district) and never overwrites one, so edits made later in the Configuration screen are kept.
-- A row an Admin deleted in the screen is only marked deleted, so its Id is still there and the script does not bring it back.
--
-- Each city corporation's district is found by its normalized division and district names among rows that are not deleted, so this works
-- whether the district was added by seed-districts.sql or already existed with another Id. Run seed-divisions.sql and seed-districts.sql
-- first: a city corporation whose district is missing, or was deleted, stops the script with an error and nothing is added.
--
-- After inserting, the script checks that every seeded row is present, either by its fixed Id (so a row an Admin renamed still counts) or by its
-- name under its district; if one is not, it stops with an error and the whole run is rolled back.
--
-- Created and last-modified (who and when): the times take the database default (the moment the script runs) and the created-by and
-- last-modified-by columns stay empty, because no signed-in account loaded these rows; the portal shows that as System.
--
-- NormalizedName follows the application rule: trimmed, repeated whitespace collapsed, invariant upper case. Geographic records have no
-- code, so the usual abbreviations (BCC, DNCC, DSCC, and so on) are not stored. This script does not go through the application, so it
-- writes no audit events. The list is the project owner's (2026-10-21): the 12 city corporations by district, with Dhaka district having
-- two (North and South); no official source or version was cited.

BEGIN;

CREATE TEMP TABLE seed_city_corporations ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('f04584c2-0695-5273-bed2-8dead154a672', 'BARISHAL', 'BARISHAL', 'Barishal City Corporation', 'BARISHAL CITY CORPORATION'),
    ('83d0c578-857d-50cc-89fa-1b05090214b0', 'RAJSHAHI', 'BOGURA', 'Bogura City Corporation', 'BOGURA CITY CORPORATION'),
    ('ac72c12b-9b81-5222-8c47-4183815b90ad', 'CHATTOGRAM', 'CHATTOGRAM', 'Chattogram City Corporation', 'CHATTOGRAM CITY CORPORATION'),
    ('4a32bcd1-0021-5ff6-a7b7-8e0673e3b7e1', 'CHATTOGRAM', 'CUMILLA', 'Cumilla City Corporation', 'CUMILLA CITY CORPORATION'),
    ('2b60ab6a-09dd-5bdd-bb66-96f63b545931', 'DHAKA', 'DHAKA', 'Dhaka North City Corporation', 'DHAKA NORTH CITY CORPORATION'),
    ('3267734d-f7ec-5adf-86ab-197729feb420', 'DHAKA', 'DHAKA', 'Dhaka South City Corporation', 'DHAKA SOUTH CITY CORPORATION'),
    ('5080fc9f-1036-5599-927e-c08c97321e73', 'DHAKA', 'GAZIPUR', 'Gazipur City Corporation', 'GAZIPUR CITY CORPORATION'),
    ('1fa4f64a-b17e-5755-9691-e8ee073cece5', 'KHULNA', 'KHULNA', 'Khulna City Corporation', 'KHULNA CITY CORPORATION'),
    ('e3f6287c-caca-5071-9eb5-c6d60615a72a', 'MYMENSINGH', 'MYMENSINGH', 'Mymensingh City Corporation', 'MYMENSINGH CITY CORPORATION'),
    ('f5ef9be8-e56e-58e3-9193-3f18b7facf0b', 'DHAKA', 'NARAYANGANJ', 'Narayanganj City Corporation', 'NARAYANGANJ CITY CORPORATION'),
    ('6258d604-d72d-573b-9a49-e936a14d959d', 'RAJSHAHI', 'RAJSHAHI', 'Rajshahi City Corporation', 'RAJSHAHI CITY CORPORATION'),
    ('41dce7c4-f4fe-534a-97c5-18016ede2a08', 'RANGPUR', 'RANGPUR', 'Rangpur City Corporation', 'RANGPUR CITY CORPORATION'),
    ('e35da00d-3c4f-5d3b-bac4-d09b2db7b71c', 'SYLHET', 'SYLHET', 'Sylhet City Corporation', 'SYLHET CITY CORPORATION')
) AS v (id, division, district, name, normalized);

INSERT INTO "CityCorporations" ("Id", "DistrictId", "Name", "NormalizedName", "IsActive")
SELECT s.id::uuid, t."Id", s.name, s.normalized, true
FROM seed_city_corporations s
JOIN "Divisions" d ON d."NormalizedName" = s.division AND NOT d."IsDeleted"
JOIN "Districts" t ON t."DivisionId" = d."Id" AND t."NormalizedName" = s.district AND NOT t."IsDeleted"
ON CONFLICT DO NOTHING;

DO $$
DECLARE missing integer;
BEGIN
    SELECT count(*) INTO missing
    FROM seed_city_corporations s
    WHERE NOT EXISTS (SELECT 1 FROM "CityCorporations" c WHERE c."Id" = s.id::uuid)
      AND NOT EXISTS (
        SELECT 1 FROM "CityCorporations" c
        JOIN "Districts" t ON t."Id" = c."DistrictId"
        JOIN "Divisions" d ON d."Id" = t."DivisionId"
        WHERE d."NormalizedName" = s.division AND t."NormalizedName" = s.district AND c."NormalizedName" = s.normalized
          AND NOT c."IsDeleted" AND NOT t."IsDeleted" AND NOT d."IsDeleted");
    IF missing > 0 THEN
        RAISE EXCEPTION '% city corporation(s) could not be added because their district is missing or was deleted; run seed-divisions.sql and seed-districts.sql first (a deleted district stays deleted).', missing;
    END IF;
END $$;

COMMIT;

SELECT count(*) AS city_corporations_in_table FROM "CityCorporations";
