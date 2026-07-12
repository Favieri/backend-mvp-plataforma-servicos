namespace Domain.Entities;

public class ChatNotificationState
{
    public string ConversationId { get; private set; } = default!;
    public string RecipientUserId { get; private set; } = default!;
    public string LastNotifiedMessageId { get; private set; } = default!;
    public DateTime NotifiedAt { get; private set; }

    private ChatNotificationState() { }

    public static ChatNotificationState Create(string conversationId, string recipientUserId, string messageId)
    {
        return new ChatNotificationState
        {
            ConversationId = conversationId,
            RecipientUserId = recipientUserId,
            LastNotifiedMessageId = messageId,
            NotifiedAt = DateTime.UtcNow
        };
    }

    public void MarkNotified(string messageId)
    {
        LastNotifiedMessageId = messageId;
        NotifiedAt = DateTime.UtcNow;
    }
}
