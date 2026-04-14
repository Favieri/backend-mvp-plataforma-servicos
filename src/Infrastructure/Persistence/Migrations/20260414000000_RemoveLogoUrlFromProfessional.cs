using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Ensures avatarUrl column exists and removes logoUrl from the Professional table.
///
/// IDEMPOTENT: uses IF NOT EXISTS / conditional DO block so it is safe to re-apply
/// on environments that may or may not have had logoUrl added previously.
/// </summary>
public partial class RemoveLogoUrlFromProfessional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""avatarUrl"" text;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'Professional' AND column_name = 'logoUrl'
  ) THEN
    UPDATE ""Professional""
      SET ""avatarUrl"" = ""logoUrl""
      WHERE ""logoUrl"" IS NOT NULL AND ""avatarUrl"" IS NULL;
    ALTER TABLE ""Professional"" DROP COLUMN ""logoUrl"";
  END IF;
END $$;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""logoUrl"" text;");
    }
}
