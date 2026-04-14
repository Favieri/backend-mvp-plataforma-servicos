-- PRD-10-01: Ensure avatarUrl exists and remove logoUrl if present
-- Run manually on PostgreSQL before or after deploying the application.
-- This script is idempotent and safe to run multiple times.

-- Step 1: Ensure avatarUrl column exists
ALTER TABLE "Professional"
  ADD COLUMN IF NOT EXISTS "avatarUrl" TEXT;

-- Step 2: Migrate data from logoUrl to avatarUrl (if logoUrl column exists),
--         then drop the column.
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'Professional' AND column_name = 'logoUrl'
  ) THEN
    UPDATE "Professional"
      SET "avatarUrl" = "logoUrl"
      WHERE "logoUrl" IS NOT NULL AND "avatarUrl" IS NULL;

    ALTER TABLE "Professional" DROP COLUMN "logoUrl";
  END IF;
END $$;
