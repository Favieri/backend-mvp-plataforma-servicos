-- =============================================================================
-- Fase 1 — Pedido + Proposta + Timeline MVP
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260311180000_Phase1OrderProposalTimeline
--
-- Execute na ordem:
--   1. Alterações na tabela "Order"
--   2. Criação da tabela proposal
--   3. Criação da tabela order_timeline
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Expansão da tabela "Order" (modelo transacional)
-- Todas as colunas são nullable para retrocompatibilidade com pedidos legados
-- =============================================================================

ALTER TABLE "Order"
    ADD COLUMN IF NOT EXISTS "professionalId"      text,
    ADD COLUMN IF NOT EXISTS "tierId"              integer,
    ADD COLUMN IF NOT EXISTS "origin"              text,
    ADD COLUMN IF NOT EXISTS "proposalId"          text,
    ADD COLUMN IF NOT EXISTS "appointmentId"       text,
    ADD COLUMN IF NOT EXISTS "conversationId"      text,
    ADD COLUMN IF NOT EXISTS "priceTotalCents"     integer,
    ADD COLUMN IF NOT EXISTS "signalCents"         integer,
    ADD COLUMN IF NOT EXISTS "balanceCents"        integer,
    ADD COLUMN IF NOT EXISTS "installments"        integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS "paymentMethod"       text,
    ADD COLUMN IF NOT EXISTS "addressId"           text,
    ADD COLUMN IF NOT EXISTS "scope"               text,
    ADD COLUMN IF NOT EXISTS "scheduledAt"         timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "completedAt"         timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "cancelledAt"         timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "cancelledBy"         text,
    ADD COLUMN IF NOT EXISTS "cancellationReason"  text,
    ADD COLUMN IF NOT EXISTS "autoConfirmAt"       timestamp without time zone;

-- Comentários descritivos
COMMENT ON COLUMN "Order"."professionalId"     IS 'FK para Professional.id — preenchido nos flows Phase 1';
COMMENT ON COLUMN "Order"."tierId"             IS 'FK para service_tier.id — determina o workflow do pedido';
COMMENT ON COLUMN "Order"."origin"             IS 'booking_direct | proposal_accepted | recurring';
COMMENT ON COLUMN "Order"."proposalId"         IS 'FK para proposal.id quando origin = proposal_accepted';
COMMENT ON COLUMN "Order"."priceTotalCents"    IS 'Valor total em centavos';
COMMENT ON COLUMN "Order"."signalCents"        IS 'Sinal cobrado no booking/aceite (centavos)';
COMMENT ON COLUMN "Order"."balanceCents"       IS 'Saldo liberado após conclusão (centavos)';
COMMENT ON COLUMN "Order"."installments"       IS 'Número de parcelas (1 = à vista)';
COMMENT ON COLUMN "Order"."scheduledAt"        IS 'Data/hora agendada do serviço (substitui campo date legado)';
COMMENT ON COLUMN "Order"."autoConfirmAt"      IS 'Timestamp para auto-confirmação pelo sistema (72h após profissional marcar concluído)';

-- Índices novos em "Order"
CREATE INDEX IF NOT EXISTS "IX_Order_professionalId"
    ON "Order" ("professionalId");

CREATE INDEX IF NOT EXISTS "IX_Order_autoConfirmAt"
    ON "Order" ("autoConfirmAt")
    WHERE "autoConfirmAt" IS NOT NULL;


-- =============================================================================
-- PARTE 2 — Tabela proposal
-- Proposta formal enviada pelo profissional ao cliente (Tier 2 e Tier 3)
-- =============================================================================

CREATE TABLE IF NOT EXISTS proposal (
    id                      text        NOT NULL,
    order_id                text,
    professional_id         text        NOT NULL,
    client_id               text        NOT NULL,
    service_id              text        NOT NULL,
    professional_service_id text,
    conversation_id         text,
    scope                   text        NOT NULL,
    includes_description    text,
    excludes_description    text,
    price_total_cents       integer     NOT NULL,
    price_by_stage          jsonb,
    duration_estimate       text,
    suggested_datetime      timestamp without time zone,
    visit_fee_cents         integer     NOT NULL DEFAULT 0,
    valid_until             timestamp without time zone NOT NULL,
    status                  text        NOT NULL DEFAULT 'draft',
    rejection_reason        text,
    created_at              timestamp without time zone NOT NULL,
    updated_at              timestamp without time zone NOT NULL,

    CONSTRAINT "PK_proposal" PRIMARY KEY (id)
);

