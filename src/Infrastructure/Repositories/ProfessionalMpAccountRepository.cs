using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories;

public sealed class ProfessionalMpAccountRepository(AppDbContext ctx, ILogger<ProfessionalMpAccountRepository> logger)
    : IProfessionalMpAccountRepository
{
    public async Task<ProfessionalMpAccountDto?> GetByProfessionalIdAsync(string professionalId, CancellationToken ct)
    {
        var entity = await ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfessionalId == professionalId, ct);

        return entity is null ? null : ToDto(entity);
    }

    public async Task UpsertAsync(
        string professionalId,
        long mpUserId,
        string accessToken,
        string refreshToken,
        DateTime expiresAt,
        bool liveMode,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var strategy = ctx.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            var existing = await ctx.ProfessionalMpAccounts
                .FirstOrDefaultAsync(x => x.ProfessionalId == professionalId, ct);

            if (existing is not null)
            {
                var updated = existing with
                {
                    MpUserId = mpUserId,
                    MpAccessToken = accessToken,
                    MpRefreshToken = refreshToken,
                    MpTokenExpiresAt = expiresAt,
                    LiveMode = liveMode,
                    Status = "active",
                    UpdatedAt = now,
                };
                ctx.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                ctx.ProfessionalMpAccounts.Add(new ProfessionalMpAccount(
                    Id: Guid.NewGuid().ToString(),
                    ProfessionalId: professionalId,
                    MpUserId: mpUserId,
                    MpAccessToken: accessToken,
                    MpRefreshToken: refreshToken,
                    MpTokenExpiresAt: expiresAt,
                    Status: "active",
                    LiveMode: liveMode,
                    CreatedAt: now,
                    UpdatedAt: now));
            }

            await ctx.Professionals
                .Where(p => p.Id == professionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MpConnected, true)
                    .SetProperty(p => p.MpConnectedAt, now), ct);

            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<bool> RevokeAsync(string professionalId, CancellationToken ct)
    {
        var strategy = ctx.Database.CreateExecutionStrategy();
        var affected = 0;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            affected = await ctx.ProfessionalMpAccounts
                .Where(x => x.ProfessionalId == professionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "revoked")
                    .SetProperty(x => x.MpAccessToken, string.Empty)
                    .SetProperty(x => x.MpRefreshToken, string.Empty)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (affected > 0)
            {
                await ctx.Professionals
                    .Where(p => p.Id == professionalId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.MpConnected, false), ct);
            }

            await tx.CommitAsync(ct);
        });

        return affected > 0;
    }

    public async Task<IReadOnlyList<ProfessionalMpAccountDto>> GetExpiringSoonAsync(
        DateTime expiresBeforeUtc, CancellationToken ct)
    {
        var entities = await ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .Where(x => x.Status == "active" && x.MpTokenExpiresAt < expiresBeforeUtc)
            .ToListAsync(ct);

        return entities.Select(ToDto).ToList();
    }

    public async Task RefreshAndUpdateAsync(string professionalId, IMpOAuthService mpService, CancellationToken ct)
    {
        var entity = await ctx.ProfessionalMpAccounts
            .FirstOrDefaultAsync(x => x.ProfessionalId == professionalId, ct);

        if (entity is null)
        {
            logger.LogWarning("[MpRepo] RefreshAndUpdateAsync: no account found for professional {ProfessionalId}", professionalId);
            return;
        }

        // MpOAuthException(401) propagates to caller (MpTokenRefreshJob) which calls MarkExpiredAsync
        var tokenResponse = await mpService.RefreshTokenAsync(entity.MpRefreshToken, ct);

        var now = DateTime.UtcNow;
        var newExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn);

        await ctx.ProfessionalMpAccounts
            .Where(x => x.ProfessionalId == professionalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MpAccessToken, tokenResponse.AccessToken)
                .SetProperty(x => x.MpRefreshToken, tokenResponse.RefreshToken)
                .SetProperty(x => x.MpTokenExpiresAt, newExpiresAt)
                .SetProperty(x => x.UpdatedAt, now), ct);

        logger.LogInformation(
            "[MpRepo] Refreshed token for professional {ProfessionalId}. NewExpiresAt={ExpiresAt}",
            professionalId, newExpiresAt);
    }

    public async Task TryRevokeAndDisconnectAsync(string professionalId, IMpOAuthService mpService, CancellationToken ct)
    {
        var entity = await ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfessionalId == professionalId, ct);

        if (entity is not null && !string.IsNullOrEmpty(entity.MpAccessToken))
        {
            await mpService.TryRevokeTokenAsync(entity.MpAccessToken, ct);
        }

        await RevokeAsync(professionalId, ct);
    }

    public async Task MarkExpiredAsync(string professionalId, CancellationToken ct)
    {
        var strategy = ctx.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            await ctx.ProfessionalMpAccounts
                .Where(x => x.ProfessionalId == professionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "expired")
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            await ctx.Professionals
                .Where(p => p.Id == professionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.MpConnected, false), ct);

            await tx.CommitAsync(ct);
        });

        logger.LogWarning("[MpRepo] Marked account as expired for professional {ProfessionalId}", professionalId);
    }

    private static ProfessionalMpAccountDto ToDto(ProfessionalMpAccount e) => new()
    {
        Id = e.Id,
        ProfessionalId = e.ProfessionalId,
        MpUserId = e.MpUserId,
        Status = e.Status,
        LiveMode = e.LiveMode,
        MpTokenExpiresAt = e.MpTokenExpiresAt,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
