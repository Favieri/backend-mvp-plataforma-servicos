namespace Domain.Entities;

public sealed record ProfessionalService(
    string Id,
    string ProfessionalId,
    string ServiceId,
    string NomeServico,
    double? Preco,
    string? Descricao,
    int? TierId = null,
    string? ContractMode = null,
    int? DurationMinutes = null,
    string? IncludesDescription = null,
    string? ExcludesDescription = null,
    bool? MaterialIncluded = null,
    int? VisitFeeCents = null,
    int? MinLeadTimeMinutes = null,
    string? TipoContratacao = null);
