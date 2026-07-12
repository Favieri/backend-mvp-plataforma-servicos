using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD Confirmação de conta e recuperação de senha:
///   - "User".email_verified (boolean, default false).
///   - account_token: mecanismo único de token (hash SHA-256, nunca texto puro) reaproveitado
///     tanto para confirmação de e-mail quanto para recuperação de senha.
///
/// IDEMPOTENTE: todas as DDL usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS.
/// </summary>
public partial class AddAccountConfirmationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Extend "User" ────────────────────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS email_verified boolean NOT NULL DEFAULT false;");

        // ─── Tabela account_token ─────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS account_token (
    id          text PRIMARY KEY,
    user_id     text NOT NULL REFERENCES ""User""(id) ON DELETE CASCADE,
    type        text NOT NULL,
    token_hash  text NOT NULL,
    expires_at  timestamp NOT NULL,
    used_at     timestamp NULL,
    created_at  timestamp NOT NULL DEFAULT now()
);");

        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS idx_account_token_user_id ON account_token (user_id);");
        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS idx_account_token_hash_type ON account_token (token_hash, type);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS account_token;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" DROP COLUMN IF EXISTS email_verified;");
    }
}
