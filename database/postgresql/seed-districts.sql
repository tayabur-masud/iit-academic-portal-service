-- Seeds the 64 districts of Bangladesh into "Districts" (Configuration > Geography).
--
-- Run as the schema owner (the account that applies the EF Core migrations) after the migrations, in this order:
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-divisions.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-districts.sql
--   psql -v ON_ERROR_STOP=1 -d iit_academic_portal -f database/postgresql/seed-upazilas.sql
--
-- The identifiers are fixed. Each Id is a version 5 UUID under the namespace
-- 028436bf-43b6-5975-9aac-7820e49ed20d (uuid5 of the URL urn:iit-academic-portal:configuration:geography)
-- of "district|DIVISION|NAME", using normalized (upper case) names, and is written out below, so
-- every new database gets exactly the same Ids and nothing depends on gen_random_uuid().
--
-- It is safe to run again, here or on any other database: ON CONFLICT DO NOTHING skips a row that is already there (same Id, or the
-- same live normalized name within its parent) and never overwrites one, so edits made later in the Configuration screen are kept.
-- A row an Admin deleted in the screen is only marked deleted, so its Id is still there and the script does not bring it back.
--
-- Each district's division is found by its normalized name among divisions that are not deleted, so this works whether the division was
-- added by seed-divisions.sql or already existed with another Id. Run seed-divisions.sql first: a district whose division is missing, or was
-- deleted, stops the script with an error and nothing is added.
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

