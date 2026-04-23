using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-05: Expands ledger_entry type CHECK constraint to include dispute and refund types.
/// Adds a composite index on (professional_id, created_at DESC) for wallet queries.
/// IDEMPOTENT: uses DROP CONSTRAINT IF EXISTS / CREATE INDEX IF NOT EXISTS guards.
/// </summary>
public partial class ExpandLedgerTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Expand type CHECK constraint to include dispute and refund variants
        migrationBuilder.Sql(@"
ALTER TABLE ledger_entry DROP CONSTRAINT IF EXISTS ledger_entry_type_check;
ALTER TABLE ledger_entry ADD CONSTRAINT ledger_entry_type_check
    CHECK (type IN ('platform_fee','earning_hold','earning_released',
                    'earning_dispute_hold','earning_dispute_released',
                    'earning_dispute_refunded','refund'));");

        // Composite index for wallet balance and ledger queries filtered by professional
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_ledger_professional_created
    ON ledger_entry (professional_id, created_at DESC)
    WHERE professional_id IS NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_ledger_professional_created;");

        migrationBuilder.Sql(@"
ALTER TABLE ledger_entry DROP CONSTRAINT IF EXISTS ledger_entry_type_check;
ALTER TABLE ledger_entry ADD CONSTRAINT ledger_entry_type_check
    CHECK (type IN ('platform_fee','earning_hold','earning_released'));");
    }
}
