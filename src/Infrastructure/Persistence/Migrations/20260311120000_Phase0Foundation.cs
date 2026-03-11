using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 0 Foundation Migration.
/// Creates service_tier and service_category tables with seed data.
/// Adds tier/category FKs to Service and ProfessionalService.
/// Adds verification and metrics fields to Professional.
/// All new columns are nullable for retrocompatibility.
/// </summary>
public partial class Phase0Foundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── service_tier ────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "service_tier",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "text", nullable: false),
                code = table.Column<string>(type: "text", nullable: false),
                allow_booking_direct = table.Column<bool>(type: "boolean", nullable: false),
                requires_proposal = table.Column<bool>(type: "boolean", nullable: false),
                requires_chat = table.Column<bool>(type: "boolean", nullable: false),
                allowed_price_formats = table.Column<string[]>(type: "text[]", nullable: false, defaultValue: new string[0]),
                default_signal_percent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                max_installments = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                cancellation_rules = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_service_tier", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_service_tier_code",
            table: "service_tier",
            column: "code",
            unique: true);

        // ─── service_category ────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "service_category",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                icon = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_service_category", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_service_category_name",
            table: "service_category",
            column: "name");

        // ─── Seed service_tier ────────────────────────────────────────────────────
        migrationBuilder.InsertData(
            table: "service_tier",
            columns: ["id", "name", "code", "allow_booking_direct", "requires_proposal", "requires_chat", "allowed_price_formats", "default_signal_percent", "max_installments"],
            values: new object[,]
            {
                { 1, "Serviço Simples", "tier1", true, false, false, new[] { "fixed", "hourly" }, 0, 1 },
                { 2, "Orçamento Rápido", "tier2", false, true, true, new[] { "fixed", "starting_at", "quote" }, 30, 3 },
                { 3, "Projeto Complexo", "tier3", false, true, true, new[] { "fixed", "quote" }, 30, 6 },
                { 4, "Recorrente", "tier4", true, false, false, new[] { "recurring" }, 0, 1 }
            });

        // ─── Seed service_category ────────────────────────────────────────────────
        var now = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);
        migrationBuilder.InsertData(
            table: "service_category",
            columns: ["id", "name", "icon", "created_at"],
            values: new object[,]
            {
                { "cat-plumbing",    "Encanamento",          "🔧", now },
                { "cat-electrical",  "Elétrica",             "⚡", now },
                { "cat-cleaning",    "Limpeza",              "🧹", now },
                { "cat-renovation",  "Reformas",             "🏗️", now },
                { "cat-gardening",   "Jardinagem",           "🌿", now },
                { "cat-painting",    "Pintura",              "🖌️", now },
                { "cat-ac",          "Ar Condicionado",      "❄️", now },
                { "cat-pest",        "Dedetização",          "🦟", now },
                { "cat-moving",      "Mudança",              "📦", now },
                { "cat-it",          "Informática",          "💻", now },
                { "cat-tutoring",    "Aulas Particulares",   "📚", now },
                { "cat-pets",        "Cuidados com Animais", "🐾", now },
                { "cat-beauty",      "Beleza e Estética",    "💇", now },
                { "cat-security",    "Segurança",            "🔒", now },
                { "cat-other",       "Outros",               "🔨", now }
            });

        // ─── Service: add categoryId and tierId ───────────────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "categoryId",
            table: "Service",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "tierId",
            table: "Service",
            type: "integer",
            nullable: true);

        // ─── ProfessionalService: add tier and contract fields ────────────────────
        migrationBuilder.AddColumn<int>(
            name: "tierId",
            table: "ProfessionalService",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "contractMode",
            table: "ProfessionalService",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "durationMinutes",
            table: "ProfessionalService",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "includesDescription",
            table: "ProfessionalService",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "excludesDescription",
            table: "ProfessionalService",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "materialIncluded",
            table: "ProfessionalService",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "visitFeeCents",
            table: "ProfessionalService",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "minLeadTimeMinutes",
            table: "ProfessionalService",
            type: "integer",
            nullable: true);

        // ─── Professional: add verification and metrics fields ────────────────────
        migrationBuilder.AddColumn<string>(
            name: "entityType",
            table: "Professional",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "documentNumber",
            table: "Professional",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "yearsOfExperience",
            table: "Professional",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string[]>(
            name: "specialties",
            table: "Professional",
            type: "text[]",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "responseRate",
            table: "Professional",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "avgResponseTimeMinutes",
            table: "Professional",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "completionRate",
            table: "Professional",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "verificationStatus",
            table: "Professional",
            type: "text",
            nullable: false,
            defaultValue: "pending");

        migrationBuilder.AddColumn<string>(
            name: "badges",
            table: "Professional",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "bufferMinutes",
            table: "Professional",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Remove Professional fields
        migrationBuilder.DropColumn(name: "entityType", table: "Professional");
        migrationBuilder.DropColumn(name: "documentNumber", table: "Professional");
        migrationBuilder.DropColumn(name: "yearsOfExperience", table: "Professional");
        migrationBuilder.DropColumn(name: "specialties", table: "Professional");
        migrationBuilder.DropColumn(name: "responseRate", table: "Professional");
        migrationBuilder.DropColumn(name: "avgResponseTimeMinutes", table: "Professional");
        migrationBuilder.DropColumn(name: "completionRate", table: "Professional");
        migrationBuilder.DropColumn(name: "verificationStatus", table: "Professional");
        migrationBuilder.DropColumn(name: "badges", table: "Professional");
        migrationBuilder.DropColumn(name: "bufferMinutes", table: "Professional");

        // Remove ProfessionalService fields
        migrationBuilder.DropColumn(name: "tierId", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "contractMode", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "durationMinutes", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "includesDescription", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "excludesDescription", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "materialIncluded", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "visitFeeCents", table: "ProfessionalService");
        migrationBuilder.DropColumn(name: "minLeadTimeMinutes", table: "ProfessionalService");

        // Remove Service fields
        migrationBuilder.DropColumn(name: "categoryId", table: "Service");
        migrationBuilder.DropColumn(name: "tierId", table: "Service");

        // Drop category and tier tables
        migrationBuilder.DropTable(name: "service_category");
        migrationBuilder.DropTable(name: "service_tier");
    }
}
