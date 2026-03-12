-- =============================================================================
-- Fase 3 — Disputa + Avaliação Expandida (Double-Blind)
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260312000000_Phase3DisputeReviewExpanded
--
-- Execute na ordem:
--   1. Criação do enum dispute_status (se não existir)
--   2. Criação da tabela dispute
--   3. Expansão da tabela "Review" com categorias, fotos e double-blind
--   4. Índices de suporte ao ProposalExpirationJob (proposals.valid_until)
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Enum dispute_status
-- Controla o ciclo de vida de uma disputa
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'dispute_status') THEN
        CREATE TYPE dispute_status AS ENUM (
            'opened',
            'professional_responded',
            'mediating',
            'resolved',
            'closed'
        );
        RAISE NOTICE '[Phase 3] Enum dispute_status criado.';
    ELSE
        RAISE NOTICE '[Phase 3] Enum dispute_status já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 2 — Tabela dispute
-- Contestações abertas por clientes contra pedidos concluídos ou em andamento.
-- Uma disputa por pedido (UNIQUE em order_id).
-- =============================================================================

CREATE TABLE IF NOT EXISTS dispute (
    id                          text        NOT NULL,
    order_id                    text        NOT NULL,
    client_id                   text        NOT NULL,
    professional_id             text        NOT NULL,
    reason                      text        NOT NULL,
    description                 text,
    evidence_urls               jsonb,                              -- array de URLs de evidência do cliente
    professional_response       text,
    professional_evidence_urls  jsonb,                              -- array de URLs de evidência do profissional
    resolution                  text,
    resolved_by                 text,                               -- 'system' | 'mediator' | 'agreement'
    refund_amount_cents         integer,
    status                      text        NOT NULL DEFAULT 'opened',
    created_at                  timestamp without time zone NOT NULL DEFAULT now(),
    resolved_at                 timestamp without time zone,

    CONSTRAINT "PK_dispute"             PRIMARY KEY (id),
    CONSTRAINT "UQ_dispute_order_id"    UNIQUE      (order_id),
    CONSTRAINT "FK_dispute_order"       FOREIGN KEY (order_id)
        REFERENCES "Order"("id") ON DELETE RESTRICT,
    CONSTRAINT "FK_dispute_client"      FOREIGN KEY (client_id)
        REFERENCES "User"("id") ON DELETE RESTRICT,
    CONSTRAINT "CK_dispute_status"      CHECK (status IN (
        'opened', 'professional_responded', 'mediating', 'resolved', 'closed'
    ))
);

COMMENT ON TABLE  dispute                           IS 'Disputas abertas por clientes em pedidos — uma por pedido (UNIQUE order_id)';
COMMENT ON COLUMN dispute.id                        IS 'PK (texto, ULID ou UUID gerado pela aplicação)';
COMMENT ON COLUMN dispute.order_id                  IS 'FK para "Order".id — UNIQUE, uma disputa por pedido';
COMMENT ON COLUMN dispute.client_id                 IS 'FK para "User".id — quem abriu a disputa';
COMMENT ON COLUMN dispute.professional_id           IS 'Id do profissional (desnormalizado para queries rápidas)';
COMMENT ON COLUMN dispute.reason                    IS 'Motivo curto da disputa (ex: serviço não concluído)';
COMMENT ON COLUMN dispute.description               IS 'Descrição detalhada fornecida pelo cliente';
COMMENT ON COLUMN dispute.evidence_urls             IS 'JSONB: array de URLs de fotos/arquivos de evidência do cliente';
COMMENT ON COLUMN dispute.professional_response     IS 'Resposta do profissional à disputa';
COMMENT ON COLUMN dispute.professional_evidence_urls IS 'JSONB: array de URLs de evidência do profissional';
COMMENT ON COLUMN dispute.resolution                IS 'Texto da resolução final';
COMMENT ON COLUMN dispute.resolved_by               IS 'Quem resolveu: system | mediator | agreement';
COMMENT ON COLUMN dispute.refund_amount_cents       IS 'Valor de reembolso aprovado em centavos (nullable)';
COMMENT ON COLUMN dispute.status                    IS 'Estado: opened | professional_responded | mediating | resolved | closed';
COMMENT ON COLUMN dispute.created_at                IS 'Timestamp de abertura da disputa';
COMMENT ON COLUMN dispute.resolved_at               IS 'Timestamp de resolução (nullable)';

-- Índices para dispute
CREATE INDEX IF NOT EXISTS "IX_dispute_order_id"
    ON dispute (order_id);

CREATE INDEX IF NOT EXISTS "IX_dispute_professional_id"
    ON dispute (professional_id);

CREATE INDEX IF NOT EXISTS "IX_dispute_status"
    ON dispute (status)
    WHERE status NOT IN ('resolved', 'closed');

CREATE INDEX IF NOT EXISTS "IX_dispute_client_id"
    ON dispute (client_id);


-- =============================================================================
-- PARTE 3 — Expansão da tabela "Review"
-- Categorias de nota, fotos e double-blind
-- =============================================================================

-- Categorias de avaliação (1-5 cada)
ALTER TABLE "Review"
    ADD COLUMN IF NOT EXISTS "punctualityRating"    integer,
    ADD COLUMN IF NOT EXISTS "qualityRating"        integer,
    ADD COLUMN IF NOT EXISTS "communicationRating"  integer,
    ADD COLUMN IF NOT EXISTS "cleanlinessRating"    integer;

COMMENT ON COLUMN "Review"."punctualityRating"   IS 'Nota de pontualidade (1-5, nullable)';
COMMENT ON COLUMN "Review"."qualityRating"       IS 'Nota de qualidade do serviço (1-5, nullable)';
COMMENT ON COLUMN "Review"."communicationRating" IS 'Nota de comunicação (1-5, nullable)';
COMMENT ON COLUMN "Review"."cleanlinessRating"   IS 'Nota de limpeza/organização (1-5, nullable)';

