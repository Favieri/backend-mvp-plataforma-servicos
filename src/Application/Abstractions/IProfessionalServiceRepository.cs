namespace Application.Abstractions;

public interface IProfessionalServiceRepository
{
    Task<IReadOnlyList<object>> GetAsync(string? professionalId, string? serviceId, CancellationToken ct);
    Task<object?> GetByIdAsync(string id, CancellationToken ct);
    Task<object> CreateAsync(string professionalId, string serviceId, string nomeServico, decimal? preco, string? descricao, int? tierId, string? contractMode, int? durationMinutes, int? minLeadTimeMinutes, string? tipoContratacao, CancellationToken ct);
    Task<object?> UpdateAsync(string id, string? nomeServico, decimal? preco, string? descricao, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
    Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct);
    Task<bool> ServiceExistsAsync(string serviceId, CancellationToken ct);
}
