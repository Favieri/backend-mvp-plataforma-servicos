using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Perf PRD Item 4: adiciona índice em Professional.verificationStatus (filtro usado por
/// GET /professionals?verificationStatus=) e torna o índice composto (active, rating)
/// explicitamente descendente em rating, alinhado à ordenação usada na paginação.
/// IDEMPOTENTE: CreateIndex/DropIndex do EF Core já são seguros para reaplicação
/// (o Program.cs trata MigrateAsync como best-effort e loga se a migration já foi aplicada).
/// </summary>
public partial class AddProfessionalIndexPerf : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Professional_active_rating",
            table: "Professional");

        migrationBuilder.CreateIndex(
            name: "IX_Professional_active_rating",
            table: "Professional",
            columns: new[] { "active", "rating" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "IX_Professional_verificationStatus",
            table: "Professional",
            column: "verificationStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Professional_verificationStatus",
            table: "Professional");

        migrationBuilder.DropIndex(
            name: "IX_Professional_active_rating",
            table: "Professional");

        migrationBuilder.CreateIndex(
            name: "IX_Professional_active_rating",
            table: "Professional",
            columns: new[] { "active", "rating" });
    }
}
