namespace Domain.Entities;

/// <summary>
/// Documento de verificação enviado por um profissional.
/// O registro mais recente com status 'verified' representa a verificação ativa.
/// </summary>
public sealed record ProfessionalVerification(
    string Id,
    string ProfessionalId,
    string DocumentType,        // rg, cnh, cpf, cnpj, diploma, crea, cau, crm, oab, other
    string DocumentUrl,         // URL no Supabase Storage
    string Status,              // submitted, in_review, verified, rejected
    string? Notes,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    DateTime SubmittedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
