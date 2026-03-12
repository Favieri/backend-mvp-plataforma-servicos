-- =============================================================================
-- Fase 5 — Verificação e Métricas de Confiança
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260312070000_Phase5VerificationTrustMetrics
--
-- Execute na ordem:
--   1. Colunas de trust metrics + verificação na tabela "Professional"
--   2. Criação do enum verification_status
--   3. Criação da tabela professional_verification
--   4. Índices de suporte a filtros e jobs de recálculo
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Colunas extras na tabela "Professional"
-- Campos adicionados nas Fases 1-4 via código mas sem migration SQL:
--   entityType, documentNumber, yearsOfExperience, specialties, bufferMinutes,
--   responseRate, avgResponseTimeMinutes, completionRate,
--   verificationStatus, badges
-- =============================================================================

ALTER TABLE "Professional"
    ADD COLUMN IF NOT EXISTS "entityType"               text,
    ADD COLUMN IF NOT EXISTS "documentNumber"           text,
    ADD COLUMN IF NOT EXISTS "yearsOfExperience"        integer,
    ADD COLUMN IF NOT EXISTS "specialties"              text[],
    ADD COLUMN IF NOT EXISTS "bufferMinutes"            integer          NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "responseRate"             double precision,
    ADD COLUMN IF NOT EXISTS "avgResponseTimeMinutes"   integer,
    ADD COLUMN IF NOT EXISTS "completionRate"           double precision,
    ADD COLUMN IF NOT EXISTS "verificationStatus"       text             NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS "badges"                   text;

RAISE NOTICE '[Phase 5] Colunas de trust metrics e verificação garantidas na tabela "Professional".';

-- Constraint de domínio para verificationStatus
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'CK_Professional_verificationStatus'
    ) THEN
        ALTER TABLE "Professional"
            ADD CONSTRAINT "CK_Professional_verificationStatus"
            CHECK ("verificationStatus" IN ('pending', 'submitted', 'in_review', 'verified', 'rejected'));
        RAISE NOTICE '[Phase 5] CHECK constraint CK_Professional_verificationStatus criada.';
    ELSE
        RAISE NOTICE '[Phase 5] CHECK constraint CK_Professional_verificationStatus já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 2 — Enum verification_status
-- Controla o ciclo de vida do documento de verificação do profissional
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'verification_status') THEN
        CREATE TYPE verification_status AS ENUM (
            'pending',      -- aguardando envio de documentos
            'submitted',    -- documentos enviados, aguardando análise
            'in_review',    -- em análise pela equipe
            'verified',     -- verificado e aprovado
            'rejected'      -- rejeitado (motivo em notes)
        );
        RAISE NOTICE '[Phase 5] Enum verification_status criado.';
    ELSE
        RAISE NOTICE '[Phase 5] Enum verification_status já existe — pulando.';
    END IF;
END $$;


-- =============================================================================
-- PARTE 3 — Tabela professional_verification
-- Registro de documentos enviados por profissionais para verificação de identidade.
-- Um profissional pode ter múltiplos documentos (histórico de tentativas).
-- O registro mais recente representa o estado atual.
-- =============================================================================

CREATE TABLE IF NOT EXISTS professional_verification (
    id                  text        NOT NULL,
    professional_id     text        NOT NULL,               -- profissional que enviou o documento
    document_type       text        NOT NULL,               -- ex: 'rg', 'cnh', 'cpf', 'cnpj', 'diploma', 'crea'
    document_url        text        NOT NULL,               -- URL do arquivo no storage (Supabase Storage)
    status              text        NOT NULL DEFAULT 'submitted',  -- submitted | in_review | verified | rejected
    notes               text,                               -- observações do revisor (motivo de rejeição, etc.)
    reviewed_by         text,                               -- userId do admin que revisou
    reviewed_at         timestamp without time zone,        -- quando foi revisado
    submitted_at        timestamp without time zone NOT NULL DEFAULT now(),
    created_at          timestamp without time zone NOT NULL DEFAULT now(),
    updated_at          timestamp without time zone NOT NULL DEFAULT now(),

    CONSTRAINT "PK_professional_verification"               PRIMARY KEY (id),
    CONSTRAINT "FK_professional_verification_professional"  FOREIGN KEY (professional_id)
        REFERENCES "Professional"("id") ON DELETE CASCADE,
    CONSTRAINT "CK_professional_verification_status"        CHECK (status IN ('submitted', 'in_review', 'verified', 'rejected')),
    CONSTRAINT "CK_professional_verification_document_type" CHECK (document_type IN (
        'rg', 'cnh', 'cpf', 'cnpj', 'diploma', 'crea', 'cau', 'crm', 'oab', 'other'
    ))
);

