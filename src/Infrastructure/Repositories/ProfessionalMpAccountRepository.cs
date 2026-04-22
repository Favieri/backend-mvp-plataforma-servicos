using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalMpAccountRepository(AppDbContext ctx) : IProfessionalMpAccountRepository
{
    public Task<ProfessionalMpAccount?> GetByProfessionalIdAsync(string professionalId, CancellationToken ct) =>
        ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfessionalId == professionalId, ct);

    public async Task UpsertAsync(ProfessionalMpAccount account, CancellationToken ct)
    {
        var existing = await ctx.ProfessionalMpAccounts
            .FirstOrDefaultAsync(x => x.ProfessionalId == account.ProfessionalId, ct);

        if (existing is null)
        {
            ctx.ProfessionalMpAccounts.Add(account);
        }
        else
        {
            await ctx.ProfessionalMpAccounts
                .Where(x => x.ProfessionalId == account.ProfessionalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.MpUserId, account.MpUserId)
                    .SetProperty(x => x.MpAccessToken, account.MpAccessToken)
                    .SetProperty(x => x.MpRefreshToken, account.MpRefreshToken)
                    .SetProperty(x => x.MpTokenExpiresAt, account.MpTokenExpiresAt)
                    .SetProperty(x => x.MpScope, account.MpScope)
                    .SetProperty(x => x.MpLiveMode, account.MpLiveMode)
                    .SetProperty(x => x.Status, account.Status)
                    .SetProperty(x => x.ConnectedAt, account.ConnectedAt)
                    .SetProperty(x => x.UpdatedAt, account.UpdatedAt),
                ct);
            return;
        }

        await ctx.SaveChangesAsync(ct);
    }

    public Task UpdateTokensAsync(string professionalId, string accessToken, string refreshToken, DateTime expiresAt, CancellationToken ct) =>
        ctx.ProfessionalMpAccounts
            .Where(x => x.ProfessionalId == professionalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MpAccessToken, accessToken)
                .SetProperty(x => x.MpRefreshToken, refreshToken)
                .SetProperty(x => x.MpTokenExpiresAt, expiresAt)
                .SetProperty(x => x.LastRefreshedAt, DateTime.UtcNow)
                .SetProperty(x => x.Status, "active")
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow),
            ct);

    public Task UpdateStatusAsync(string professionalId, string status, CancellationToken ct) =>
        ctx.ProfessionalMpAccounts
            .Where(x => x.ProfessionalId == professionalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow),
            ct);

    public async Task<IReadOnlyList<ProfessionalMpAccount>> GetExpiringTokensAsync(int withinDays, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow.AddDays(withinDays);
        return await ctx.ProfessionalMpAccounts
            .AsNoTracking()
            .Where(x => x.Status == "active" && x.MpTokenExpiresAt < threshold)
            .ToListAsync(ct);
    }
}
