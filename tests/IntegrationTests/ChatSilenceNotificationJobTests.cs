using Application.Abstractions;
using Domain.Entities;
using Infrastructure.BackgroundJobs;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Covers PRD "Notificação de chat por e-mail: job periódico com janela de silêncio (2h)":
/// ChatSilenceNotificationJob só notifica após 2h reais de silêncio, não duplica notificação
/// para a mesma mensagem, notifica de novo para uma mensagem mais recente, e respeita a
/// checagem de "recentemente ativo".
/// </summary>
public sealed class ChatSilenceNotificationJobTests : IClassFixture<ChatSilenceNotificationJobTests.ApiFactory>
{
    private const string ClientId = "client-1";
    private const string ProfessionalUserId = "pro-user-1";
    private const string ConversationId = "conv-1";

    private readonly ApiFactory _factory;

    public ChatSilenceNotificationJobTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Notifies_Conversation_With_Message_Unread_For_More_Than_2h()
    {
        await _factory.SeedAsync(messageAge: TimeSpan.FromHours(3), messageId: "msg-1");
        _factory.FakeEmail.Reset();

        await _factory.RunJobOnceAsync();

        var sent = Assert.Single(_factory.FakeEmail.Sent);
        Assert.Equal(ProfessionalUserId + "@test.com", sent.To);
    }

    [Fact]
    public async Task Does_Not_Notify_Message_Unread_For_Less_Than_2h()
    {
        await _factory.SeedAsync(messageAge: TimeSpan.FromMinutes(30), messageId: "msg-1");
        _factory.FakeEmail.Reset();

        await _factory.RunJobOnceAsync();

        Assert.Empty(_factory.FakeEmail.Sent);
    }

    [Fact]
    public async Task Does_Not_Notify_Same_Message_Twice_Across_Consecutive_Runs()
    {
        await _factory.SeedAsync(messageAge: TimeSpan.FromHours(3), messageId: "msg-1");
        _factory.FakeEmail.Reset();

        await _factory.RunJobOnceAsync();
        await _factory.RunJobOnceAsync();

        Assert.Single(_factory.FakeEmail.Sent);
    }

    [Fact]
    public async Task Notifies_Again_When_A_Newer_Message_Also_Goes_Silent_For_2h()
    {
        await _factory.SeedAsync(messageAge: TimeSpan.FromHours(3), messageId: "msg-1");
        _factory.FakeEmail.Reset();
        await _factory.RunJobOnceAsync();
        Assert.Single(_factory.FakeEmail.Sent);

        // New message from the client arrives, also silent for more than 2h.
        await _factory.AddMessageAsync(messageAge: TimeSpan.FromHours(2.5), messageId: "msg-2");
        await _factory.RunJobOnceAsync();

        Assert.Equal(2, _factory.FakeEmail.Sent.Count);
    }

    [Fact]
    public async Task Does_Not_Notify_When_Recipient_Was_Recently_Active()
    {
        await _factory.SeedAsync(
            messageAge: TimeSpan.FromHours(3),
            messageId: "msg-1",
            recipientLastReadAgo: TimeSpan.FromMinutes(1));
        _factory.FakeEmail.Reset();

        await _factory.RunJobOnceAsync();

        Assert.Empty(_factory.FakeEmail.Sent);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public FakeEmailService FakeEmail { get; } = new();

        public ApiFactory()
        {
            IntegrationTestDefaults.ConfigureTestEnvironment();
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(FakeEmail);
            });
        }

        public async Task RunJobOnceAsync()
        {
            var job = Services.GetRequiredService<ChatSilenceNotificationJob>();
            await job.RunOnceAsync(CancellationToken.None);
        }

        public async Task SeedAsync(TimeSpan messageAge, string messageId, TimeSpan? recipientLastReadAgo = null)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            ctx.Users.Add(new User(ClientId, "Client One", "client-1@test.com", null, "CLIENTE", null, DateTime.UtcNow));
            ctx.Users.Add(new User(ProfessionalUserId, "Pro One", ProfessionalUserId + "@test.com", null, "PROFISSIONAL", null, DateTime.UtcNow));
            ctx.Conversations.Add(new Conversation(
                Id: ConversationId,
                OrderId: null,
                ClientId: ClientId,
                ProfessionalId: ProfessionalUserId,
                CreatedAt: DateTime.UtcNow,
                ClientLastReadAt: null,
                ProfessionalLastReadAt: recipientLastReadAgo is TimeSpan ago ? DateTime.UtcNow - ago : null));

            ctx.Messages.Add(new Message(
                Id: messageId,
                ConversationId: ConversationId,
                SenderId: ClientId,
                Text: "Olá, tudo bem?",
                SentAt: DateTime.UtcNow - messageAge));

            await ctx.SaveChangesAsync();
        }

        public async Task AddMessageAsync(TimeSpan messageAge, string messageId)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            ctx.Messages.Add(new Message(
                Id: messageId,
                ConversationId: ConversationId,
                SenderId: ClientId,
                Text: "Ainda por aí?",
                SentAt: DateTime.UtcNow - messageAge));

            await ctx.SaveChangesAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }
    }

    public sealed class FakeEmailService : IEmailService
    {
        public List<(string To, string SenderName, string MessageSnippet, string ConversationId)> Sent { get; } = [];

        public void Reset() => Sent.Clear();

        public Task SendAsync(string to, string subject, string html, string? text = null, string? dedupeKey = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendNewLeadAsync(string to, string professionalName, string clientName, string serviceName, string leadUrl, string? city = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendChatMessageAsync(string to, string recipientName, string senderName, string messageSnippet, string chatUrl, string conversationId, int windowMinutes = 10, CancellationToken ct = default)
        {
            Sent.Add((to, senderName, messageSnippet, conversationId));
            return Task.CompletedTask;
        }

        public Task SendBookingConfirmedProfessionalAsync(string to, string professionalName, string clientName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBookingConfirmedClientAsync(string to, string clientName, string professionalName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendEmailVerificationAsync(string to, string name, string verificationUrl, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPasswordResetAsync(string to, string name, string resetUrl, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSocialAccountReminderAsync(string to, string name, string provider, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
