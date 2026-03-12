using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 3 Migration: Dispute + Expanded Review (double-blind, categories, photos).
/// - Creates dispute table
/// - Expands "Review" with rating categories, photo_urls, professional review of client, double-blind timestamps, isVerified
/// - Adds index on proposal.valid_until + status for ProposalExpirationJob
/// </summary>
public partial class Phase3DisputeReviewExpanded : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Create dispute table ────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "dispute",
            columns: table => new
            {
                id                         = table.Column<string>(type: "text", nullable: false),
                order_id                   = table.Column<string>(type: "text", nullable: false),
                client_id                  = table.Column<string>(type: "text", nullable: false),
                professional_id            = table.Column<string>(type: "text", nullable: false),
                reason                     = table.Column<string>(type: "text", nullable: false),
                description                = table.Column<string>(type: "text", nullable: true),
                evidence_urls              = table.Column<string>(type: "jsonb", nullable: true),
                professional_response      = table.Column<string>(type: "text", nullable: true),
                professional_evidence_urls = table.Column<string>(type: "jsonb", nullable: true),
                resolution                 = table.Column<string>(type: "text", nullable: true),
                resolved_by                = table.Column<string>(type: "text", nullable: true),
                refund_amount_cents        = table.Column<int>(type: "integer", nullable: true),
                status                     = table.Column<string>(type: "text", nullable: false, defaultValue: "opened"),
                created_at                 = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                resolved_at                = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dispute", x => x.id);
                table.ForeignKey(
                    name: "FK_dispute_Order_order_id",
                    column: x => x.order_id,
                    principalTable: "Order",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_dispute_User_client_id",
                    column: x => x.client_id,
                    principalTable: "User",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dispute_order_id",
            table: "dispute",
            column: "order_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_dispute_professional_id",
            table: "dispute",
            column: "professional_id");

        migrationBuilder.CreateIndex(
            name: "IX_dispute_client_id",
            table: "dispute",
            column: "client_id");

        migrationBuilder.CreateIndex(
            name: "IX_dispute_status",
            table: "dispute",
            column: "status");

        // ─── Expand "Review" with Phase 3 fields ────────────────────────────

        // Category ratings (1-5, nullable)
        migrationBuilder.AddColumn<int>(
            name: "punctualityRating",
            table: "Review",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "qualityRating",
            table: "Review",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "communicationRating",
            table: "Review",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "cleanlinessRating",
            table: "Review",
            type: "integer",
            nullable: true);

        // Photo URLs (JSONB)
        migrationBuilder.AddColumn<string>(
            name: "photoUrls",
            table: "Review",
            type: "jsonb",
            nullable: true);

        // Professional reviews client
        migrationBuilder.AddColumn<string>(
            name: "professionalReviewOfClient",
            table: "Review",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "professionalRatingOfClient",
            table: "Review",
            type: "integer",
            nullable: true);

        // Double-blind visibility timestamps
        migrationBuilder.AddColumn<DateTime>(
            name: "clientVisibleAt",
            table: "Review",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "professionalVisibleAt",
            table: "Review",
            type: "timestamp without time zone",
            nullable: true);

        // Verified flag
        migrationBuilder.AddColumn<bool>(
            name: "isVerified",
            table: "Review",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // ─── Index for ProposalExpirationJob ─────────────────────────────────
        migrationBuilder.CreateIndex(
            name: "IX_proposal_valid_until_status",
            table: "proposal",
            columns: new[] { "valid_until", "status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_proposal_valid_until_status", table: "proposal");

        migrationBuilder.DropColumn(name: "punctualityRating",         table: "Review");
        migrationBuilder.DropColumn(name: "qualityRating",             table: "Review");
        migrationBuilder.DropColumn(name: "communicationRating",       table: "Review");
        migrationBuilder.DropColumn(name: "cleanlinessRating",         table: "Review");
        migrationBuilder.DropColumn(name: "photoUrls",                 table: "Review");
        migrationBuilder.DropColumn(name: "professionalReviewOfClient",table: "Review");
        migrationBuilder.DropColumn(name: "professionalRatingOfClient",table: "Review");
        migrationBuilder.DropColumn(name: "clientVisibleAt",           table: "Review");
        migrationBuilder.DropColumn(name: "professionalVisibleAt",     table: "Review");
        migrationBuilder.DropColumn(name: "isVerified",                table: "Review");

        migrationBuilder.DropTable(name: "dispute");
    }
}