RAISE NOTICE '[Phase 5] Tabela professional_verification garantida.';


-- =============================================================================
-- PARTE 4 — Índices de suporte
-- =============================================================================

-- Listagem de verificações por profissional (histórico)
CREATE INDEX IF NOT EXISTS "IX_professional_verification_professional_id"
    ON professional_verification (professional_id);

-- Fila de revisão: documentos submetidos ou em análise, ordenados por data
CREATE INDEX IF NOT EXISTS "IX_professional_verification_status_submitted_at"
    ON professional_verification (status, submitted_at)
    WHERE status IN ('submitted', 'in_review');

-- Filtro no marketplace: profissionais verificados
CREATE INDEX IF NOT EXISTS "IX_Professional_verificationStatus"
    ON "Professional" ("verificationStatus");

-- Filtro de marketplace por rating + verificationStatus (uso combinado frequente)
CREATE INDEX IF NOT EXISTS "IX_Professional_active_verificationStatus_rating"
    ON "Professional" (active, "verificationStatus", rating DESC NULLS LAST)
    WHERE active = true;

-- Filtro de resposta rápida
CREATE INDEX IF NOT EXISTS "IX_Professional_responseRate"
    ON "Professional" ("responseRate" DESC NULLS LAST)
    WHERE active = true AND "responseRate" IS NOT NULL;

-- Filtro por completionRate
CREATE INDEX IF NOT EXISTS "IX_Professional_completionRate"
    ON "Professional" ("completionRate" DESC NULLS LAST)
    WHERE active = true AND "completionRate" IS NOT NULL;

RAISE NOTICE '[Phase 5] Índices de suporte criados.';


-- =============================================================================
-- VERIFICAÇÃO FINAL
-- =============================================================================

DO $$
DECLARE
    verification_table_exists       boolean;
    verification_status_col_exists  boolean;
    response_rate_col_exists        boolean;
    completion_rate_col_exists      boolean;
    badges_col_exists               boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name   = 'professional_verification'
    ) INTO verification_table_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'Professional'
          AND column_name  = 'verificationStatus'
    ) INTO verification_status_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'Professional'
          AND column_name  = 'responseRate'
    ) INTO response_rate_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'Professional'
          AND column_name  = 'completionRate'
    ) INTO completion_rate_col_exists;

    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'Professional'
          AND column_name  = 'badges'
    ) INTO badges_col_exists;

    RAISE NOTICE '[Phase 5] professional_verification table exists: %',  verification_table_exists;
    RAISE NOTICE '[Phase 5] Professional.verificationStatus exists: %',  verification_status_col_exists;
    RAISE NOTICE '[Phase 5] Professional.responseRate exists: %',         response_rate_col_exists;
    RAISE NOTICE '[Phase 5] Professional.completionRate exists: %',       completion_rate_col_exists;
    RAISE NOTICE '[Phase 5] Professional.badges exists: %',               badges_col_exists;

    IF NOT (verification_table_exists AND verification_status_col_exists AND response_rate_col_exists) THEN
        RAISE EXCEPTION '[Phase 5] FALHA: nem todos os objetos foram criados corretamente. Verifique os erros acima.';
    END IF;

    RAISE NOTICE '[Phase 5] ✓ Migração concluída com sucesso.';
END $$;
