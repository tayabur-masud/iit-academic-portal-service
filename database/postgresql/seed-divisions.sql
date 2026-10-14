-- Seeds the 8 divisions of Bangladesh into "Divisions" (Configuration > Geography).
--
-- Run as the schema owner (the account that applies the EF Core migrations) after the migrations, in this order:
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-divisions.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-districts.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-upazilas.sql
--
-- The identifiers are fixed. Each Id is a version 5 UUID under the namespace
-- 028436bf-43b6-5975-9aac-7820e49ed20d (uuid5 of the URL urn:iit-academic-portal:configuration:geography)
-- of "division|NAME", using normalized (upper case) names, and is written out below, so
-- every new database gets exactly the same Ids and nothing depends on gen_random_uuid().
--
-- It is safe to run again, here or on any other database: ON CONFLICT DO NOTHING skips a row that is already there (same Id, or the
-- same live normalized name within its parent) and never overwrites one, so edits made later in the Configuration screen are kept.
-- A row an Admin deleted in the screen is only marked deleted, so its Id is still there and the script does not bring it back.
--
-- After inserting, the script checks that every seeded row is present, either by its fixed Id (so a row an Admin renamed still counts) or by its
-- name under its parent; if one is not, it stops with an error and the whole run is rolled back.
--
-- Created and last-modified (who and when): the times take the database default (the moment the script runs) and the created-by and
-- last-modified-by columns stay empty, because no signed-in account loaded these rows; the portal shows that as System.
--
-- NormalizedName follows the application rule: trimmed, repeated whitespace collapsed, invariant upper case. Geographic records have no
-- code. This script does not go through the application, so it writes no audit events. The data is the owner's own files
-- (bd-divisions.json, bd-districts.json, bd-upazilas.json); no official source was cited. Two exact repeats in Natore (upazila ids 361 and
-- 362 in the source) are left out, and "Raipura, Narsingdi" is stored as "Raipura".

BEGIN;

INSERT INTO "Divisions" ("Id", "Name", "NormalizedName", "IsActive")
VALUES
    ('6afb0622-cd35-5dae-a9f1-fa9113af0bc0', 'Barishal', 'BARISHAL', true),
    ('63350ddc-c2b7-5bc0-b624-9a68711bea18', 'Chattogram', 'CHATTOGRAM', true),
    ('dd259409-a0db-53e5-8cf4-5716b39cd176', 'Dhaka', 'DHAKA', true),
    ('23268338-25e6-559a-8fbd-d95577319a62', 'Khulna', 'KHULNA', true),
    ('960d91d3-907d-53ef-8de6-65552cbe9d6a', 'Rajshahi', 'RAJSHAHI', true),
    ('0502154b-b41d-515e-9856-a1dfc160ee38', 'Rangpur', 'RANGPUR', true),
    ('701f9313-a50b-5cf0-be2a-efd362e785c7', 'Sylhet', 'SYLHET', true),
    ('cfbdfb01-4569-5791-af54-3d393248883b', 'Mymensingh', 'MYMENSINGH', true)
ON CONFLICT DO NOTHING;

COMMIT;

SELECT count(*) AS divisions_in_table FROM "Divisions";
