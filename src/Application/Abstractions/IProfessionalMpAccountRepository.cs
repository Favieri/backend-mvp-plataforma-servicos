using Domain.Entities;

namespace Application.Abstractions;

public interface IProfessionalMpAccountRepository
{
    Task<ProfessionalMpAccount?> GetByProfessionalIdAsync(string professionalId, CancellationToken ct);
    Task UpsertAsync(ProfessionalMpAccount account, CancellationToken ct);
    Task UpdateTokensAsync(string professionalId, string accessToken, string refreshToken, DateTime expiresAt, CancellationToken ct);
    Task UpdateStatusAsync(string professionalId, string status, CancellationToken ct);
    Task<IReadOnlyList<ProfessionalMpAccount>> GetExpiringTokensAsync(int withinDays, CancellationToken ct);
}
