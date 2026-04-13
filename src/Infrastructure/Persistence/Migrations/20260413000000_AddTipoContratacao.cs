using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds tipoContratacao column to ProfessionalService table and makes preco nullable.
///
/// tipoContratacao replaces the tierId+contractMode classification with two values:
///   RESERVA_DIRETA (direct booking, precoBase and durationMinutes required)
///   PROPOSTA       (proposal-based, precoBase treated as SOB_CONSULTA, nullable)
///
/// IDEMPOTENT: uses IF NOT EXISTS / IF EXISTS guards so it can be safely re-applied.
/// </summary>
public partial class AddTipoContratacao : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add tipoContratacao column
        migrationBuilder.Sql(
            @"ALTER TABLE ""ProfessionalService"" ADD COLUMN IF NOT EXISTS ""tipoContratacao"" text;");

        // Make preco nullable (was double precision NOT NULL)
        migrationBuilder.Sql(
            @"ALTER TABLE ""ProfessionalService"" ALTER COLUMN ""preco"" DROP NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore NOT NULL — set any nulls to 0 to avoid constraint violation
        migrationBuilder.Sql(
            @"UPDATE ""ProfessionalService"" SET ""preco"" = 0 WHERE ""preco"" IS NULL;");
        migrationBuilder.Sql(
            @"ALTER TABLE ""ProfessionalService"" ALTER COLUMN ""preco"" SET NOT NULL;");

        migrationBuilder.Sql(
            @"ALTER TABLE ""ProfessionalService"" DROP COLUMN IF EXISTS ""tipoContratacao"";");
    }
}
