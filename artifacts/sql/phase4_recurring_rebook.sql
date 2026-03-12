-- =============================================================================
-- Fase 4 — Recorrência + Recontratação
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260312060000_Phase4RecurringRebook
--
-- Execute na ordem:
--   1. Criação do enum recurring_frequency e recurring_plan_status
--   2. Criação da tabela recurring_plan
--   3. Criação da tabela recurring_occurrence
--   4. Adição de coluna recurring_plan_id em "Order" (para rastrear origem)
--   5. Índices de suporte ao RecurringBillingJob
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Enums de recorrência
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'recurring_frequency') THEN
        CREATE TYPE recurring_frequency AS ENUM (
            'weekly',
            'biweekly',
            'monthly'
        );
        RAISE NOTICE '[Phase 4] Enum recurring_frequency criado.';
    ELSE
        RAISE NOTICE '[Phase 4] Enum recurring_frequency já existe — pulando.';
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'recurring_plan_status') THEN
        CREATE TYPE recurring_plan_status AS ENUM (
            'active',
            'paused',
            'cancelled'
        );
        RAISE NOTICE '[Phase 4] Enum recurring_plan_status criado.';
    ELSE
        RAISE NOTICE '[Phase 4] Enum recurring_plan_status já existe — pulando.';
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'recurring_occurrence_status') THEN
        CREATE TYPE recurring_occurrence_status AS ENUM (
            'pending',
            'order_created',
            'skipped',
            'failed'
        );
        RAISE NOTICE '[Phase 4] Enum recurring_occurrence_status criado.';
    ELSE
        RAISE NOTICE '[Phase 4] Enum recurring_occurrence_status já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 2 — Tabela recurring_plan
-- Plano de contratação recorrente criado a partir de um pedido concluído.
-- Um cliente pode ter múltiplos planos ativos com o mesmo profissional.
-- =============================================================================

CREATE TABLE IF NOT EXISTS recurring_plan (
    id                  text        NOT NULL,
    client_id           text        NOT NULL,
    professional_id     text        NOT NULL,
    service_id          text        NOT NULL,
    source_order_id     text        NOT NULL,               -- pedido original que originou o plano
    frequency           text        NOT NULL DEFAULT 'monthly',
    price_total_cents   integer     NOT NULL,               -- preço base (sem desconto)
    discount_percent    integer     NOT NULL DEFAULT 0,     -- % de desconto recorrente (ex: 10 = 10%)
    payment_method      text,
    scope               text,
    address_id          text,
    status              text        NOT NULL DEFAULT 'active',
    next_billing_at     timestamp without time zone NOT NULL, -- quando gerar o próximo pedido
    occurrence_count    integer     NOT NULL DEFAULT 0,     -- quantas ocorrências já foram geradas
    started_at          timestamp without time zone NOT NULL DEFAULT now(),
    paused_at           timestamp without time zone,
    cancelled_at        timestamp without time zone,
    created_at          timestamp without time zone NOT NULL DEFAULT now(),

    CONSTRAINT "PK_recurring_plan"              PRIMARY KEY (id),
    CONSTRAINT "FK_recurring_plan_client"       FOREIGN KEY (client_id)
        REFERENCES "User"("id") ON DELETE RESTRICT,
    CONSTRAINT "FK_recurring_plan_source_order" FOREIGN KEY (source_order_id)
        REFERENCES "Order"("id") ON DELETE RESTRICT,
    CONSTRAINT "CK_recurring_plan_discount"     CHECK (discount_percent >= 0 AND discount_percent <= 100),
    CONSTRAINT "CK_recurring_plan_price"        CHECK (price_total_cents > 0),
    CONSTRAINT "CK_recurring_plan_frequency"    CHECK (frequency IN ('weekly', 'biweekly', 'monthly')),
    CONSTRAINT "CK_recurring_plan_status"       CHECK (status IN ('active', 'paused', 'cancelled'))
);

