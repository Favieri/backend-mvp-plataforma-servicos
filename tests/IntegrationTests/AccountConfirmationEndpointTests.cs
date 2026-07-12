using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Application.Services;
using Domain.Entities;
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
/// HTTP-level coverage for the account-confirmation / password-recovery PRD:
/// /auth/forgot-password, /auth/reset-password, /auth/verify-email, /auth/resend-verification,
/// plus the verification e-mail triggered by POST /users.
/// </summary>
// Cada teste bate no rate limiter compartilhado da política "auth" (10 req/min) — usar uma
// ApiFactory nova por teste (em vez de IClassFixture compartilhada) para que o estado do
// limiter não vaze entre métodos de teste desta classe.
public sealed class AccountConfirmationEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ForgotPassword_WithValidEmail_CreatesTokenAndSendsEmail()
    {
        await _factory.ResetDatabaseAsync();
        var userId = await _factory.SeedUserAsync("u1", "u1@test.com", "SenhaAtual1");
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new { Email = "u1@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("u1@test.com", _factory.Email.Sent.Where(s => s.Kind == "reset").Select(s => s.To));
        Assert.Equal(1, await _factory.CountPendingTokensAsync(userId, AccountTokenService.PasswordResetType));
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_ReturnsGenericSuccessWithoutCreatingToken()
    {
        await _factory.ResetDatabaseAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new { Email = "nao-existe@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.Email.Sent);
    }

    [Fact]
    public async Task ForgotPassword_ForSocialAccount_SendsReminderNotResetLink()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u2", "u2@test.com", plainPassword: null, provider: "google");
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new { Email = "u2@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("u2@test.com", _factory.Email.Sent.Where(s => s.Kind == "social-reminder").Select(s => s.To));
        Assert.DoesNotContain("u2@test.com", _factory.Email.Sent.Where(s => s.Kind == "reset").Select(s => s.To));
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_UpdatesPasswordAndInvalidatesToken()
    {
        await _factory.ResetDatabaseAsync();
        var userId = await _factory.SeedUserAsync("u3", "u3@test.com", "SenhaAntiga1");
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/auth/forgot-password", new { Email = "u3@test.com" });
        var token = _factory.Email.ExtractToken(_factory.Email.LastResetUrl["u3@test.com"]);

        var resetResponse = await client.PostAsJsonAsync("/auth/reset-password", new { Token = token, NewPassword = "SenhaNova123" });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var loginOld = await client.PostAsJsonAsync("/auth", new { Email = "u3@test.com", Senha = "SenhaAntiga1" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);

        var loginNew = await client.PostAsJsonAsync("/auth", new { Email = "u3@test.com", Senha = "SenhaNova123" });
        Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);

        Assert.Equal(0, await _factory.CountPendingTokensAsync(userId, AccountTokenService.PasswordResetType));
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_Returns422()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u4", "u4@test.com", "SenhaAtual1");
        using var client = _factory.CreateClient();

        var plainToken = AccountTokenService.GenerateToken();
        await _factory.InsertRawTokenAsync("u4", AccountTokenService.PasswordResetType, plainToken, DateTime.UtcNow.AddMinutes(-1));

        var response = await client.PostAsJsonAsync("/auth/reset-password", new { Token = plainToken, NewPassword = "SenhaNova123" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithAlreadyUsedToken_Returns422()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u5", "u5@test.com", "SenhaAtual1");
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/auth/forgot-password", new { Email = "u5@test.com" });
        var token = _factory.Email.ExtractToken(_factory.Email.LastResetUrl["u5@test.com"]);

        var first = await client.PostAsJsonAsync("/auth/reset-password", new { Token = token, NewPassword = "SenhaNova123" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/auth/reset-password", new { Token = token, NewPassword = "OutraSenha123" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidatesOtherPendingTokensForSameUser()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u6", "u6@test.com", "SenhaAtual1");
        using var client = _factory.CreateClient();

        var tokenA = AccountTokenService.GenerateToken();
        var tokenB = AccountTokenService.GenerateToken();
        await _factory.InsertRawTokenAsync("u6", AccountTokenService.PasswordResetType, tokenA, DateTime.UtcNow.AddMinutes(30));
        await _factory.InsertRawTokenAsync("u6", AccountTokenService.PasswordResetType, tokenB, DateTime.UtcNow.AddMinutes(30));

        var resetWithA = await client.PostAsJsonAsync("/auth/reset-password", new { Token = tokenA, NewPassword = "SenhaNova123" });
        Assert.Equal(HttpStatusCode.OK, resetWithA.StatusCode);

        var resetWithB = await client.PostAsJsonAsync("/auth/reset-password", new { Token = tokenB, NewPassword = "OutraSenha123" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resetWithB.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithPasswordShorterThanMinimum_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u7", "u7@test.com", "SenhaAtual1");
        using var client = _factory.CreateClient();

        var plainToken = AccountTokenService.GenerateToken();
        await _factory.InsertRawTokenAsync("u7", AccountTokenService.PasswordResetType, plainToken, DateTime.UtcNow.AddMinutes(30));

        var response = await client.PostAsJsonAsync("/auth/reset-password", new { Token = plainToken, NewPassword = "1234567" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_WithValidToken_SetsEmailVerifiedTrue()
    {
        await _factory.ResetDatabaseAsync();
        var userId = await _factory.SeedUserAsync("u8", "u8@test.com", "SenhaAtual1", emailVerified: false);
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/auth/resend-verification", new { Email = "u8@test.com" });
        var token = _factory.Email.ExtractToken(_factory.Email.LastVerificationUrl["u8@test.com"]);

        var response = await client.GetAsync($"/auth/verify-email?token={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await _factory.IsEmailVerifiedAsync(userId));
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredToken_Returns422()
    {
        await _factory.ResetDatabaseAsync();
        await _factory.SeedUserAsync("u9", "u9@test.com", "SenhaAtual1", emailVerified: false);
        using var client = _factory.CreateClient();

        var plainToken = AccountTokenService.GenerateToken();
        await _factory.InsertRawTokenAsync("u9", AccountTokenService.EmailVerificationType, plainToken, DateTime.UtcNow.AddMinutes(-1));

        var response = await client.GetAsync($"/auth/verify-email?token={plainToken}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_TriggersEmailVerificationSend_ButSucceedsEvenIfEmailFails()
    {
        await _factory.ResetDatabaseAsync();
        _factory.Email.ThrowOnVerification = true;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/users", new
        {
            Name = "Novo Usuário",
            Email = "novo@test.com",
            Phone = (string?)null,
            Role = "profissional",
            Senha = "SenhaValida1",
            ZoneId = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        public FakeEmailService Email { get; } = new();

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
                services.AddSingleton<IEmailService>(Email);
            });
        }

        public async Task ResetDatabaseAsync()
        {
            Email.Reset();

            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            // "senha"/"email_verified" não fazem parte do modelo EF (ver UserConfiguration —
            // mesmo tratamento de AuthRepositoryTests), account_token não tem DbSet nenhum.
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN senha TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN email_verified INTEGER NOT NULL DEFAULT 0");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_zip_code TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_street TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_number TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_neighborhood TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_city TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_state TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_complement TEXT");
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE \"User\" ADD COLUMN addr_reference TEXT");
            await ctx.Database.ExecuteSqlRawAsync("""
                CREATE TABLE account_token (
                    id text PRIMARY KEY,
                    user_id text NOT NULL,
                    type text NOT NULL,
                    token_hash text NOT NULL,
                    expires_at TEXT NOT NULL,
                    used_at TEXT NULL,
                    created_at TEXT NOT NULL
                )
                """);
        }

        public async Task<string> SeedUserAsync(string id, string email, string? plainPassword, string? provider = null, bool emailVerified = false)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            ctx.Users.Add(new User(id, "User " + id, email, null, "cliente", null, DateTime.UtcNow, provider));
            await ctx.SaveChangesAsync();

            var hash = plainPassword is null ? null : BCrypt.Net.BCrypt.HashPassword(plainPassword);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "User" SET senha = {hash} WHERE id = {id}""");
            if (emailVerified)
                await ctx.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "User" SET email_verified = 1 WHERE id = {id}""");

            return id;
        }

        public async Task InsertRawTokenAsync(string userId, string type, string plainToken, DateTime expiresAt)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenRepo = new Infrastructure.Repositories.AccountTokenRepository(ctx);
            await tokenRepo.CreateAsync(userId, type, AccountTokenService.HashToken(plainToken), expiresAt, CancellationToken.None);
        }

        public async Task<int> CountPendingTokensAsync(string userId, string type)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await ctx.Database
                .SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM account_token WHERE user_id = {userId} AND type = {type} AND used_at IS NULL""")
                .FirstAsync();
        }

        public async Task<bool> IsEmailVerifiedAsync(string userId)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await ctx.Database
                .SqlQuery<bool>($"""SELECT email_verified AS "Value" FROM "User" WHERE id = {userId}""")
                .FirstAsync();
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
        public List<(string To, string Kind)> Sent { get; } = new();
        public Dictionary<string, string> LastResetUrl { get; } = new();
        public Dictionary<string, string> LastVerificationUrl { get; } = new();
        public bool ThrowOnVerification { get; set; }

        public void Reset()
        {
            Sent.Clear();
            LastResetUrl.Clear();
            LastVerificationUrl.Clear();
            ThrowOnVerification = false;
        }

        public string ExtractToken(string url) => url.Split("token=")[1];

        public Task SendAsync(string to, string subject, string html, string? text = null, string? dedupeKey = null, CancellationToken ct = default)
        {
            Sent.Add((to, "generic"));
            return Task.CompletedTask;
        }

        public Task SendNewLeadAsync(string to, string professionalName, string clientName, string serviceName, string leadUrl, string? city = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendChatMessageAsync(string to, string recipientName, string senderName, string messageSnippet, string chatUrl, string conversationId, int windowMinutes = 10, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBookingConfirmedProfessionalAsync(string to, string professionalName, string clientName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBookingConfirmedClientAsync(string to, string clientName, string professionalName, string serviceName, string when, string bookingUrl, string? address = null, string? dedupeKey = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendEmailVerificationAsync(string to, string name, string verificationUrl, CancellationToken ct = default)
        {
            if (ThrowOnVerification)
                throw new InvalidOperationException("Simulated e-mail delivery failure");

            Sent.Add((to, "verification"));
            LastVerificationUrl[to] = verificationUrl;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string to, string name, string resetUrl, CancellationToken ct = default)
        {
            Sent.Add((to, "reset"));
            LastResetUrl[to] = resetUrl;
            return Task.CompletedTask;
        }

        public Task SendSocialAccountReminderAsync(string to, string name, string provider, CancellationToken ct = default)
        {
            Sent.Add((to, "social-reminder"));
            return Task.CompletedTask;
        }
    }
}
