-- ============================================================
-- Script: apply-address-migration.sql
-- Migration: 20260320000000_AddAddressFields
--
-- Aplica as colunas de endereço às tabelas Order, User e
-- recurring_plan caso ainda não existam.
--
-- Como executar:
--   psql $DB_CONNECTION -f scripts/apply-address-migration.sql
--
-- Ou via dotnet ef (se disponível):
--   dotnet ef database update --project src/Infrastructure --startup-project src/Api
-- ============================================================

-- ─── Order: service address snapshot ─────────────────────────────────────────
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrZipCode"      text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrStreet"       text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrNumber"       text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrNeighborhood" text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrCity"         text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrState"        text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrComplement"   text;
ALTER TABLE "Order" ADD COLUMN IF NOT EXISTS "svcAddrReference"    text;

-- ─── User: default address ────────────────────────────────────────────────────
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_zip_code      text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_street        text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_number        text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_neighborhood  text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_city          text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_state         text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_complement    text;
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS addr_reference     text;

-- ─── recurring_plan: service address snapshot ─────────────────────────────────
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrZipCode"      text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrStreet"       text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrNumber"       text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrNeighborhood" text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrCity"         text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrState"        text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrComplement"   text;
ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS "svcAddrReference"    text;

-- ─── Registrar migration no histórico EF ─────────────────────────────────────
-- Garante que 'dotnet ef database update' não tente reaplicar
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260320000000_AddAddressFields', '8.0.10')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ─── Verificação ──────────────────────────────────────────────────────────────
SELECT column_name
FROM information_schema.columns
WHERE table_name = 'Order'
  AND column_name LIKE 'svcAddr%'
ORDER BY column_name;
