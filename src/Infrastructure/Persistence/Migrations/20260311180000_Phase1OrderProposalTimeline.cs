using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 1 Migration: Order + Proposal + Timeline MVP.
/// - Expands "Order" table with transactional model fields (all nullable for retrocompat)
/// - Creates proposal table
/// - Creates order_timeline table
///
/// IDEMPOTENT: uses IF NOT EXISTS guards so it can be safely re-applied when the schema
/// was already created outside EF Core (avoids "column already exists" errors on startup).
/// </summary>
public partial class Phase1OrderProposalTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Expand "Order" table ─────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""professionalId"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""tierId"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""origin"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""proposalId"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""appointmentId"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""conversationId"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""priceTotalCents"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""signalCents"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""balanceCents"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""installments"" integer NOT NULL DEFAULT 1;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""paymentMethod"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""addressId"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""scope"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""scheduledAt"" timestamp without time zone;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""completedAt"" timestamp without time zone;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""cancelledAt"" timestamp without time zone;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""cancelledBy"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""cancellationReason"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""autoConfirmAt"" timestamp without time zone;");

        // Indexes for Order
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Order_professionalId"" ON ""Order""(""professionalId"");");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Order_autoConfirmAt"" ON ""Order""(""autoConfirmAt"") WHERE ""autoConfirmAt"" IS NOT NULL;");

        // ─── Create proposal table ────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS proposal (
    id text NOT NULL,
    order_id text,
    professional_id text NOT NULL,
    client_id text NOT NULL,
    service_id text NOT NULL,
    professional_service_id text,
    conversation_id text,
    scope text NOT NULL,
    includes_description text,
    excludes_description text,
    price_total_cents integer NOT NULL,
    price_by_stage jsonb,
    duration_estimate text,
    suggested_datetime timestamp without time zone,
    visit_fee_cents integer NOT NULL DEFAULT 0,
    valid_until timestamp without time zone NOT NULL,
    status text NOT NULL DEFAULT 'draft',
    rejection_reason text,
    created_at timestamp without time zone NOT NULL,
    updated_at timestamp without time zone NOT NULL,
    CONSTRAINT ""PK_proposal"" PRIMARY KEY (id)
);");

        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_order_id"" ON proposal(order_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_professional_id"" ON proposal(professional_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_client_id"" ON proposal(client_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_conversation_id"" ON proposal(conversation_id) WHERE conversation_id IS NOT NULL;");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_status"" ON proposal(status);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_valid_until"" ON proposal(valid_until);");

        // ─── Create order_timeline table ──────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS order_timeline (
    id text NOT NULL,
    order_id text NOT NULL,
    event_type text NOT NULL,
    actor_id text,
    actor_role text,
    metadata jsonb,
    created_at timestamp without time zone NOT NULL,
    CONSTRAINT ""PK_order_timeline"" PRIMARY KEY (id)
);");

        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_order_timeline_order_id_created_at"" ON order_timeline(order_id, created_at);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_order_timeline_event_type"" ON order_timeline(event_type);");
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