-- Check constraints para categorias (1-5)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'CK_Review_punctualityRating'
          AND table_name = 'Review'
    ) THEN
        ALTER TABLE "Review"
            ADD CONSTRAINT "CK_Review_punctualityRating"
            CHECK ("punctualityRating" IS NULL OR ("punctualityRating" BETWEEN 1 AND 5));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'CK_Review_qualityRating'
          AND table_name = 'Review'
    ) THEN
        ALTER TABLE "Review"
            ADD CONSTRAINT "CK_Review_qualityRating"
            CHECK ("qualityRating" IS NULL OR ("qualityRating" BETWEEN 1 AND 5));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'CK_Review_communicationRating'
          AND table_name = 'Review'
    ) THEN
        ALTER TABLE "Review"
            ADD CONSTRAINT "CK_Review_communicationRating"
            CHECK ("communicationRating" IS NULL OR ("communicationRating" BETWEEN 1 AND 5));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'CK_Review_cleanlinessRating'
          AND table_name = 'Review'
    ) THEN
        ALTER TABLE "Review"
            ADD CONSTRAINT "CK_Review_cleanlinessRating"
            CHECK ("cleanlinessRating" IS NULL OR ("cleanlinessRating" BETWEEN 1 AND 5));
    END IF;
END $$;

-- Fotos
ALTER TABLE "Review"
    ADD COLUMN IF NOT EXISTS "photoUrls" jsonb;

COMMENT ON COLUMN "Review"."photoUrls" IS 'JSONB: array de URLs de fotos da avaliação (Supabase Storage)';

-- Profissional avalia cliente (bidirecional)
ALTER TABLE "Review"
    ADD COLUMN IF NOT EXISTS "professionalReviewOfClient"  text,
    ADD COLUMN IF NOT EXISTS "professionalRatingOfClient"  integer;

COMMENT ON COLUMN "Review"."professionalReviewOfClient" IS 'Comentário do profissional sobre o cliente (nullable)';
COMMENT ON COLUMN "Review"."professionalRatingOfClient" IS 'Nota do profissional sobre o cliente (1-5, nullable)';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'CK_Review_professionalRatingOfClient'
          AND table_name = 'Review'
    ) THEN
        ALTER TABLE "Review"
            ADD CONSTRAINT "CK_Review_professionalRatingOfClient"
            CHECK ("professionalRatingOfClient" IS NULL OR ("professionalRatingOfClient" BETWEEN 1 AND 5));
    END IF;
END $$;

-- Double-blind: timestamps de visibilidade
ALTER TABLE "Review"
    ADD COLUMN IF NOT EXISTS "clientVisibleAt"       timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "professionalVisibleAt" timestamp without time zone;

COMMENT ON COLUMN "Review"."clientVisibleAt"       IS 'Quando a review do profissional ficou visível para o cliente (double-blind)';
COMMENT ON COLUMN "Review"."professionalVisibleAt" IS 'Quando a review do cliente ficou visível para o profissional (double-blind)';

-- Verificação de integridade: review vinculada a pedido com pagamento
ALTER TABLE "Review"
    ADD COLUMN IF NOT EXISTS "isVerified" boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN "Review"."isVerified" IS 'true quando a review é de pedido concluído com pagamento confirmado';


-- =============================================================================
-- PARTE 4 — Índice de suporte ao ProposalExpirationJob
-- Permite varredura eficiente de propostas vencidas
-- =============================================================================

CREATE INDEX IF NOT EXISTS "IX_proposal_valid_until_status"
    ON proposal (valid_until, status)
    WHERE status = 'sent';

COMMENT ON INDEX "IX_proposal_valid_until_status" IS 'Suporte ao ProposalExpirationJob: find sent proposals where valid_until < now()';

-- Índice em Review para profissional avaliando cliente
CREATE INDEX IF NOT EXISTS "IX_Review_professionalRatingOfClient"
    ON "Review" ("professionalId", "professionalRatingOfClient")
    WHERE "professionalRatingOfClient" IS NOT NULL;

-- Índice em Review para double-blind visibility
CREATE INDEX IF NOT EXISTS "IX_Review_clientVisibleAt"
    ON "Review" ("clientVisibleAt")
    WHERE "clientVisibleAt" IS NULL;

CREATE INDEX IF NOT EXISTS "IX_Review_professionalVisibleAt"
    ON "Review" ("professionalVisibleAt")
    WHERE "professionalVisibleAt" IS NULL;


-- =============================================================================
-- Verificação final
-- =============================================================================

DO $$
DECLARE
    dispute_exists  boolean;
    review_cols     text;
    proposal_idx    boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'dispute'
    ) INTO dispute_exists;

    SELECT string_agg(column_name, ', ' ORDER BY ordinal_position)
    INTO review_cols
    FROM information_schema.columns
    WHERE table_name = 'Review'
      AND column_name IN (
          'punctualityRating', 'qualityRating', 'communicationRating',
          'cleanlinessRating', 'photoUrls', 'professionalReviewOfClient',
          'professionalRatingOfClient', 'clientVisibleAt', 'professionalVisibleAt',
          'isVerified'
      );

    SELECT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE indexname = 'IX_proposal_valid_until_status'
    ) INTO proposal_idx;

    RAISE NOTICE '[Phase 3] dispute table exists: %',       dispute_exists;
    RAISE NOTICE '[Phase 3] Review new columns: %',         COALESCE(review_cols, 'NONE');
    RAISE NOTICE '[Phase 3] ProposalExpiration index: %',   proposal_idx;
END $$;
