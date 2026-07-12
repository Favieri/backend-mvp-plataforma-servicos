namespace Application.Abstractions;

public sealed record AccountTokenRow(string Id, string UserId);

public interface IAccountTokenRepository
{
    Task CreateAsync(string userId, string type, string tokenHash, DateTime expiresAt, CancellationToken ct);
    Task<AccountTokenRow?> FindValidAsync(string tokenHash, string type, CancellationToken ct);
    Task MarkUsedAsync(string tokenId, CancellationToken ct);
    Task InvalidatePendingAsync(string userId, string type, CancellationToken ct);
}