COMMENT ON TABLE  proposal                         IS 'Proposta formal criada pelo profissional para um cliente (Tier 2/3)';
COMMENT ON COLUMN proposal.order_id                IS 'Preenchido após aceite — FK para "Order".id';
COMMENT ON COLUMN proposal.price_by_stage          IS 'JSONB: [{name, amount_cents, order}] — para projetos por etapa (Tier 3)';
COMMENT ON COLUMN proposal.status                  IS 'draft | sent | accepted | negotiating | rejected | expired';
COMMENT ON COLUMN proposal.valid_until             IS 'Proposta expira automaticamente após esta data';
COMMENT ON COLUMN proposal.visit_fee_cents         IS 'Taxa de visita técnica cobrada independentemente da aprovação';

CREATE INDEX IF NOT EXISTS "IX_proposal_order_id"
    ON proposal (order_id);

CREATE INDEX IF NOT EXISTS "IX_proposal_professional_id"
    ON proposal (professional_id);

CREATE INDEX IF NOT EXISTS "IX_proposal_client_id"
    ON proposal (client_id);

CREATE INDEX IF NOT EXISTS "IX_proposal_conversation_id"
    ON proposal (conversation_id)
    WHERE conversation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_proposal_status"
    ON proposal (status);

CREATE INDEX IF NOT EXISTS "IX_proposal_valid_until"
    ON proposal (valid_until);


-- =============================================================================
-- PARTE 3 — Tabela order_timeline
-- Trilha de auditoria imutável: cada evento de ciclo de vida de um pedido
-- =============================================================================

CREATE TABLE IF NOT EXISTS order_timeline (
    id          text        NOT NULL,
    order_id    text        NOT NULL,
    event_type  text        NOT NULL,
    actor_id    text,
    actor_role  text,
    metadata    jsonb,
    created_at  timestamp without time zone NOT NULL,

    CONSTRAINT "PK_order_timeline" PRIMARY KEY (id)
);

COMMENT ON TABLE  order_timeline            IS 'Trilha imutável de eventos do ciclo de vida dos pedidos';
COMMENT ON COLUMN order_timeline.event_type IS 'Ex: order_created, status_changed_to_scheduled, completion_confirmed_by_client, auto_confirmed_by_system';
COMMENT ON COLUMN order_timeline.actor_role IS 'client | professional | system | admin';
COMMENT ON COLUMN order_timeline.metadata   IS 'JSONB livre com contexto do evento (reason, proposalId, etc.)';

CREATE INDEX IF NOT EXISTS "IX_order_timeline_order_id_created_at"
    ON order_timeline (order_id, created_at);

CREATE INDEX IF NOT EXISTS "IX_order_timeline_event_type"
    ON order_timeline (event_type);


-- =============================================================================
-- VERIFICAÇÃO (opcional — rode para confirmar que tudo foi aplicado)
-- =============================================================================
-- SELECT column_name, data_type, is_nullable
--   FROM information_schema.columns
--  WHERE table_name = 'Order'
--  ORDER BY ordinal_position;
--
-- SELECT table_name FROM information_schema.tables
--  WHERE table_name IN ('proposal', 'order_timeline');
--
-- SELECT indexname FROM pg_indexes
--  WHERE tablename IN ('Order', 'proposal', 'order_timeline')
--  ORDER BY tablename, indexname;


-- =============================================================================
-- ROLLBACK (comentado — execute manualmente se precisar reverter)
-- =============================================================================
-- DROP TABLE IF EXISTS order_timeline;
-- DROP TABLE IF EXISTS proposal;
--
-- ALTER TABLE "Order"
--     DROP COLUMN IF EXISTS "professionalId",
--     DROP COLUMN IF EXISTS "tierId",
--     DROP COLUMN IF EXISTS "origin",
--     DROP COLUMN IF EXISTS "proposalId",
--     DROP COLUMN IF EXISTS "appointmentId",
--     DROP COLUMN IF EXISTS "conversationId",
--     DROP COLUMN IF EXISTS "priceTotalCents",
--     DROP COLUMN IF EXISTS "signalCents",
--     DROP COLUMN IF EXISTS "balanceCents",
--     DROP COLUMN IF EXISTS "installments",
--     DROP COLUMN IF EXISTS "paymentMethod",
--     DROP COLUMN IF EXISTS "addressId",
--     DROP COLUMN IF EXISTS "scope",
--     DROP COLUMN IF EXISTS "scheduledAt",
--     DROP COLUMN IF EXISTS "completedAt",
--     DROP COLUMN IF EXISTS "cancelledAt",
--     DROP COLUMN IF EXISTS "cancelledBy",
--     DROP COLUMN IF EXISTS "cancellationReason",
--     DROP COLUMN IF EXISTS "autoConfirmAt";
