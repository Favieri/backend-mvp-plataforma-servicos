-- PRD Confirmação de conta e recuperação de senha
-- SQL idempotente para execução manual (Supabase SQL Editor ou psql), independente de
-- quando a migration EF equivalente (20260712140000_AddAccountConfirmationTables) for aplicada.

-- ─── Extend "User" ───────────────────────────────────────────────────────────
ALTER TABLE "User" ADD COLUMN IF NOT EXISTS email_verified boolean NOT NULL DEFAULT false;

-- ─── Tabela account_token ──────────────────────────────────────────────────────
-- Único mecanismo de token, reaproveitado tanto para confirmação de e-mail quanto para
-- recuperação de senha. O token em si nunca é armazenado em texto puro — apenas seu hash
-- SHA-256 (token_hash), no mesmo espírito de nunca guardar a senha em texto puro.
CREATE TABLE IF NOT EXISTS account_token (
    id          text PRIMARY KEY,
    user_id     text NOT NULL REFERENCES "User"(id) ON DELETE CASCADE,
    type        text NOT NULL,        -- 'email_verification' | 'password_reset'
    token_hash  text NOT NULL,        -- SHA-256 do token — nunca o token em texto puro
    expires_at  timestamp NOT NULL,
    used_at     timestamp NULL,
    created_at  timestamp NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_account_token_user_id ON account_token (user_id);
CREATE INDEX IF NOT EXISTS idx_account_token_hash_type ON account_token (token_hash, type);

-- Após aplicar via EF, registre a migration para evitar reaplicação:
--   INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
--   VALUES ('20260712140000_AddAccountConfirmationTables', '8.0.10')
--   ON CONFLICT ("MigrationId") DO NOTHING;
