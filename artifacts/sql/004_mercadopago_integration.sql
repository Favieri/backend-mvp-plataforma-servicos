-- =============================================================================
-- PRD-MP-01 — Mercado Pago Integration: Database Foundation
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260420000000_AddMercadoPagoIntegration
--
-- Execute na ordem:
--   1. Criação da tabela professional_mp_account (OAuth tokens por profissional)
--   2. FK para "Professional" (com guard idempotente)
--   3. Índices de suporte ao token-refresh job
--   4. Criação da tabela webhook_events (idempotência de webhooks MP)
--   5. Índice para reprocessamento de eventos com falha
--   6. Extensão da tabela "Professional" (mpConnected, mpConnectedAt)
--   7. Índice parcial em "Professional" para listagem de aptos a receber
--   8. Extensão da tabela "Order" (campos de fee e MP preference)
--   9. Índice para correlação webhook → pedido via mpPreferenceId
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Tabela professional_mp_account
-- Armazena tokens OAuth do profissional com o Mercado Pago.
-- Um profissional pode ter no máximo UMA conta MP ativa (UNIQUE constraint).
-- Se revogar e reconectar, o registro é atualizado (UPDATE), não inserido novo.
-- ATENÇÃO: mp_access_token e mp_refresh_token devem ser criptografados em
--          produção via AWS Secrets Manager ou colunas criptografadas.
--          Para MVP: armazenados como texto — débito técnico documentado.
-- =============================================================================

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
    CONSTRAINT professional_mp_account_pkey                   PRIMARY KEY (id),
    CONSTRAINT professional_mp_account_status_check           CHECK (status IN ('active', 'expired', 'revoked')),
    CONSTRAINT professional_mp_account_professional_id_unique UNIQUE (professional_id)
);

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Tabela professional_mp_account: OK (já existia ou foi criada).';
END $$;


-- =============================================================================
-- PARTE 2 — FK: professional_mp_account → "Professional"
-- Adicionada em bloco separado para suportar re-execução idempotente.
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_professional_mp_account_Professional'
    ) THEN
        ALTER TABLE professional_mp_account
            ADD CONSTRAINT "FK_professional_mp_account_Professional"
            FOREIGN KEY (professional_id) REFERENCES "Professional"(id) ON DELETE CASCADE;
        RAISE NOTICE '[MP-01] FK_professional_mp_account_Professional adicionada.';
    ELSE
        RAISE NOTICE '[MP-01] FK_professional_mp_account_Professional já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 3 — Índices de professional_mp_account
-- =============================================================================

-- Lookup crítico em toda criação de MP preference
CREATE INDEX IF NOT EXISTS idx_prof_mp_account_professional_id
    ON professional_mp_account (professional_id);

-- Job de refresh: busca tokens ativos prestes a expirar
CREATE INDEX IF NOT EXISTS idx_prof_mp_account_expires_status
    ON professional_mp_account (mp_token_expires_at, status)
    WHERE status = 'active';

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Índices de professional_mp_account: OK.';
END $$;


-- =============================================================================
-- PARTE 4 — Tabela webhook_events
-- Garante idempotência no processamento de webhooks do MP.
-- PRIMARY KEY (provider, event_id) impede que o mesmo evento seja processado
-- duas vezes. Use INSERT ON CONFLICT DO NOTHING no handler.
-- =============================================================================

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
);

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Tabela webhook_events: OK (já existia ou foi criada).';
END $$;


-- =============================================================================
-- PARTE 5 — Índice de webhook_events
-- =============================================================================

-- Reprocessamento de eventos com falha ou ainda não processados
CREATE INDEX IF NOT EXISTS idx_webhook_events_status_created
    ON webhook_events (status, created_at)
    WHERE status IN ('failed', 'received');

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Índice de webhook_events: OK.';
END $$;


-- =============================================================================
-- PARTE 6 — Extensão da tabela "Professional"
-- mpConnected: snapshot desnormalizado para evitar JOIN em toda listagem.
-- Deve ser atualizado via trigger ou pelo serviço de OAuth ao conectar/revogar.
-- =============================================================================

ALTER TABLE "Professional"
    ADD COLUMN IF NOT EXISTS "mpConnected"   boolean     NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "mpConnectedAt" timestamptz;

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Colunas mpConnected/mpConnectedAt em "Professional": OK.';
END $$;


-- =============================================================================
-- PARTE 7 — Índice parcial em "Professional"
-- Filtra apenas profissionais com conta MP ativa para queries de listagem.
-- =============================================================================

CREATE INDEX IF NOT EXISTS "IX_Professional_mpConnected"
    ON "Professional" ("mpConnected")
    WHERE "mpConnected" = true;

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Índice IX_Professional_mpConnected: OK.';
END $$;


-- =============================================================================
-- PARTE 8 — Extensão da tabela "Order"
-- Campos de fee armazenam snapshot imutável dos valores no momento do pedido
-- (taxas podem mudar no futuro — o histórico de cada pedido é preservado).
-- =============================================================================

ALTER TABLE "Order"
    ADD COLUMN IF NOT EXISTS "platformFeePercent" numeric(5,2) NOT NULL DEFAULT 10.00,
    ADD COLUMN IF NOT EXISTS "platformFeeCents"   integer      NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "gatewayFeeCents"    integer      NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "paymentStatus"      text         DEFAULT 'unpaid',
    ADD COLUMN IF NOT EXISTS "mpPreferenceId"     text;

COMMENT ON COLUMN "Order"."platformFeePercent" IS 'Snapshot da taxa da plataforma no momento do pedido';
COMMENT ON COLUMN "Order"."mpPreferenceId"     IS 'ID da preference criada no Mercado Pago para este pedido';

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Colunas MP em "Order": OK.';
END $$;


-- =============================================================================
-- PARTE 9 — Índice em "Order".mpPreferenceId
-- Suporte à correlação webhook → pedido ao receber notificação do MP.
-- =============================================================================

CREATE INDEX IF NOT EXISTS "IX_Order_mpPreferenceId"
    ON "Order" ("mpPreferenceId")
    WHERE "mpPreferenceId" IS NOT NULL;

DO $$
BEGIN
    RAISE NOTICE '[MP-01] Índice IX_Order_mpPreferenceId: OK.';
END $$;


-- =============================================================================
-- Verificação final
-- =============================================================================

DO $$
DECLARE
    mp_account_exists       boolean;
    webhook_events_exists   boolean;
    mp_connected_col_exists boolean;
    platform_fee_col_exists boolean;
    mp_preference_col_exists boolean;
    fk_exists               boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'professional_mp_account'
    ) INTO mp_account_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'webhook_events'
    ) INTO webhook_events_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Professional' AND column_name = 'mpConnected'
    ) INTO mp_connected_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Order' AND column_name = 'platformFeePercent'
    ) INTO platform_fee_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Order' AND column_name = 'mpPreferenceId'
    ) INTO mp_preference_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_professional_mp_account_Professional'
    ) INTO fk_exists;

    RAISE NOTICE '[MP-01] professional_mp_account table exists:  %', mp_account_exists;
    RAISE NOTICE '[MP-01] webhook_events table exists:           %', webhook_events_exists;
    RAISE NOTICE '[MP-01] Professional.mpConnected col exists:   %', mp_connected_col_exists;
    RAISE NOTICE '[MP-01] Order.platformFeePercent col exists:   %', platform_fee_col_exists;
    RAISE NOTICE '[MP-01] Order.mpPreferenceId col exists:       %', mp_preference_col_exists;
    RAISE NOTICE '[MP-01] FK to Professional exists:             %', fk_exists;
END $$;
