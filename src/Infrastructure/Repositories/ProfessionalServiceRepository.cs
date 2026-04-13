using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ProfessionalServiceRepository(AppDbContext ctx) : IProfessionalServiceRepository
{
    private async Task<object?> ProjectByIdAsync(string id, CancellationToken ct)
        => await (
            from ps in ctx.ProfessionalServices.AsNoTracking()
            join s in ctx.Services.AsNoTracking() on ps.ServiceId equals s.Id
            where ps.Id == id
            select new
            {
                id = ps.Id,
                serviceId = ps.ServiceId,
                professionalId = ps.ProfessionalId,
                nomeServico = ps.NomeServico,
                preco = ps.Preco,
                descricao = ps.Descricao,
                tierId = ps.TierId,
                contractMode = ps.ContractMode,
                durationMinutes = ps.DurationMinutes,
                minLeadTimeMinutes = ps.MinLeadTimeMinutes,
                tipoContratacao = ps.TipoContratacao,
                tipoPrecificacao = ps.TipoContratacao == Domain.Enums.TipoContratacao.Proposta
                    ? "SOB_CONSULTA"
                    : (ps.Preco.HasValue ? "FIXO" : null),
                service = new { id = s.Id, name = s.Name, icon = s.Icon }
            }
        ).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<object>> GetAsync(
        string? professionalId, string? serviceId, CancellationToken ct)
    {
        var query =
            from ps in ctx.ProfessionalServices.AsNoTracking()
            join s in ctx.Services.AsNoTracking() on ps.ServiceId equals s.Id
            select new { ps, s };

        if (!string.IsNullOrWhiteSpace(professionalId))
            query = query.Where(x => x.ps.ProfessionalId == professionalId);

        if (!string.IsNullOrWhiteSpace(serviceId))
            query = query.Where(x => x.ps.ServiceId == serviceId);

        var rows = await query
            .OrderBy(x => x.ps.NomeServico)
            .Select(x => new
            {
                id = x.ps.Id,
                serviceId = x.ps.ServiceId,
                professionalId = x.ps.ProfessionalId,
                nomeServico = x.ps.NomeServico,
                preco = x.ps.Preco,
                descricao = x.ps.Descricao,
                tierId = x.ps.TierId,
                contractMode = x.ps.ContractMode,
                durationMinutes = x.ps.DurationMinutes,
                minLeadTimeMinutes = x.ps.MinLeadTimeMinutes,
                tipoContratacao = x.ps.TipoContratacao,
                tipoPrecificacao = x.ps.TipoContratacao == Domain.Enums.TipoContratacao.Proposta
                    ? "SOB_CONSULTA"
                    : (x.ps.Preco.HasValue ? "FIXO" : null),
                service = new { id = x.s.Id, name = x.s.Name, icon = x.s.Icon }
            })
            .ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
        => await ProjectByIdAsync(id, ct);

    public async Task<object> CreateAsync(
        string professionalId, string serviceId, string nomeServico,
        decimal? preco, string? descricao,
        int? tierId, string? contractMode, int? durationMinutes, int? minLeadTimeMinutes,
        string? tipoContratacao,
        CancellationToken ct)
    {
        var entity = new ProfessionalService(
            Id: Guid.NewGuid().ToString(),
            ProfessionalId: professionalId,
            ServiceId: serviceId,
            NomeServico: nomeServico,
            Preco: preco.HasValue ? (double)preco.Value : (double?)null,
            Descricao: descricao,
            TierId: tierId,
            ContractMode: contractMode,
            DurationMinutes: durationMinutes,
            MinLeadTimeMinutes: minLeadTimeMinutes,
            TipoContratacao: tipoContratacao);

        ctx.ProfessionalServices.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return (await ProjectByIdAsync(entity.Id, ct))!;
    }

    public async Task<object?> UpdateAsync(
        string id, string? nomeServico, decimal? preco, string? descricao, CancellationToken ct)
    {
        var existing = await ctx.ProfessionalServices
            .AsNoTracking()
            .FirstOrDefaultAsync(ps => ps.Id == id, ct);

        if (existing is null) return null;

        var newNome = nomeServico ?? existing.NomeServico;
        var newPreco = preco.HasValue ? (double)preco.Value : existing.Preco;
        var newDesc = descricao ?? existing.Descricao;

        await ctx.ProfessionalServices
            .Where(ps => ps.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ps => ps.NomeServico, newNome)
                .SetProperty(ps => ps.Preco, newPreco)
                .SetProperty(ps => ps.Descricao, newDesc), ct);

        return await ProjectByIdAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var deleted = await ctx.ProfessionalServices
            .Where(ps => ps.Id == id)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task<bool> ProfessionalExistsAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals.AsNoTracking().AnyAsync(p => p.Id == professionalId, ct);

    public async Task<bool> ServiceExistsAsync(string serviceId, CancellationToken ct)
        => await ctx.Services.AsNoTracking().AnyAsync(s => s.Id == serviceId, ct);
}
