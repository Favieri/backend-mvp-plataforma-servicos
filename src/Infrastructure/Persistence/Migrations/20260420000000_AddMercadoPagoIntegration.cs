using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-01: Mercado Pago integration database foundation.
///
/// Creates:
///   - professional_mp_account: stores OAuth tokens per professional (one per professional)
///   - webhook_events: idempotent webhook log (PK = provider + event_id)
///
/// Extends:
///   - "Professional": mpConnected (bool snapshot), mpConnectedAt
///   - "Order": platformFeePercent, platformFeeCents, gatewayFeeCents, paymentStatus, mpPreferenceId
///
/// IDEMPOTENT: all DDL uses IF NOT EXISTS / ADD COLUMN IF NOT EXISTS.
/// </summary>
public partial class AddMercadoPagoIntegration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Table: professional_mp_account ──────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS professional_mp_account (
    id                   uuid        NOT NULL DEFAULT gen_random_uuid(),
    professional_id      text        NOT NULL,
    mp_user_id           text        NOT NULL,
    mp_access_token      text        NOT NULL,
    mp_refresh_token     text        NOT NULL,
    mp_token_expires_at  timestamptz NOT NULL,
    mp_scope             text,
    mp_live_mode         boolean     NOT NULL DEFAULT false,
    status               text        NOT NULL DEFAULT 'active',
    connected_at         timestamptz NOT NULL DEFAULT now(),
    last_refreshed_at    timestamptz,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT professional_mp_account_pkey            PRIMARY KEY (id),
    CONSTRAINT professional_mp_account_status_check    CHECK (status IN ('active', 'expired', 'revoked')),
    CONSTRAINT professional_mp_account_professional_id_unique UNIQUE (professional_id)
);");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_professional_mp_account_Professional'
    ) THEN
        ALTER TABLE professional_mp_account
            ADD CONSTRAINT ""FK_professional_mp_account_Professional""
            FOREIGN KEY (professional_id) REFERENCES ""Professional""(id) ON DELETE CASCADE;
    END IF;
END $$;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_prof_mp_account_professional_id
    ON professional_mp_account (professional_id);");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_prof_mp_account_expires_status
    ON professional_mp_account (mp_token_expires_at, status)
    WHERE status = 'active';");

        // ─── Table: webhook_events ────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS webhook_events (
    provider         text        NOT NULL,
    event_id         text        NOT NULL,
    topic            text        NOT NULL,
    action           text,
    raw_payload      text        NOT NULL,
    status           text        NOT NULL DEFAULT 'received',
    error_message    text,
    created_at       timestamptz NOT NULL DEFAULT now(),
    processed_at     timestamptz,
    CONSTRAINT webhook_events_pkey         PRIMARY KEY (provider, event_id),
    CONSTRAINT webhook_events_status_check CHECK (status IN ('received', 'processed', 'failed', 'ignored'))
);");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_webhook_events_status_created
    ON webhook_events (status, created_at)
    WHERE status IN ('failed', 'received');");

        // ─── Extend "Professional" ────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""mpConnected"" boolean NOT NULL DEFAULT false;");
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""mpConnectedAt"" timestamptz;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_Professional_mpConnected""
    ON ""Professional"" (""mpConnected"")
    WHERE ""mpConnected"" = true;");

        // ─── Extend "Order" ───────────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""platformFeePercent"" numeric(5,2) NOT NULL DEFAULT 10.00;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""platformFeeCents"" integer NOT NULL DEFAULT 0;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""gatewayFeeCents"" integer NOT NULL DEFAULT 0;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""paymentStatus"" text DEFAULT 'unpaid';");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""mpPreferenceId"" text;");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_Order_mpPreferenceId""
    ON ""Order"" (""mpPreferenceId"")
    WHERE ""mpPreferenceId"" IS NOT NULL;");

        migrationBuilder.Sql(@"COMMENT ON COLUMN ""Order"".""platformFeePercent"" IS 'Snapshot da taxa da plataforma no momento do pedido';");
        migrationBuilder.Sql(@"COMMENT ON COLUMN ""Order"".""mpPreferenceId"" IS 'ID da preference criada no Mercado Pago para este pedido';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ─── Revert "Order" extensions ────────────────────────────────────────
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Order_mpPreferenceId"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""mpPreferenceId"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""paymentStatus"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""gatewayFeeCents"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""platformFeeCents"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" DROP COLUMN IF EXISTS ""platformFeePercent"";");

        // ─── Revert "Professional" extensions ────────────────────────────────
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Professional_mpConnected"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" DROP COLUMN IF EXISTS ""mpConnectedAt"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" DROP COLUMN IF EXISTS ""mpConnected"";");

        // ─── Drop webhook_events ──────────────────────────────────────────────
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_webhook_events_status_created;");
        migrationBuilder.DropTable(name: "webhook_events");

        // ─── Drop professional_mp_account ─────────────────────────────────────
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_prof_mp_account_expires_status;");
        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_prof_mp_account_professional_id;");
        migrationBuilder.DropTable(name: "professional_mp_account");
    }
}
