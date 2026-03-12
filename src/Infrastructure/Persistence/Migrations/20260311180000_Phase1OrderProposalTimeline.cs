using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 1 Migration: Order + Proposal + Timeline MVP.
/// - Expands "Order" table with transactional model fields (all nullable for retrocompat)
/// - Creates proposal table
/// - Creates order_timeline table
/// </summary>
public partial class Phase1OrderProposalTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Expand "Order" table ────────────────────────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "professionalId",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "tierId",
            table: "Order",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "origin",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "proposalId",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "appointmentId",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "conversationId",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "priceTotalCents",
            table: "Order",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "signalCents",
            table: "Order",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "balanceCents",
            table: "Order",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "installments",
            table: "Order",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "paymentMethod",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addressId",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "scope",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "scheduledAt",
            table: "Order",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "completedAt",
            table: "Order",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "cancelledAt",
            table: "Order",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cancelledBy",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cancellationReason",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "autoConfirmAt",
            table: "Order",
            type: "timestamp without time zone",
            nullable: true);

        // New indexes for Order
        migrationBuilder.CreateIndex(
            name: "IX_Order_professionalId",
            table: "Order",
            column: "professionalId");

        migrationBuilder.CreateIndex(
            name: "IX_Order_autoConfirmAt",
            table: "Order",
            column: "autoConfirmAt");

        // ─── Create proposal table ────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "proposal",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: true),
                professional_id = table.Column<string>(type: "text", nullable: false),
                client_id = table.Column<string>(type: "text", nullable: false),
                service_id = table.Column<string>(type: "text", nullable: false),
                professional_service_id = table.Column<string>(type: "text", nullable: true),
                conversation_id = table.Column<string>(type: "text", nullable: true),
                scope = table.Column<string>(type: "text", nullable: false),
                includes_description = table.Column<string>(type: "text", nullable: true),
                excludes_description = table.Column<string>(type: "text", nullable: true),
                price_total_cents = table.Column<int>(type: "integer", nullable: false),
                price_by_stage = table.Column<string>(type: "jsonb", nullable: true),
                duration_estimate = table.Column<string>(type: "text", nullable: true),
                suggested_datetime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                visit_fee_cents = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                valid_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_proposal", x => x.id));

        migrationBuilder.CreateIndex(name: "IX_proposal_order_id", table: "proposal", column: "order_id");
        migrationBuilder.CreateIndex(name: "IX_proposal_professional_id", table: "proposal", column: "professional_id");
        migrationBuilder.CreateIndex(name: "IX_proposal_client_id", table: "proposal", column: "client_id");
        migrationBuilder.CreateIndex(name: "IX_proposal_conversation_id", table: "proposal", column: "conversation_id");
        migrationBuilder.CreateIndex(name: "IX_proposal_status", table: "proposal", column: "status");
        migrationBuilder.CreateIndex(name: "IX_proposal_valid_until", table: "proposal", column: "valid_until");

        // ─── Create order_timeline table ──────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "order_timeline",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                order_id = table.Column<string>(type: "text", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                actor_id = table.Column<string>(type: "text", nullable: true),
                actor_role = table.Column<string>(type: "text", nullable: true),
                metadata = table.Column<string>(type: "jsonb", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_order_timeline", x => x.id));

        migrationBuilder.CreateIndex(
            name: "IX_order_timeline_order_id_created_at",
            table: "order_timeline",
            columns: ["order_id", "created_at"]);

        migrationBuilder.CreateIndex(
            name: "IX_order_timeline_event_type",
            table: "order_timeline",
            column: "event_type");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_timeline");
        migrationBuilder.DropTable(name: "proposal");

        migrationBuilder.DropIndex(name: "IX_Order_professionalId", table: "Order");
        migrationBuilder.DropIndex(name: "IX_Order_autoConfirmAt", table: "Order");

        migrationBuilder.DropColumn(name: "professionalId", table: "Order");
        migrationBuilder.DropColumn(name: "tierId", table: "Order");
        migrationBuilder.DropColumn(name: "origin", table: "Order");
        migrationBuilder.DropColumn(name: "proposalId", table: "Order");
        migrationBuilder.DropColumn(name: "appointmentId", table: "Order");
        migrationBuilder.DropColumn(name: "conversationId", table: "Order");
        migrationBuilder.DropColumn(name: "priceTotalCents", table: "Order");
        migrationBuilder.DropColumn(name: "signalCents", table: "Order");
        migrationBuilder.DropColumn(name: "balanceCents", table: "Order");
        migrationBuilder.DropColumn(name: "installments", table: "Order");
        migrationBuilder.DropColumn(name: "paymentMethod", table: "Order");
        migrationBuilder.DropColumn(name: "addressId", table: "Order");
        migrationBuilder.DropColumn(name: "scope", table: "Order");
        migrationBuilder.DropColumn(name: "scheduledAt", table: "Order");
        migrationBuilder.DropColumn(name: "completedAt", table: "Order");
        migrationBuilder.DropColumn(name: "cancelledAt", table: "Order");
        migrationBuilder.DropColumn(name: "cancelledBy", table: "Order");
        migrationBuilder.DropColumn(name: "cancellationReason", table: "Order");
        migrationBuilder.DropColumn(name: "autoConfirmAt", table: "Order");
    }
}
