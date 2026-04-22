using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-MP-01 + PRD-MP-02: Mercado Pago OAuth integration.
///
/// - Creates professional_mp_account table (one row per professional, upserted on connect)
/// - Adds mp_connected (bool, default false) and mp_connected_at (timestamp?) to "Professional"
///
/// IDEMPOTENT: all statements use IF NOT EXISTS / IF EXISTS guards.
/// </summary>
public partial class AddMercadoPagoOAuth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Create professional_mp_account ──────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS professional_mp_account (
    id text NOT NULL,
    professional_id text NOT NULL,
    mp_user_id bigint NOT NULL,
    mp_access_token text NOT NULL,
    mp_refresh_token text NOT NULL,
    mp_token_expires_at timestamp without time zone NOT NULL,
    status text NOT NULL DEFAULT 'active',
    live_mode boolean NOT NULL DEFAULT true,
    created_at timestamp without time zone NOT NULL,
    updated_at timestamp without time zone NOT NULL,
    CONSTRAINT ""PK_professional_mp_account"" PRIMARY KEY (id)
);");

        // One account per professional
        migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_professional_mp_account_professional_id""
ON professional_mp_account (professional_id);");

        // Index for the token refresh background job
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_professional_mp_account_status_expires""
ON professional_mp_account (status, mp_token_expires_at);");

        // ─── Professional: add MP connection flags ────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""mp_connected"" boolean NOT NULL DEFAULT false;");
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" ADD COLUMN IF NOT EXISTS ""mp_connected_at"" timestamp without time zone;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" DROP COLUMN IF EXISTS ""mp_connected_at"";");
        migrationBuilder.Sql(@"ALTER TABLE ""Professional"" DROP COLUMN IF EXISTS ""mp_connected"";");
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS professional_mp_account;");
    }
}
