using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD-18a: Cria a tabela user_address para suporte a múltiplos endereços por usuário.
/// Também cria índices e migra os endereços existentes (campos addr_* da tabela User).
///
/// IDEMPOTENTE: usa IF NOT EXISTS / ON CONFLICT DO NOTHING para poder ser
/// reaplicada com segurança caso o schema já exista no Supabase.
///
/// Após aplicar via EF, execute também no Supabase SQL Editor:
///   INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
///   VALUES ('20260526000000_AddUserAddressTable', '8.0.10')
///   ON CONFLICT ("MigrationId") DO NOTHING;
/// </summary>
public partial class AddUserAddressTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── Tabela principal ─────────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS user_address (
  id              text        PRIMARY KEY DEFAULT gen_random_uuid()::text,
  user_id         text        NOT NULL REFERENCES ""User""(id) ON DELETE CASCADE,
  label           text,
  zip_code        text        NOT NULL,
  street          text        NOT NULL,
  number          text        NOT NULL,
  neighborhood    text        NOT NULL,
  city            text        NOT NULL,
  state           text        NOT NULL,
  complement      text,
  reference       text,
  is_default      boolean     NOT NULL DEFAULT false,
  last_used_at    timestamptz,
  created_at      timestamptz NOT NULL DEFAULT now()
);");

        // ─── Índices ──────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_user_address_user_id
  ON user_address (user_id);");

        // Garante no máximo um is_default=true por usuário (constraint parcial)
        migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_address_default_per_user
  ON user_address (user_id)
  WHERE is_default = true;");

        // ─── Migração de endereços existentes (addr_* → user_address) ─────────
        migrationBuilder.Sql(@"
INSERT INTO user_address (id, user_id, zip_code, street, number, neighborhood,
                          city, state, complement, reference, is_default, created_at)
SELECT
  gen_random_uuid()::text,
  id,
  addr_zip_code,
  addr_street,
  addr_number,
  addr_neighborhood,
  addr_city,
  addr_state,
  addr_complement,
  addr_reference,
  true,
  now()
FROM ""User""
WHERE addr_zip_code IS NOT NULL
  AND addr_zip_code != ''
  AND addr_street IS NOT NULL
  AND addr_street != ''
ON CONFLICT DO NOTHING;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS user_address;");
    }
}
