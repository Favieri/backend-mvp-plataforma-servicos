using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds provider and provider_user_id columns to User table for social login (Google/Facebook).
///
/// IDEMPOTENT: uses IF NOT EXISTS guards so it can be safely re-applied when the schema
/// was already created outside EF Core.
/// </summary>
public partial class AddSocialLoginFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""provider"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""provider_user_id"" text;");

        // Unique index on (provider, provider_user_id) where both are not null
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_User_provider_providerUserId"
            ON "User" (provider, provider_user_id)
            WHERE provider IS NOT NULL AND provider_user_id IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_User_provider_providerUserId",
            table: "User");

        migrationBuilder.DropColumn(
            name: "provider_user_id",
            table: "User");

        migrationBuilder.DropColumn(
            name: "provider",
            table: "User");
    }
}