COMMENT ON TABLE  recurring_plan                        IS 'Planos de contratação recorrente — gerados a partir de pedidos concluídos';
COMMENT ON COLUMN recurring_plan.id                     IS 'PK (texto, ULID ou UUID gerado pela aplicação)';
COMMENT ON COLUMN recurring_plan.client_id              IS 'FK para "User".id — cliente que criou o plano';
COMMENT ON COLUMN recurring_plan.professional_id        IS 'Id do profissional (desnormalizado para queries)';
COMMENT ON COLUMN recurring_plan.service_id             IS 'FK para "Service".id';
COMMENT ON COLUMN recurring_plan.source_order_id        IS 'FK para "Order".id — pedido concluído que originou o plano';
COMMENT ON COLUMN recurring_plan.frequency              IS 'Frequência: weekly | biweekly | monthly';
COMMENT ON COLUMN recurring_plan.price_total_cents      IS 'Valor base do serviço em centavos (antes do desconto)';
COMMENT ON COLUMN recurring_plan.discount_percent       IS 'Desconto recorrente em % (0–100). Ex: 10 = 10% off';
COMMENT ON COLUMN recurring_plan.payment_method         IS 'Método de pagamento padrão do plano';
COMMENT ON COLUMN recurring_plan.scope                  IS 'Escopo/descrição do serviço';
COMMENT ON COLUMN recurring_plan.address_id             IS 'Endereço padrão para as ocorrências';
COMMENT ON COLUMN recurring_plan.status                 IS 'Estado: active | paused | cancelled';
COMMENT ON COLUMN recurring_plan.next_billing_at        IS 'Próxima data em que o RecurringBillingJob deve gerar um pedido';
COMMENT ON COLUMN recurring_plan.occurrence_count       IS 'Contador de ocorrências geradas com sucesso';
COMMENT ON COLUMN recurring_plan.started_at             IS 'Data de início do plano';
COMMENT ON COLUMN recurring_plan.paused_at              IS 'Data em que o plano foi pausado (nullable)';
COMMENT ON COLUMN recurring_plan.cancelled_at           IS 'Data em que o plano foi cancelado (nullable)';


-- =============================================================================
-- PARTE 3 — Tabela recurring_occurrence
-- Cada execução do plano gera uma ocorrência vinculada a um "Order".
-- =============================================================================

CREATE TABLE IF NOT EXISTS recurring_occurrence (
    id                  text        NOT NULL,
    plan_id             text        NOT NULL,
    order_id            text,                               -- NULL enquanto status = 'pending' ou 'failed'
    occurrence_number   integer     NOT NULL,               -- 1, 2, 3, … (sequencial por plano)
    scheduled_for       timestamp without time zone NOT NULL, -- data/hora planejada para a ocorrência
    status              text        NOT NULL DEFAULT 'pending',
    failure_reason      text,                               -- mensagem de erro caso status = 'failed'
    created_at          timestamp without time zone NOT NULL DEFAULT now(),

    CONSTRAINT "PK_recurring_occurrence"            PRIMARY KEY (id),
    CONSTRAINT "FK_recurring_occurrence_plan"       FOREIGN KEY (plan_id)
        REFERENCES recurring_plan (id) ON DELETE CASCADE,
    CONSTRAINT "FK_recurring_occurrence_order"      FOREIGN KEY (order_id)
        REFERENCES "Order"("id") ON DELETE SET NULL,
    CONSTRAINT "UQ_recurring_occurrence_plan_num"   UNIQUE (plan_id, occurrence_number),
    CONSTRAINT "CK_recurring_occurrence_status"     CHECK (status IN ('pending', 'order_created', 'skipped', 'failed'))
);

