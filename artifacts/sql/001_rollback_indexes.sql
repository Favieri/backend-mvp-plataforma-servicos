-- ============================================================
-- Jobeasy — EF Core Baseline Migration Rollback
-- Drops indexes added by 20260306000000_InitialEfBaseline
-- ============================================================

BEGIN;

DROP INDEX IF EXISTS "IX_User_zoneId";
DROP INDEX IF EXISTS "IX_Professional_userId";
DROP INDEX IF EXISTS "IX_Professional_active_rating";
DROP INDEX IF EXISTS "IX_Order_clientId";
DROP INDEX IF EXISTS "IX_Order_serviceId";
DROP INDEX IF EXISTS "IX_Order_clientId_createdAt";
DROP INDEX IF EXISTS "IX_Order_status";
DROP INDEX IF EXISTS "IX_Appointment_professionalId";
DROP INDEX IF EXISTS "IX_Appointment_clientId";
DROP INDEX IF EXISTS "IX_Appointment_professionalId_startsAt_status";
DROP INDEX IF EXISTS "IX_Conversation_clientId";
DROP INDEX IF EXISTS "IX_Conversation_professionalId";
DROP INDEX IF EXISTS "IX_Conversation_clientId_professionalId";
DROP INDEX IF EXISTS "IX_Message_conversationId";
DROP INDEX IF EXISTS "IX_Message_conversationId_sentAt";
DROP INDEX IF EXISTS "IX_Review_professionalId";
DROP INDEX IF EXISTS "IX_Review_clientId_createdAt";
DROP INDEX IF EXISTS "IX_ProfessionalService_professionalId";
DROP INDEX IF EXISTS "IX_ProfessionalService_serviceId";
DROP INDEX IF EXISTS "IX_ProfessionalZone_zoneId";
DROP INDEX IF EXISTS "IX_ProfessionalAvailability_professionalId_weekday";
DROP INDEX IF EXISTS "IX_ProfessionalBlock_professionalId_startsAt_endsAt";
DROP INDEX IF EXISTS "IX_ProfessionalPortfolio_professionalId";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260306000000_InitialEfBaseline';

COMMIT;
