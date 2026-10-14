-- Seeds the residential halls and hostels of the University of Dhaka into "Halls" (Configuration > Halls).
--
-- Run as the schema owner (the account that applies the EF Core migrations) after the migrations, including AddHalls:
--   psql -d iit_academic_portal -f database/postgresql/seed-halls.sql
--
-- The identifiers are fixed. Each Id is a version 5 UUID of the hall's normalized name under the namespace
-- d44d801a-6ffd-54e8-94b0-c607f02c317c (uuid5 of the URL urn:iit-academic-portal:configuration:halls), written out below, so a new
-- database gets exactly the same Ids as every other one and nothing depends on gen_random_uuid().
--
-- It is safe to run again, here or on any other database: ON CONFLICT DO NOTHING skips a hall that is already there
-- (same Id, or the same live normalized name) and never overwrites a row, so edits an Admin made later are kept. A hall an Admin
-- deleted in the screen is only marked deleted, so its Id is still there and the script does not bring it back. To change a seeded
-- hall after the first run, edit it in the Configuration screen, not here.
--
-- Created and last-modified (who and when): the times take the database default (the moment the script runs) and the created-by and
-- last-modified-by columns stay empty, because no signed-in account loaded these rows; the portal shows that as System.
--
-- NormalizedName follows the application rule: trimmed, repeated whitespace collapsed, invariant upper case. Halls have no
-- code. Every hall gets the same default address. This script does not go through the application, so it writes no audit
-- events. The names are as given by the owner.

BEGIN;

INSERT INTO "Halls" ("Id", "Name", "NormalizedName", "Code", "NormalizedCode", "Address", "IsActive")
VALUES
    ('c701857b-ebcd-5510-b493-b51fa840dc0f', 'Salimullah Muslim Hall', 'SALIMULLAH MUSLIM HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('54f2bdaa-0ff5-5d83-8a0b-8f4e81e0a397', 'Dr. Muhammad Shahidullah Hall', 'DR. MUHAMMAD SHAHIDULLAH HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('3cee167a-a69a-5713-b61b-085781526e53', 'Jagannath Hall', 'JAGANNATH HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('9b00ac6e-eb82-54d0-9dd3-41a67a01dea5', 'Fazlul Huq Muslim Hall', 'FAZLUL HUQ MUSLIM HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('0a68c58f-8121-5d47-bedf-ac10ee0cd70d', 'Shahid Sergeant Zahurul Huq Hall', 'SHAHID SERGEANT ZAHURUL HUQ HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('6342c1b2-9760-5903-ac45-422958bfc5a2', 'Ruqayyah Hall', 'RUQAYYAH HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('3643c691-6b35-544d-8cbd-f58a93869bf3', 'Surja Sen Hall', 'SURJA SEN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('bf57bd5b-0278-51d8-b20c-fe46d0ded1f5', 'Haji Muhammad Mohsin Hall', 'HAJI MUHAMMAD MOHSIN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('7a57e95c-222f-5397-9e73-37ca02d2560c', 'Shamsun Nahar Hall', 'SHAMSUN NAHAR HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('e7e9ef97-5ad6-508c-98d5-68c0f980a5a1', 'Kabi Jasimuddin Hall', 'KABI JASIMUDDIN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('770564a8-093f-53d6-84d6-033a0335f818', 'A.F. Rahman Hall', 'A.F. RAHMAN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('2997bdb8-975b-5de9-8f4d-cedcc9003245', 'Muktijoddha Ziaur Rahman Hall', 'MUKTIJODDHA ZIAUR RAHMAN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('c795a8a1-7bbe-511d-8fae-8faee71537d9', 'Sheikh Mujibur Rahman Hall', 'SHEIKH MUJIBUR RAHMAN HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('38414d43-2312-5fde-bd5a-33d4f75a1eab', 'Bangladesh-Kuwait Maitree Hall', 'BANGLADESH-KUWAIT MAITREE HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('eda18e45-d013-5b0f-ac1f-b794ae587552', 'Sir P.J. Hartog International Hall', 'SIR P.J. HARTOG INTERNATIONAL HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('aa504275-9dd2-5992-8506-efb729f5df60', 'Bangamata Sheikh Fazilatunnesa Mujib Hall', 'BANGAMATA SHEIKH FAZILATUNNESA MUJIB HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('999b0e97-26c2-5e96-a440-14d52459ac3a', 'Amar Ekushey Hall', 'AMAR EKUSHEY HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('7fce62b6-7dcd-5a98-ad9b-68c2c1d0357a', 'Kabi Sufia Kamal Hall', 'KABI SUFIA KAMAL HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('5e5041b7-77a6-50a1-a2d9-85b1ba1de5ae', 'Bijoy Ekattor Hall', 'BIJOY EKATTOR HALL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('8b53e061-166b-5cd3-86f1-ad9b1cff781f', 'Nabab Foyzunnessa Chowdhurani Chhatrinibash', 'NABAB FOYZUNNESSA CHOWDHURANI CHHATRINIBASH', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('f886d9bc-c2e9-5025-ab5d-590a60ac59d8', 'IBA Hostel', 'IBA HOSTEL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('5c0b44e0-4386-5058-8d9f-e40b09420e28', 'Dr. Qudrat-E-Khuda Hostel', 'DR. QUDRAT-E-KHUDA HOSTEL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true),
    ('4e05eb5e-e15b-5c9c-baa2-a272781c4e6b', 'Shahid Athlet Sultana Kamal Hostel', 'SHAHID ATHLET SULTANA KAMAL HOSTEL', NULL, NULL, 'University of Dhaka, Dhaka-1000, Bangladesh', true)
ON CONFLICT DO NOTHING;

COMMIT;

SELECT count(*) AS halls_in_table FROM "Halls";
