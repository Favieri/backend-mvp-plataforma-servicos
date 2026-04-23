using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-04: Webhook handler database changes.
///
/// 1. Adds mp_payment_id column to payment (stores MP numeric payment ID)
/// 2. Expands payment status CHECK constraint to include 'paid' and 'disputed'
/// 3. Creates ledger_entry table for financial traceability
///    (platform_fee, earning_hold, earning_released entries per payment)
///
/// IDEMPOTENT: uses IF NOT EXISTS / DROP CONSTRAINT IF EXISTS guards.
/// </summary>
public partial class AddWebhookAndLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── 1. Add mp_payment_id to payment ────────────────────────────────
        migrationBuilder.Sql(@"
ALTER TABLE payment ADD COLUMN IF NOT EXISTS mp_payment_id text;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_payment_mp_payment_id
    ON payment (mp_payment_id)
    WHERE mp_payment_id IS NOT NULL;");

        // ─── 2. Expand payment_status_check constraint ───────────────────────
        // Drop and recreate to add 'paid' and 'disputed' statuses
        migrationBuilder.Sql(@"
ALTER TABLE payment DROP CONSTRAINT IF EXISTS payment_status_check;");

        migrationBuilder.Sql(@"
ALTER TABLE payment ADD CONSTRAINT payment_status_check
    CHECK (status IN ('pending','paid','approved','rejected','cancelled','refunded','disputed'));");

        // ─── 3. Create ledger_entry table ────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ledger_entry (
    id              uuid        NOT NULL DEFAULT gen_random_uuid(),
    type            text        NOT NULL,
    order_id        text,
    payment_id      text,
    professional_id text,
    amount_cents    integer     NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ledger_entry_pkey       PRIMARY KEY (id),
    CONSTRAINT ledger_entry_type_check CHECK (type IN ('platform_fee','earning_hold','earning_released'))
);");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_ledger_order_id
    ON ledger_entry (order_id)
    WHERE order_id IS NOT NULL;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_ledger_type_created
    ON ledger_entry (type, created_at);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_ledger_type_created;");
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_ledger_order_id;");
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS ledger_entry;");

        migrationBuilder.Sql(@"ALTER TABLE payment DROP CONSTRAINT IF EXISTS payment_status_check;");
        migrationBuilder.Sql(@"
ALTER TABLE payment ADD CONSTRAINT payment_status_check
    CHECK (status IN ('pending','approved','rejected','cancelled','refunded'));");

        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_payment_mp_payment_id;");
        migrationBuilder.Sql(@"ALTER TABLE payment DROP COLUMN IF EXISTS mp_payment_id;");
    }
}
