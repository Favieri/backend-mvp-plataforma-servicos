namespace Domain.Entities;

public sealed record ProfessionalService(
    string Id,
    string ProfessionalId,
    string ServiceId,
    string NomeServico,
    double Preco,
    string? Descricao);
