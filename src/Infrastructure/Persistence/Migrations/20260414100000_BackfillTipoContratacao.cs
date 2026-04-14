using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Backfill tipoContratacao for existing ProfessionalService rows that were created
/// via the legacy tierId+contractMode path (tipoContratacao was saved as NULL).
///
/// Mapping:
///   contractMode = 'booking'  → tipoContratacao = 'RESERVA_DIRETA'
///   contractMode = anything else (e.g. 'proposal') → tipoContratacao = 'PROPOSTA'
///
/// Only affects rows where tipoContratacao IS NULL and contractMode IS NOT NULL.
/// IDEMPOTENT: re-running will not change already-populated rows.
/// </summary>
public partial class BackfillTipoContratacao : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            @"UPDATE ""ProfessionalService""
              SET ""tipoContratacao"" = CASE
                WHEN ""contractMode"" = 'booking' THEN 'RESERVA_DIRETA'
                ELSE 'PROPOSTA'
              END
              WHERE ""tipoContratacao"" IS NULL
                AND ""contractMode"" IS NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Backfill is not reversible in a meaningful way — data integrity is preserved
        // by leaving tipoContratacao as-is on rollback.
    }
}