CREATE TEMP TABLE seed_districts ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('3ca17ade-a79e-5b09-9b25-c250ff38cf46', 'DHAKA', 'Dhaka', 'DHAKA'),
    ('32f9f913-d49c-5841-a963-96ccb2c324c8', 'DHAKA', 'Faridpur', 'FARIDPUR'),
    ('60233582-651c-55d7-a9cd-0df2a60de33e', 'DHAKA', 'Gazipur', 'GAZIPUR'),
    ('dfb7a107-4662-562f-85eb-e062f8e5337e', 'DHAKA', 'Gopalganj', 'GOPALGANJ'),
    ('5ef223bd-3407-50da-aa56-bcbdd433d9d0', 'MYMENSINGH', 'Jamalpur', 'JAMALPUR'),
    ('99f97201-5001-5719-8efc-651c7258a663', 'DHAKA', 'Kishoreganj', 'KISHOREGANJ'),
    ('1398a42f-5114-5945-86b5-eeae94ad6038', 'DHAKA', 'Madaripur', 'MADARIPUR'),
    ('5866607f-534f-5577-8e5b-260bf1627721', 'DHAKA', 'Manikganj', 'MANIKGANJ'),
    ('3fae3476-bf60-53dc-bff6-84a6c6397b80', 'DHAKA', 'Munshiganj', 'MUNSHIGANJ'),
    ('0910dae7-1adf-5f26-97cb-8b90d1c73c73', 'MYMENSINGH', 'Mymensingh', 'MYMENSINGH'),
    ('595bf3b6-75de-5b5e-850c-899db5578f1a', 'DHAKA', 'Narayanganj', 'NARAYANGANJ'),
    ('b8a07d9b-d6f4-59de-9b4a-1dbb2a9dffdb', 'DHAKA', 'Narsingdi', 'NARSINGDI'),
    ('4949bbb7-9dc9-5ccf-9368-ffa74b25b491', 'MYMENSINGH', 'Netrokona', 'NETROKONA'),
    ('ad529323-fbdd-5d41-8215-50431256d007', 'DHAKA', 'Rajbari', 'RAJBARI'),
    ('5b61429a-85e5-5346-a092-283674693c10', 'DHAKA', 'Shariatpur', 'SHARIATPUR'),
    ('166abdde-2c16-5d32-8010-49f4ab3a03a7', 'MYMENSINGH', 'Sherpur', 'SHERPUR'),
    ('fd9043c3-d074-58ef-aa5f-2cbcd03edbb0', 'DHAKA', 'Tangail', 'TANGAIL'),
    ('c4e8345c-ecb7-5e2a-b477-a318373a3492', 'RAJSHAHI', 'Bogura', 'BOGURA'),
    ('617277f4-578d-524c-995d-5f8b0c3eccd6', 'RAJSHAHI', 'Joypurhat', 'JOYPURHAT'),
    ('755c324e-a2b0-594b-a1f0-4f2d15a954c9', 'RAJSHAHI', 'Naogaon', 'NAOGAON'),
    ('c072be36-2467-5eb5-a0b2-c94752f36d09', 'RAJSHAHI', 'Natore', 'NATORE'),
    ('3f69f6e5-8532-5dbb-9096-bff89fb7cf59', 'RAJSHAHI', 'Nawabganj', 'NAWABGANJ'),
    ('09adccc2-e2ef-52db-b42b-7497fcf5d2e2', 'RAJSHAHI', 'Pabna', 'PABNA'),
    ('2c1b2751-e14f-511c-a33e-35b2ee2bdea7', 'RAJSHAHI', 'Rajshahi', 'RAJSHAHI'),
    ('3d825c5a-9153-53b2-a9da-c5ddada3c387', 'RAJSHAHI', 'Sirajgonj', 'SIRAJGONJ'),
    ('75835996-3a25-5729-a085-e0d1f4db07de', 'RANGPUR', 'Dinajpur', 'DINAJPUR'),
    ('c42130bb-6223-51d1-a0b3-855ddaaca3de', 'RANGPUR', 'Gaibandha', 'GAIBANDHA'),
    ('7c46599e-ff5f-5beb-9bf0-3885b96b46ff', 'RANGPUR', 'Kurigram', 'KURIGRAM'),
    ('5b97db13-3521-5116-a284-0188da47cf4b', 'RANGPUR', 'Lalmonirhat', 'LALMONIRHAT'),
    ('1b52e007-8dca-568f-9912-3a5b2b268333', 'RANGPUR', 'Nilphamari', 'NILPHAMARI'),
    ('9e4297cb-e6a7-51bd-9d10-45cca18d39bd', 'RANGPUR', 'Panchagarh', 'PANCHAGARH'),
    ('96722155-9228-54dc-9a11-7b745e59b209', 'RANGPUR', 'Rangpur', 'RANGPUR'),
    ('8407af79-69dd-5a83-9952-5f45c359403a', 'RANGPUR', 'Thakurgaon', 'THAKURGAON'),
    ('e04b172c-1690-5a34-a6b2-3c48f9df284e', 'BARISHAL', 'Barguna', 'BARGUNA'),
    ('ad0a14c0-9002-5f1c-9e54-c1b1ff35e71e', 'BARISHAL', 'Barishal', 'BARISHAL'),
    ('754cea1e-d08f-5ed6-a78f-6037a7783307', 'BARISHAL', 'Bhola', 'BHOLA'),
    ('36b90dbd-959f-5888-be78-0280d9ee0246', 'BARISHAL', 'Jhalokati', 'JHALOKATI'),
    ('49c17097-7b6a-5435-b364-24d22b709fe7', 'BARISHAL', 'Patuakhali', 'PATUAKHALI'),
    ('dbc9477f-891b-52f0-8fb6-612319b09dad', 'BARISHAL', 'Pirojpur', 'PIROJPUR'),
    ('3e391619-9a6b-5386-9fb8-fc93e909a32a', 'CHATTOGRAM', 'Bandarban', 'BANDARBAN'),
    ('6347f1a5-85ae-55bb-af72-90340a98c464', 'CHATTOGRAM', 'Brahmanbaria', 'BRAHMANBARIA'),
    ('84af941e-e0f3-5f00-8d47-6aea9d2f0c0f', 'CHATTOGRAM', 'Chandpur', 'CHANDPUR'),
    ('6bdd12d2-eb87-5692-85d5-12ca7b3d13ca', 'CHATTOGRAM', 'Chattogram', 'CHATTOGRAM'),
    ('77503c97-69ba-51ba-8983-a73545a11e7f', 'CHATTOGRAM', 'Cumilla', 'CUMILLA'),
    ('f681f441-e3af-5ee6-855b-e67dc65eca6b', 'CHATTOGRAM', 'Cox''s Bazar', 'COX''S BAZAR'),
    ('2818b53f-8e2d-5676-9ec6-c13eeaad1e23', 'CHATTOGRAM', 'Feni', 'FENI'),
    ('fd3682d3-86c5-54f2-9c63-ea5e1faf54e6', 'CHATTOGRAM', 'Khagrachari', 'KHAGRACHARI'),
    ('6e070339-9ae9-58fd-92bb-dff4367db618', 'CHATTOGRAM', 'Lakshmipur', 'LAKSHMIPUR'),
    ('d7d4b785-5538-59b5-b766-9a5397eaf029', 'CHATTOGRAM', 'Noakhali', 'NOAKHALI'),
    ('87600f58-1869-5cb8-a22e-0e425ff72af0', 'CHATTOGRAM', 'Rangamati', 'RANGAMATI'),
    ('87f3bae8-6b68-5c3e-a0e5-57a0d3b0824c', 'SYLHET', 'Habiganj', 'HABIGANJ'),
    ('1005b217-bf40-5972-994f-d17e5ff0bccf', 'SYLHET', 'Maulvibazar', 'MAULVIBAZAR'),
    ('860a1f06-976d-50fc-aca7-082be3507ebd', 'SYLHET', 'Sunamganj', 'SUNAMGANJ'),
    ('c8eefee2-9678-5ad6-9541-5720484387f3', 'SYLHET', 'Sylhet', 'SYLHET'),
    ('e3f98226-f116-5e9c-9bf5-799de1fa6801', 'KHULNA', 'Bagerhat', 'BAGERHAT'),
    ('bb16fe3f-aa62-5f05-8b60-3ea87fe7f089', 'KHULNA', 'Chuadanga', 'CHUADANGA'),
    ('a719564e-86b1-58a4-8fec-e4e13280323c', 'KHULNA', 'Jashore', 'JASHORE'),
    ('24dfb57f-70d0-5ef6-b79f-9aedf0c87c2a', 'KHULNA', 'Jhenaidah', 'JHENAIDAH'),
    ('2071bbdb-28a1-53c2-a896-86ad7608f0c2', 'KHULNA', 'Khulna', 'KHULNA'),
    ('59281fa7-2ec0-5746-b8a1-b5946ff304a8', 'KHULNA', 'Kushtia', 'KUSHTIA'),
    ('79c03d4d-7ecb-5128-bfea-e60882bf1dc4', 'KHULNA', 'Magura', 'MAGURA'),
    ('be7f323d-ff9b-5878-98b6-6bdbdbd2c4f0', 'KHULNA', 'Meherpur', 'MEHERPUR'),
    ('a36769f5-f2d9-5212-b14d-f01c83f63dc3', 'KHULNA', 'Narail', 'NARAIL'),
    ('87906453-d50e-5328-b53d-b8c5908cee3f', 'KHULNA', 'Satkhira', 'SATKHIRA')
) AS v (id, division, name, normalized);

INSERT INTO "Districts" ("Id", "DivisionId", "Name", "NormalizedName", "IsActive")
SELECT s.id::uuid, d."Id", s.name, s.normalized, true
FROM seed_districts s
JOIN "Divisions" d ON d."NormalizedName" = s.division AND NOT d."IsDeleted"
ON CONFLICT DO NOTHING;

DO $$
DECLARE missing integer;
BEGIN
    SELECT count(*) INTO missing
    FROM seed_districts s
    WHERE NOT EXISTS (SELECT 1 FROM "Districts" x WHERE x."Id" = s.id::uuid)
      AND NOT EXISTS (
        SELECT 1 FROM "Districts" x JOIN "Divisions" d ON d."Id" = x."DivisionId"
        WHERE d."NormalizedName" = s.division AND x."NormalizedName" = s.normalized AND NOT x."IsDeleted" AND NOT d."IsDeleted");
    IF missing > 0 THEN
        RAISE EXCEPTION '% district(s) could not be added because their division is missing or was deleted; run seed-divisions.sql first (a deleted division stays deleted).', missing;
    END IF;
END $$;

COMMIT;

SELECT count(*) AS districts_in_table FROM "Districts";
