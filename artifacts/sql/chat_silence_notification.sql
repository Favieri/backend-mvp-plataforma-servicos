-- ============================================================================
-- Migration manual: estado de notificação de silêncio no chat
-- Corresponde a: PRD "Notificação de chat por e-mail: job periódico", Seção 3
-- ============================================================================

CREATE TABLE IF NOT EXISTS chat_notification_state (
    conversation_id           text NOT NULL,
    recipient_user_id         text NOT NULL,
    last_notified_message_id  text NOT NULL,
    notified_at               timestamp NOT NULL DEFAULT now(),
    PRIMARY KEY (conversation_id, recipient_user_id)
);

-- Índice de apoio para a varredura do job (buscar por conversa)
CREATE INDEX IF NOT EXISTS ix_chat_notification_state_conversation
    ON chat_notification_state (conversation_id);

-- Verificação pós-execução:
--   SELECT table_name FROM information_schema.tables WHERE table_name = 'chat_notification_state';
-- ============================================================================
