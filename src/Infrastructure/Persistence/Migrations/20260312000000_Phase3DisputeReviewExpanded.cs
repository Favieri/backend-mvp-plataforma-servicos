using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 3 Migration: Dispute + Expanded Review (double-blind, categories, photos).
/// - Creates dispute table
/// - Expands "Review" with rating categories, photo_urls, professional review of client, double-blind timestamps, isVerified
/// - Adds index on proposal.valid_until + status for ProposalExpirationJob
///
/// IDEMPOTENT: uses IF NOT EXISTS guards so it can be safely re-applied when the schema
/// was already created outside EF Core.
/// </summary>
public partial class Phase3DisputeReviewExpanded : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Create dispute table ─────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS dispute (
    id text NOT NULL,
    order_id text NOT NULL,
    client_id text NOT NULL,
    professional_id text NOT NULL,
    reason text NOT NULL,
    description text,
    evidence_urls jsonb,
    professional_response text,
    professional_evidence_urls jsonb,
    resolution text,
    resolved_by text,
    refund_amount_cents integer,
    status text NOT NULL DEFAULT 'opened',
    created_at timestamp without time zone NOT NULL,
    resolved_at timestamp without time zone,
    CONSTRAINT ""PK_dispute"" PRIMARY KEY (id)
);");

        // Foreign keys are only added if they don't already exist
        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_dispute_Order_order_id'
    ) THEN
        ALTER TABLE dispute
            ADD CONSTRAINT ""FK_dispute_Order_order_id""
            FOREIGN KEY (order_id) REFERENCES ""Order""(id) ON DELETE RESTRICT;
    END IF;
END $$;");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_dispute_User_client_id'
    ) THEN
        ALTER TABLE dispute
            ADD CONSTRAINT ""FK_dispute_User_client_id""
            FOREIGN KEY (client_id) REFERENCES ""User""(id) ON DELETE RESTRICT;
    END IF;
END $$;");

        migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_dispute_order_id"" ON dispute(order_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_dispute_professional_id"" ON dispute(professional_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_dispute_client_id"" ON dispute(client_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_dispute_status"" ON dispute(status);");

        // ─── Expand "Review" with Phase 3 fields ──────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""punctualityRating"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""qualityRating"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""communicationRating"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""cleanlinessRating"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""photoUrls"" jsonb;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""professionalReviewOfClient"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""professionalRatingOfClient"" integer;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""clientVisibleAt"" timestamp without time zone;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""professionalVisibleAt"" timestamp without time zone;");
        migrationBuilder.Sql(@"ALTER TABLE ""Review"" ADD COLUMN IF NOT EXISTS ""isVerified"" boolean NOT NULL DEFAULT false;");

        // ─── Index for ProposalExpirationJob ──────────────────────────────────
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_proposal_valid_until_status"" ON proposal(valid_until, status) WHERE status = 'sent';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_proposal_valid_until_status", table: "proposal");

        migrationBuilder.DropColumn(name: "punctualityRating",          table: "Review");
        migrationBuilder.DropColumn(name: "qualityRating",              table: "Review");
        migrationBuilder.DropColumn(name: "communicationRating",        table: "Review");
        migrationBuilder.DropColumn(name: "cleanlinessRating",          table: "Review");
        migrationBuilder.DropColumn(name: "photoUrls",                  table: "Review");
        migrationBuilder.DropColumn(name: "professionalReviewOfClient", table: "Review");
        migrationBuilder.DropColumn(name: "professionalRatingOfClient", table: "Review");
        migrationBuilder.DropColumn(name: "clientVisibleAt",            table: "Review");
        migrationBuilder.DropColumn(name: "professionalVisibleAt",      table: "Review");
        migrationBuilder.DropColumn(name: "isVerified",                 table: "Review");

        migrationBuilder.DropTable(name: "dispute");
    }
}
