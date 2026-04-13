using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds logoUrl column to Professional table for storing the professional's brand logo reference.
///
/// IDEMPOTENT: uses IF NOT EXISTS guard so it can be safely re-applied when the schema
/// was already created outside EF Core.
/// </summary>
public partial class AddLogoUrlToProfessional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""logoUrl"" text;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "logoUrl",
            table: "Professional");
    }
}
