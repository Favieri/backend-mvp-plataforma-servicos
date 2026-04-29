using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-06: Enforces data integrity for Professional.userId.
/// Adds NOT NULL constraint and index to prevent authorization failures caused by null UserId.
/// IDEMPOTENT: uses IF NOT EXISTS / IF EXISTS guards.
/// Run ONLY after confirming no null/empty userId rows via:
///   SELECT count(*) FROM "Professional" WHERE "userId" IS NULL OR "userId" = '';
/// </summary>
public partial class AddProfessionalUserIdIntegrityCheck : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Index to accelerate IsOwnerOrAdmin authorization lookups
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_Professional_userId""
    ON ""Professional"" (""userId"");");

        // Enforce NOT NULL — safe only after the userId backfill SQL has been applied
        migrationBuilder.Sql(@"
ALTER TABLE ""Professional""
    ALTER COLUMN ""userId"" SET NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Professional_userId"";");

        migrationBuilder.Sql(@"
ALTER TABLE ""Professional""
    ALTER COLUMN ""userId"" DROP NOT NULL;");
    }
}
