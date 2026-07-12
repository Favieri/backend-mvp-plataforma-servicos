using Application.Abstractions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AccountTokenRepository(AppDbContext ctx) : IAccountTokenRepository
{
    public async Task CreateAsync(string userId, string type, string tokenHash, DateTime expiresAt, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO account_token(id, user_id, type, token_hash, expires_at, created_at)
            VALUES ({id}, {userId}, {type}, {tokenHash}, {expiresAt}, {DateTime.UtcNow})
            """, ct);
    }

    public async Task<AccountTokenRow?> FindValidAsync(string tokenHash, string type, CancellationToken ct)
    {
        var row = await ctx.Database
            .SqlQuery<AccountTokenIdRow>($"""
                SELECT id AS "Id", user_id AS "UserId"
                FROM account_token
                WHERE token_hash = {tokenHash} AND type = {type}
                  AND used_at IS NULL AND expires_at > {DateTime.UtcNow}
                LIMIT 1
            """)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new AccountTokenRow(row.Id, row.UserId);
    }

    public async Task MarkUsedAsync(string tokenId, CancellationToken ct)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE account_token SET used_at = {DateTime.UtcNow} WHERE id = {tokenId}""", ct);
    }

    public async Task InvalidatePendingAsync(string userId, string type, CancellationToken ct)
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE account_token
            SET used_at = {DateTime.UtcNow}
            WHERE user_id = {userId} AND type = {type} AND used_at IS NULL
            """, ct);
    }

    private sealed record AccountTokenIdRow
    {
        public string Id { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
    }
}
