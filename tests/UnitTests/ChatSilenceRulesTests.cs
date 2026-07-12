using Application.Services;
using Xunit;

namespace UnitTests;

public sealed class ChatSilenceRulesTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    private static ChatSilenceCandidate Candidate(
        DateTime messageSentAt,
        DateTime? recipientLastReadAt = null,
        string? lastNotifiedMessageId = null,
        string messageId = "msg-1") =>
        new(
            ConversationId: "conv-1",
            RecipientUserId: "user-1",
            RecipientEmail: "user@test.com",
            RecipientName: "User",
            SenderName: "Sender",
            MessageId: messageId,
            MessageText: "Olá",
            MessageSentAt: messageSentAt,
            RecipientLastReadAt: recipientLastReadAt,
            LastNotifiedMessageId: lastNotifiedMessageId);

    [Fact]
    public void IsEligible_WhenUnreadMessageOlderThanThreshold_ReturnsTrue()
    {
        var candidate = Candidate(messageSentAt: Now - TimeSpan.FromHours(3));
        Assert.True(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenMessageYoungerThanThreshold_ReturnsFalse()
    {
        var candidate = Candidate(messageSentAt: Now - TimeSpan.FromMinutes(30));
        Assert.False(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenAlreadyNotifiedForThisMessage_ReturnsFalse()
    {
        var candidate = Candidate(
            messageSentAt: Now - TimeSpan.FromHours(3),
            lastNotifiedMessageId: "msg-1");

        Assert.False(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenNewerMessageStillSilentAfterPreviousNotification_ReturnsTrue()
    {
        var candidate = Candidate(
            messageSentAt: Now - TimeSpan.FromHours(3),
            lastNotifiedMessageId: "msg-0",
            messageId: "msg-1");

        Assert.True(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenRecipientReadAfterMessageWasSent_ReturnsFalse()
    {
        var messageSentAt = Now - TimeSpan.FromHours(3);
        var candidate = Candidate(messageSentAt, recipientLastReadAt: messageSentAt + TimeSpan.FromMinutes(5));

        Assert.False(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenRecipientRecentlyActive_ReturnsFalse()
    {
        var candidate = Candidate(
            messageSentAt: Now - TimeSpan.FromHours(3),
            recipientLastReadAt: Now - TimeSpan.FromMinutes(1));

        Assert.False(ChatSilenceRules.IsEligible(candidate, Now));
    }

    [Fact]
    public void IsEligible_WhenRecipientWasActiveButNotRecently_ReturnsTrue()
    {
        var candidate = Candidate(
            messageSentAt: Now - TimeSpan.FromHours(3),
            recipientLastReadAt: Now - TimeSpan.FromHours(4));

        Assert.True(ChatSilenceRules.IsEligible(candidate, Now));
    }
}
