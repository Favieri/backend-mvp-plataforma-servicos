namespace Application.Abstractions;

public interface IOrderIgnoreRepository
{
    Task UpsertAsync(string professionalId, string orderId, CancellationToken ct);
    Task DeleteAsync(string professionalId, string orderId, CancellationToken ct);
    Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct);
    Task<bool> OrderExistsAsync(string orderId, CancellationToken ct);
}
