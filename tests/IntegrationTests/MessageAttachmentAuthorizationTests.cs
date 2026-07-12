using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Covers PRD-Contrato-Dados-Confianca item 1.6: POST /messages/attachment must resolve the
/// sender from the authenticated JWT (not the form body) and must reject requests from a user
/// who isn't a participant of the conversation.
/// </summary>
public sealed class MessageAttachmentAuthorizationTests : IClassFixture<MessageAttachmentAuthorizationTests.ApiFactory>
{
    private const string ClientId = "client-1";
    private const string ProfessionalUserId = "pro-user-1";
    private const string OutsiderId = "outsider-1";
    private const string ConversationId = "conv-1";

    private readonly ApiFactory _factory;

    public MessageAttachmentAuthorizationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Attachment_SenderId_Is_Resolved_From_Jwt_Not_FormBody()
    {
        await _factory.SeedConversationAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken(ClientId, "cliente"));

        using var form = BuildForm(spoofedSenderId: ProfessionalUserId);
        var response = await client.PostAsync("/messages/attachment", form);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ClientId, body.GetProperty("message").GetProperty("senderId").GetString());
    }

    [Fact]
    public async Task Attachment_Rejects_Sender_Who_Is_Not_A_Conversation_Participant()
    {
        await _factory.SeedConversationAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateToken(OutsiderId, "cliente"));

        using var form = BuildForm(spoofedSenderId: ClientId);
        var response = await client.PostAsync("/messages/attachment", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Attachment_Rejects_Unauthenticated_Request()
    {
        await _factory.SeedConversationAsync();
        using var client = _factory.CreateClient();

        using var form = BuildForm(spoofedSenderId: ClientId);
        var response = await client.PostAsync("/messages/attachment", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static MultipartFormDataContent BuildForm(string spoofedSenderId)
    {
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake-image-bytes"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        return new MultipartFormDataContent
        {
            { fileContent, "file", "photo.png" },
            { new StringContent(ConversationId), "conversationId" },
            { new StringContent(spoofedSenderId), "senderId" },
            { new StringContent("file"), "attachmentType" }
        };
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

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

                services.RemoveAll<IAttachmentStorageRepository>();
                services.AddSingleton<IAttachmentStorageRepository, FakeAttachmentStorageRepository>();
            });
        }

        public async Task SeedConversationAsync()
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            ctx.Users.Add(new User(ClientId, "Client One", "client@test.com", null, "CLIENTE", null, DateTime.UtcNow));
            ctx.Users.Add(new User(ProfessionalUserId, "Pro One", "pro@test.com", null, "PROFISSIONAL", null, DateTime.UtcNow));
            ctx.Users.Add(new User(OutsiderId, "Outsider", "outsider@test.com", null, "CLIENTE", null, DateTime.UtcNow));
            ctx.Conversations.Add(new Conversation(
                Id: ConversationId,
                OrderId: null,
                ClientId: ClientId,
                ProfessionalId: ProfessionalUserId,
                CreatedAt: DateTime.UtcNow,
                ClientLastReadAt: null,
                ProfessionalLastReadAt: null));

            await ctx.SaveChangesAsync();
        }

        public string CreateToken(string userId, string role)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test.com"),
                new Claim("role", role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };
            var token = new JwtSecurityToken(
                issuer: "jobeasy",
                audience: "jobeasy",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
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

    private sealed class FakeAttachmentStorageRepository : IAttachmentStorageRepository
    {
        public Task<string?> UploadAsync(
            string messageId, Stream fileStream, string contentType, string originalFileName, CancellationToken ct)
            => Task.FromResult<string?>($"https://fake-storage.test/{messageId}/{originalFileName}");
    }
}
