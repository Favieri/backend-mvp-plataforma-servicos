-- =============================================================================
-- Fase 2 — Chat Transacional + Anti-Fuga
-- Script idempotente para execução manual no Supabase SQL Editor
-- Equivalente à migration: 20260312000000_Phase2ChatTransactional
--
-- Execute na ordem:
--   1. Expansão da tabela "Message" (type, metadata, replyToId)
--   2. Expansão da tabela "Conversation" (status)
--   3. Criação da tabela message_attachment
--
-- Todas as operações usam IF NOT EXISTS / ADD COLUMN IF NOT EXISTS
-- para segurança em re-execução.
-- =============================================================================


-- =============================================================================
-- PARTE 1 — Expansão da tabela "Message"
-- Novos campos para suporte a mensagens transacionais tipadas
-- =============================================================================

ALTER TABLE "Message"
    ADD COLUMN IF NOT EXISTS "type"       text NOT NULL DEFAULT 'text',
    ADD COLUMN IF NOT EXISTS "metadata"   jsonb,
    ADD COLUMN IF NOT EXISTS "replyToId"  text;

-- Comentários descritivos
COMMENT ON COLUMN "Message"."type"      IS 'Tipo da mensagem: text | proposal | schedule_suggestion | action | system | payment | completion | dispute';
COMMENT ON COLUMN "Message"."metadata"  IS 'Payload estruturado da ação (JSONB): ID de proposta, horário sugerido, link de pagamento, etc.';
COMMENT ON COLUMN "Message"."replyToId" IS 'FK auto-ref para "Message".id — mensagem que este reply referencia';

-- FK auto-referencial para replyToId (opcional, sem CASCADE para não quebrar histórico)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_Message_replyToId'
          AND table_name = 'Message'
    ) THEN
        ALTER TABLE "Message"
            ADD CONSTRAINT "FK_Message_replyToId"
            FOREIGN KEY ("replyToId") REFERENCES "Message"("id")
            ON DELETE SET NULL;
    END IF;
END $$;

-- Índices novos em "Message"
CREATE INDEX IF NOT EXISTS "IX_Message_type"
    ON "Message" ("type");

CREATE INDEX IF NOT EXISTS "IX_Message_replyToId"
    ON "Message" ("replyToId")
    WHERE "replyToId" IS NOT NULL;


-- =============================================================================
-- PARTE 2 — Expansão da tabela "Conversation"
-- Adiciona status para controle de estado da conversa
-- =============================================================================

ALTER TABLE "Conversation"
    ADD COLUMN IF NOT EXISTS "status" text NOT NULL DEFAULT 'active';

COMMENT ON COLUMN "Conversation"."status" IS 'Estado da conversa: active | archived | flagged';

CREATE INDEX IF NOT EXISTS "IX_Conversation_status"
    ON "Conversation" ("status");


-- =============================================================================
-- PARTE 3 — Tabela message_attachment
-- Anexos de mensagens (imagens, arquivos, fotos)
-- Armazenados no Supabase Storage (bucket: chat-attachments)
-- =============================================================================

CREATE TABLE IF NOT EXISTS message_attachment (
    id              text        NOT NULL,
    message_id      text        NOT NULL,
    type            text        NOT NULL,           -- image | file | photo
    url             text        NOT NULL,
    thumbnail_url   text,
    file_name       text,
    size_bytes      integer,
    created_at      timestamp without time zone NOT NULL DEFAULT now(),

    CONSTRAINT "PK_message_attachment" PRIMARY KEY (id),
    CONSTRAINT "FK_message_attachment_message" FOREIGN KEY (message_id)
        REFERENCES "Message"("id") ON DELETE CASCADE
);

COMMENT ON TABLE  message_attachment              IS 'Anexos de mensagens de chat (imagens, arquivos, fotos)';
COMMENT ON COLUMN message_attachment.id           IS 'PK (texto, ULID ou UUID gerado pela aplicação)';
COMMENT ON COLUMN message_attachment.message_id   IS 'FK para "Message".id';
COMMENT ON COLUMN message_attachment.type         IS 'Tipo do anexo: image | file | photo';
COMMENT ON COLUMN message_attachment.url          IS 'URL pública do arquivo no Supabase Storage';
COMMENT ON COLUMN message_attachment.thumbnail_url IS 'URL do thumbnail (apenas para imagens)';
COMMENT ON COLUMN message_attachment.file_name    IS 'Nome original do arquivo enviado';
COMMENT ON COLUMN message_attachment.size_bytes   IS 'Tamanho do arquivo em bytes';
COMMENT ON COLUMN message_attachment.created_at   IS 'Timestamp de criação';

-- Índices
CREATE INDEX IF NOT EXISTS "IX_message_attachment_message_id"
    ON message_attachment (message_id);


-- =============================================================================
-- Verificação final (opcional — pode comentar se não quiser output extra)
-- =============================================================================

DO $$
DECLARE
    msg_cols  text;
    conv_cols text;
    att_exists boolean;
BEGIN
    SELECT string_agg(column_name, ', ' ORDER BY ordinal_position)
    INTO msg_cols
    FROM information_schema.columns
    WHERE table_name = 'Message'
      AND column_name IN ('type', 'metadata', 'replyToId');

    SELECT string_agg(column_name, ', ' ORDER BY ordinal_position)
    INTO conv_cols
    FROM information_schema.columns
    WHERE table_name = 'Conversation'
      AND column_name = 'status';

    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'message_attachment'
    ) INTO att_exists;

    RAISE NOTICE '[Phase 2] Message new columns: %', COALESCE(msg_cols, 'NONE');
    RAISE NOTICE '[Phase 2] Conversation new columns: %', COALESCE(conv_cols, 'NONE');
    RAISE NOTICE '[Phase 2] message_attachment table exists: %', att_exists;
END $$;
