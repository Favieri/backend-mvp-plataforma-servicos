using Application.DTOs;

namespace Application.Abstractions;

public interface IProfessionalVerificationRepository
{
    /// <summary>Retorna o registro de verificação mais recente do profissional.</summary>
    Task<ProfessionalVerificationDto?> GetLatestByProfessionalIdAsync(string professionalId, CancellationToken ct);

    /// <summary>Lista todos os registros de verificação do profissional (histórico).</summary>
    Task<IReadOnlyList<ProfessionalVerificationDto>> GetHistoryByProfessionalIdAsync(string professionalId, CancellationToken ct);

    /// <summary>Lista documentos pendentes de revisão (submitted ou in_review), para o painel admin.</summary>
    Task<IReadOnlyList<ProfessionalVerificationDto>> GetPendingReviewAsync(CancellationToken ct);

    /// <summary>Registra envio de documento pelo profissional e atualiza verificationStatus para 'submitted'.</summary>
    Task<ProfessionalVerificationDto> SubmitAsync(
        string professionalId,
        string documentType,
        string documentUrl,
        CancellationToken ct);

    /// <summary>Atualiza o status de um documento (in_review | verified | rejected) e reflete no Professional.</summary>
    Task<ProfessionalVerificationDto?> ReviewAsync(
        string verificationId,
        string status,
        string? notes,
        string reviewedBy,
        CancellationToken ct);
}
