using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// PRD Notificação de chat por e-mail (job periódico com janela de silêncio):
///   chat_notification_state registra, por conversa e destinatário, a última mensagem
///   para a qual já foi enviado um e-mail de silêncio — evita reenvio enquanto a mesma
///   mensagem continuar não lida.
///
/// IDEMPOTENTE: todas as DDL usam IF NOT EXISTS.
/// </summary>
public partial class AddChatNotificationState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS chat_notification_state (
    conversation_id           text NOT NULL,
    recipient_user_id         text NOT NULL,
    last_notified_message_id  text NOT NULL,
    notified_at               timestamp NOT NULL DEFAULT now(),
    PRIMARY KEY (conversation_id, recipient_user_id)
);");

        migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_chat_notification_state_conversation ON chat_notification_state (conversation_id);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS chat_notification_state;");
    }
}
