using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD Fluxo de leads: limite do cliente, priorização por reputação e fechamento.
///
/// Extends:
///   - "Order": maxProposals (int, default 5) — quantas propostas o cliente quer receber.
///   - proposal: source_order_id (nullable, FK para "Order") — vincula a proposta ao lead de origem.
///
/// IDEMPOTENTE: todas as DDL usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS.
/// </summary>
public partial class AddLeadFlowLimitsAndReputation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Extend "Order" ───────────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""maxProposals"" integer NOT NULL DEFAULT 5;");
        migrationBuilder.Sql(@"COMMENT ON COLUMN ""Order"".""maxProposals"" IS 'Quantas propostas o cliente quer receber antes do lead fechar por limite (1-20, default 5)';");

        // ─── Extend "proposal" ────────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE proposal ADD COLUMN IF NOT EXISTS source_order_id text;");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_proposal_Order_source_order_id'
    ) THEN
        ALTER TABLE proposal
            ADD CONSTRAINT ""FK_proposal_Order_source_order_id""
            FOREIGN KEY (source_order_id) REFERENCES ""Order""(id) ON DELETE SET NULL;
    END IF;
END $$;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_proposal_source_order_id
    ON proposal (source_order_id);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ─── Revert "proposal" extensions ────────────────────────────────────
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_proposal_source_order_id;");
        migrationBuilder.Sql(@"ALTER TABLE proposal DROP CONSTRAINT IF EXISTS ""FK_proposal_Order_source_order_id"";");
        migrationBuilder.Sql(@"ALTER TABLE proposal DROP COLUMN IF EXISTS source_order_id;");

        // ─── Revert "Order" extensions ────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""maxProposals"";");
    }
}
