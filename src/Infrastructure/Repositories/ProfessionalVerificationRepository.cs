using Application.Abstractions;
using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalVerificationRepository(AppDbContext ctx) : IProfessionalVerificationRepository
{
    public async Task<ProfessionalVerificationDto?> GetLatestByProfessionalIdAsync(
        string professionalId, CancellationToken ct)
    {
        return await ctx.ProfessionalVerifications
            .AsNoTracking()
            .Where(v => v.ProfessionalId == professionalId)
            .OrderByDescending(v => v.SubmittedAt)
            .Select(v => new ProfessionalVerificationDto
            {
                Id = v.Id,
                ProfessionalId = v.ProfessionalId,
                DocumentType = v.DocumentType,
                DocumentUrl = v.DocumentUrl,
                Status = v.Status,
                Notes = v.Notes,
                ReviewedBy = v.ReviewedBy,
                ReviewedAt = v.ReviewedAt,
                SubmittedAt = v.SubmittedAt,
                CreatedAt = v.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ProfessionalVerificationDto>> GetHistoryByProfessionalIdAsync(
        string professionalId, CancellationToken ct)
    {
        return await ctx.ProfessionalVerifications
            .AsNoTracking()
            .Where(v => v.ProfessionalId == professionalId)
            .OrderByDescending(v => v.SubmittedAt)
            .Select(v => new ProfessionalVerificationDto
            {
                Id = v.Id,
                ProfessionalId = v.ProfessionalId,
                DocumentType = v.DocumentType,
                DocumentUrl = v.DocumentUrl,
                Status = v.Status,
                Notes = v.Notes,
                ReviewedBy = v.ReviewedBy,
                ReviewedAt = v.ReviewedAt,
                SubmittedAt = v.SubmittedAt,
                CreatedAt = v.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProfessionalVerificationDto>> GetPendingReviewAsync(CancellationToken ct)
    {
        return await ctx.ProfessionalVerifications
            .AsNoTracking()
            .Where(v => v.Status == "submitted" || v.Status == "in_review")
            .OrderBy(v => v.SubmittedAt)
            .Select(v => new ProfessionalVerificationDto
            {
                Id = v.Id,
                ProfessionalId = v.ProfessionalId,
                DocumentType = v.DocumentType,
                DocumentUrl = v.DocumentUrl,
                Status = v.Status,
                Notes = v.Notes,
                ReviewedBy = v.ReviewedBy,
                ReviewedAt = v.ReviewedAt,
                SubmittedAt = v.SubmittedAt,
                CreatedAt = v.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<ProfessionalVerificationDto> SubmitAsync(
        string professionalId,
        string documentType,
        string documentUrl,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid().ToString();

        var strategy = ctx.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            var verification = new ProfessionalVerification(
                Id: id,
                ProfessionalId: professionalId,
                DocumentType: documentType,
                DocumentUrl: documentUrl,
                Status: "submitted",
                Notes: null,
                ReviewedBy: null,
                ReviewedAt: null,
                SubmittedAt: now,
                CreatedAt: now,
                UpdatedAt: now);

            ctx.ProfessionalVerifications.Add(verification);

            // Atualiza o verificationStatus do profissional para 'submitted'
            await ctx.Professionals
                .Where(p => p.Id == professionalId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.VerificationStatus, "submitted"),
                    ct);

            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new ProfessionalVerificationDto
        {
            Id = id,
            ProfessionalId = professionalId,
            DocumentType = documentType,
            DocumentUrl = documentUrl,
            Status = "submitted",
            SubmittedAt = now,
            CreatedAt = now
        };
    }

    public async Task<ProfessionalVerificationDto?> ReviewAsync(
        string verificationId,
        string status,
        string? notes,
        string reviewedBy,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var verification = await ctx.ProfessionalVerifications
            .FirstOrDefaultAsync(v => v.Id == verificationId, ct);

        if (verification is null)
            return null;

        var strategy = ctx.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            var updated = verification with
            {
                Status = status,
                Notes = notes,
                ReviewedBy = reviewedBy,
                ReviewedAt = now,
                UpdatedAt = now
            };

            ctx.ProfessionalVerifications.Entry(verification).CurrentValues.SetValues(updated);

            // Reflete o novo status no profissional (verified ou rejected)
            await ctx.Professionals
                .Where(p => p.Id == verification.ProfessionalId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.VerificationStatus, status),
                    ct);

            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new ProfessionalVerificationDto
        {
            Id = verification.Id,
            ProfessionalId = verification.ProfessionalId,
            DocumentType = verification.DocumentType,
            DocumentUrl = verification.DocumentUrl,
            Status = status,
            Notes = notes,
            ReviewedBy = reviewedBy,
            ReviewedAt = now,
            SubmittedAt = verification.SubmittedAt,
            CreatedAt = verification.CreatedAt
        };
    }
}
