-- ============================================================
-- Jobeasy — EF Core Baseline Migration: 20260306000000_InitialEfBaseline
-- Idempotent SQL script for applying this migration to an existing database.
--
-- This script:
--   1. Creates __EFMigrationsHistory table (if not exists)
--   2. Adds new performance indexes (if not exists)
--   3. Marks the migration as applied
--
-- APPLY: psql -U <user> -d <database> -f 001_initial_idempotent.sql
-- ROLLBACK: Run 001_rollback_indexes.sql
-- ============================================================

BEGIN;

-- EF Core migrations history table
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- Only apply if this migration has not been applied yet
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260306000000_InitialEfBaseline'
    ) THEN

        -- New indexes on "User"
        CREATE INDEX IF NOT EXISTS "IX_User_zoneId"
            ON public."User" ("zoneId");

        -- New indexes on "Professional"
        CREATE INDEX IF NOT EXISTS "IX_Professional_userId"
            ON public."Professional" ("userId");

        CREATE INDEX IF NOT EXISTS "IX_Professional_active_rating"
            ON public."Professional" ("active", "rating");

        -- New indexes on "Order"
        CREATE INDEX IF NOT EXISTS "IX_Order_clientId"
            ON public."Order" ("clientId");

        CREATE INDEX IF NOT EXISTS "IX_Order_serviceId"
            ON public."Order" ("serviceId");

        CREATE INDEX IF NOT EXISTS "IX_Order_clientId_createdAt"
            ON public."Order" ("clientId", "createdAt");

        CREATE INDEX IF NOT EXISTS "IX_Order_status"
            ON public."Order" ("status");

        -- New indexes on "Appointment"
        CREATE INDEX IF NOT EXISTS "IX_Appointment_professionalId"
            ON public."Appointment" ("professionalId");

        CREATE INDEX IF NOT EXISTS "IX_Appointment_clientId"
            ON public."Appointment" ("clientId");

        CREATE INDEX IF NOT EXISTS "IX_Appointment_professionalId_startsAt_status"
            ON public."Appointment" ("professionalId", "startsAt", "status");

        -- New indexes on "Conversation"
        CREATE INDEX IF NOT EXISTS "IX_Conversation_clientId"
            ON public."Conversation" ("clientId");

        CREATE INDEX IF NOT EXISTS "IX_Conversation_professionalId"
            ON public."Conversation" ("professionalId");

        CREATE INDEX IF NOT EXISTS "IX_Conversation_clientId_professionalId"
            ON public."Conversation" ("clientId", "professionalId");

        -- New indexes on "Message"
        CREATE INDEX IF NOT EXISTS "IX_Message_conversationId"
            ON public."Message" ("conversationId");

        CREATE INDEX IF NOT EXISTS "IX_Message_conversationId_sentAt"
            ON public."Message" ("conversationId", "sentAt");

        -- New indexes on "Review"
        CREATE INDEX IF NOT EXISTS "IX_Review_professionalId"
            ON public."Review" ("professionalId");

        CREATE INDEX IF NOT EXISTS "IX_Review_clientId_createdAt"
            ON public."Review" ("clientId", "createdAt");

        -- New indexes on "ProfessionalService"
        CREATE INDEX IF NOT EXISTS "IX_ProfessionalService_professionalId"
            ON public."ProfessionalService" ("professionalId");

        CREATE INDEX IF NOT EXISTS "IX_ProfessionalService_serviceId"
            ON public."ProfessionalService" ("serviceId");

        -- New indexes on "ProfessionalZone"
        CREATE INDEX IF NOT EXISTS "IX_ProfessionalZone_zoneId"
            ON public."ProfessionalZone" ("zoneId");

        -- New indexes on "ProfessionalAvailability"
        CREATE INDEX IF NOT EXISTS "IX_ProfessionalAvailability_professionalId_weekday"
            ON public."ProfessionalAvailability" ("professionalId", "weekday");

        -- New indexes on "ProfessionalBlock"
        CREATE INDEX IF NOT EXISTS "IX_ProfessionalBlock_professionalId_startsAt_endsAt"
            ON public."ProfessionalBlock" ("professionalId", "startsAt", "endsAt");

        -- New indexes on "ProfessionalPortfolio"
        CREATE INDEX IF NOT EXISTS "IX_ProfessionalPortfolio_professionalId"
            ON public."ProfessionalPortfolio" ("professionalId");

        -- Mark migration as applied
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260306000000_InitialEfBaseline', '8.0.10');

        RAISE NOTICE 'Migration 20260306000000_InitialEfBaseline applied successfully.';
    ELSE
        RAISE NOTICE 'Migration 20260306000000_InitialEfBaseline already applied — skipping.';
    END IF;
END $$;

COMMIT;
