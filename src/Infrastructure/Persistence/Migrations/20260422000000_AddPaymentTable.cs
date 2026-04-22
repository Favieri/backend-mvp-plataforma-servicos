using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-03: Creates the payment table for Mercado Pago Checkout Pro.
///
/// Stores one payment record per preference creation. Status starts as 'pending'
/// and is updated by the webhook handler (PRD-MP-04).
/// Pix fields (pix_code, pix_qr_code_base64, pix_expires_at) are reserved
/// for PRD-MP-04 webhook processing.
///
/// IDEMPOTENT: uses IF NOT EXISTS / DO $$ guards.
/// </summary>
public partial class AddPaymentTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS payment (
    id                  text        NOT NULL,
    order_id            text        NOT NULL,
    gateway             text        NOT NULL,
    gateway_ref         text,
    method              text        NOT NULL DEFAULT 'unknown',
    amount_cents        integer     NOT NULL,
    platform_fee_cents  integer     NOT NULL DEFAULT 0,
    gateway_fee_cents   integer     NOT NULL DEFAULT 0,
    status              text        NOT NULL DEFAULT 'pending',
    created_at          timestamptz NOT NULL DEFAULT now(),
    paid_at             timestamptz,
    pix_code            text,
    pix_qr_code_base64  text,
    pix_expires_at      timestamptz,
    CONSTRAINT payment_pkey         PRIMARY KEY (id),
    CONSTRAINT payment_status_check CHECK (status IN ('pending','approved','rejected','cancelled','refunded'))
);");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_payment_Order'
    ) THEN
        ALTER TABLE payment
            ADD CONSTRAINT ""FK_payment_Order""
            FOREIGN KEY (order_id) REFERENCES ""Order""(id) ON DELETE CASCADE;
    END IF;
END $$;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_payment_order_id
    ON payment (order_id);");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_payment_order_status
    ON payment (order_id, status)
    WHERE status = 'pending';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_payment_order_status;");
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_payment_order_id;");
        migrationBuilder.DropTable(name: "payment");
    }
}