COMMENT ON TABLE  recurring_occurrence                      IS 'Cada execução de um plano recorrente — rastreia o pedido gerado';
COMMENT ON COLUMN recurring_occurrence.id                   IS 'PK (texto, ULID ou UUID gerado pela aplicação)';
COMMENT ON COLUMN recurring_occurrence.plan_id              IS 'FK para recurring_plan.id';
COMMENT ON COLUMN recurring_occurrence.order_id             IS 'FK para "Order".id — pedido criado por esta ocorrência (nullable até criação)';
COMMENT ON COLUMN recurring_occurrence.occurrence_number    IS 'Número sequencial da ocorrência dentro do plano (1, 2, 3…)';
COMMENT ON COLUMN recurring_occurrence.scheduled_for        IS 'Data planejada para execução desta ocorrência';
COMMENT ON COLUMN recurring_occurrence.status               IS 'Estado: pending | order_created | skipped | failed';
COMMENT ON COLUMN recurring_occurrence.failure_reason       IS 'Motivo de falha caso status = failed';
COMMENT ON COLUMN recurring_occurrence.created_at           IS 'Quando a ocorrência foi registrada pelo job';


-- =============================================================================
-- PARTE 4 — Coluna recurring_plan_id em "Order"
-- Rastreia se um pedido foi gerado por um plano recorrente.
-- =============================================================================

ALTER TABLE "Order"
    ADD COLUMN IF NOT EXISTS "recurringPlanId" text;

COMMENT ON COLUMN "Order"."recurringPlanId" IS 'FK para recurring_plan.id — preenchido quando o pedido foi gerado pelo RecurringBillingJob ou por recontratação com plano';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_Order_recurringPlan'
          AND table_name = 'Order'
    ) THEN
        ALTER TABLE "Order"
            ADD CONSTRAINT "FK_Order_recurringPlan"
            FOREIGN KEY ("recurringPlanId")
            REFERENCES recurring_plan (id) ON DELETE SET NULL;
        RAISE NOTICE '[Phase 4] FK_Order_recurringPlan adicionada.';
    ELSE
        RAISE NOTICE '[Phase 4] FK_Order_recurringPlan já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 5 — Índices
-- Suporte ao RecurringBillingJob e queries de listagem
-- =============================================================================

-- Job: busca planos ativos onde next_billing_at <= now()
CREATE INDEX IF NOT EXISTS "IX_recurring_plan_next_billing_at_status"
    ON recurring_plan (next_billing_at, status)
    WHERE status = 'active';

COMMENT ON INDEX "IX_recurring_plan_next_billing_at_status"
    IS 'Suporte ao RecurringBillingJob: find active plans where next_billing_at <= now()';

-- Listagem por cliente
CREATE INDEX IF NOT EXISTS "IX_recurring_plan_client_id"
    ON recurring_plan (client_id);

-- Listagem por profissional
CREATE INDEX IF NOT EXISTS "IX_recurring_plan_professional_id"
    ON recurring_plan (professional_id);

-- Busca de ocorrências por plano (ordenação por número)
CREATE INDEX IF NOT EXISTS "IX_recurring_occurrence_plan_id"
    ON recurring_occurrence (plan_id, occurrence_number);

-- Busca de ocorrências por order_id
CREATE INDEX IF NOT EXISTS "IX_recurring_occurrence_order_id"
    ON recurring_occurrence (order_id)
    WHERE order_id IS NOT NULL;

-- Índice em Order para rastrear pedidos recorrentes
CREATE INDEX IF NOT EXISTS "IX_Order_recurringPlanId"
    ON "Order" ("recurringPlanId")
    WHERE "recurringPlanId" IS NOT NULL;


-- =============================================================================
-- Verificação final
-- =============================================================================

DO $$
DECLARE
    plan_exists         boolean;
    occurrence_exists   boolean;
    order_col_exists    boolean;
    billing_idx_exists  boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'recurring_plan'
    ) INTO plan_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'recurring_occurrence'
    ) INTO occurrence_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Order' AND column_name = 'recurringPlanId'
    ) INTO order_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'IX_recurring_plan_next_billing_at_status'
    ) INTO billing_idx_exists;

    RAISE NOTICE '[Phase 4] recurring_plan table exists:        %', plan_exists;
    RAISE NOTICE '[Phase 4] recurring_occurrence table exists:  %', occurrence_exists;
    RAISE NOTICE '[Phase 4] Order.recurringPlanId col exists:   %', order_col_exists;
    RAISE NOTICE '[Phase 4] RecurringBillingJob index exists:   %', billing_idx_exists;
END $$;
