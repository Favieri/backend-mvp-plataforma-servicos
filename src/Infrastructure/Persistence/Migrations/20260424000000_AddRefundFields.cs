using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-09: Adds refund tracking columns to the payment table and expands the
/// ledger_entry type CHECK constraint with 'earning_cancelled'.
/// IDEMPOTENT: uses ADD COLUMN IF NOT EXISTS / DROP CONSTRAINT IF EXISTS guards.
/// </summary>
public partial class AddRefundFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Refund tracking columns on payment
        migrationBuilder.Sql(@"
ALTER TABLE payment
    ADD COLUMN IF NOT EXISTS refund_status  text DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS refund_id      text,
    ADD COLUMN IF NOT EXISTS refunded_at    timestamptz,
    ADD COLUMN IF NOT EXISTS refund_reason  text;");

        // Partial index for the pending-refund retry queue
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_payment_refund_pending
    ON payment (refund_status)
    WHERE refund_status = 'pending';");

        // Expand ledger_entry type CHECK to include earning_cancelled (refund reversal)
        migrationBuilder.Sql(@"
ALTER TABLE ledger_entry DROP CONSTRAINT IF EXISTS ledger_entry_type_check;
ALTER TABLE ledger_entry ADD CONSTRAINT ledger_entry_type_check
    CHECK (type IN ('platform_fee','earning_hold','earning_released',
                    'earning_dispute_hold','earning_dispute_released',
                    'earning_dispute_refunded','refund','earning_cancelled'));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_payment_refund_pending;");

        migrationBuilder.Sql(@"
ALTER TABLE payment
    DROP COLUMN IF EXISTS refund_status,
    DROP COLUMN IF EXISTS refund_id,
    DROP COLUMN IF EXISTS refunded_at,
    DROP COLUMN IF EXISTS refund_reason;");

        migrationBuilder.Sql(@"
ALTER TABLE ledger_entry DROP CONSTRAINT IF EXISTS ledger_entry_type_check;
ALTER TABLE ledger_entry ADD CONSTRAINT ledger_entry_type_check
    CHECK (type IN ('platform_fee','earning_hold','earning_released',
                    'earning_dispute_hold','earning_dispute_released',
                    'earning_dispute_refunded','refund'));");
    }
}
