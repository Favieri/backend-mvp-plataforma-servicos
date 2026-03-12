using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ReviewRepository(AppDbContext ctx) : IReviewRepository
{
    public async Task<object> GetByProfessionalAsync(string professionalId, int limit, CancellationToken ct)
    {
        var clampedLimit = Math.Max(1, Math.Min(limit, 50));

        var reviews = await (
            from r in ctx.Reviews.AsNoTracking()
            join u in ctx.Users.AsNoTracking() on r.ClientId equals u.Id into userJoin
            from u in userJoin.DefaultIfEmpty()
            where r.ProfessionalId == professionalId
            orderby r.CreatedAt descending
            select new
            {
                id = r.Id,
                rating = r.Rating,
                comment = r.Comment,
                createdAt = r.CreatedAt,
                clientName = u != null ? u.Name : "Cliente"
            }
        ).Take(clampedLimit).ToListAsync(ct);

        var agg = await ctx.Reviews
            .AsNoTracking()
            .Where(r => r.ProfessionalId == professionalId)
            .GroupBy(_ => 1)
            .Select(g => new { avg = g.Average(r => (double)r.Rating), cnt = g.Count() })
            .FirstOrDefaultAsync(ct);

        return new
        {
            reviews,
            average = agg?.avg ?? 0.0,
            count = (long)(agg?.cnt ?? 0)
        };
    }

    public async Task<object?> GetByIdAsync(string id, CancellationToken ct)
    {
        return await ctx.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                id = r.Id,
                orderId = r.OrderId,
                professionalId = r.ProfessionalId,
                clientId = r.ClientId,
                rating = r.Rating,
                comment = r.Comment,
                createdAt = r.CreatedAt
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<object> CreateAsync(
        string professionalId, string clientId, string orderId,
        int rating, string? comment, CancellationToken ct)
    {
        var review = new Review(
            Id: Guid.NewGuid().ToString(),
            OrderId: orderId,
            ProfessionalId: professionalId,
            ClientId: clientId,
            Rating: rating,
            Comment: comment,
            CreatedAt: DateTime.UtcNow);

        ctx.Reviews.Add(review);
        await ctx.SaveChangesAsync(ct);

        // Recalculate professional avg rating
        var (average, count) = await RecalculateAndUpdateRatingAsync(professionalId, ct);

        return new
        {
            ok = true,
            review = new { id = review.Id, rating = review.Rating, comment = review.Comment, createdAt = review.CreatedAt },
            average,
            count
        };
    }

    public async Task<object?> UpdateAsync(string id, int? rating, string? comment, CancellationToken ct)
    {
        var existing = await ctx.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ProfessionalId })
            .FirstOrDefaultAsync(ct);

        if (existing is null) return null;

        var updated = false;

        if (rating is not null)
        {
            await ctx.Reviews
                .Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Rating, rating.Value), ct);

            updated = true;
        }

        if (comment is not null)
        {
            await ctx.Reviews
                .Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Comment, comment), ct);

            updated = true;
        }

        if (updated)
        {
            await RecalculateAndUpdateRatingAsync(existing.ProfessionalId, ct);
        }

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> OrderAlreadyReviewedAsync(string orderId, CancellationToken ct)
        => await ctx.Reviews.AsNoTracking().AnyAsync(r => r.OrderId == orderId, ct);

    public async Task<bool> OrderBelongsToClientAsync(string orderId, string clientId, CancellationToken ct)
        => await ctx.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.ClientId == clientId, ct);

    public async Task<string?> GetProfessionalUserIdAsync(string professionalId, CancellationToken ct)
        => await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.Id == professionalId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<object>> GetEligibleOrdersAsync(
        string clientId, string professionalId, CancellationToken ct)
    {
        const int ReviewWindowDays = 14;
        var since = DateTime.UtcNow.AddDays(-ReviewWindowDays);

        var rows = await (
            from o in ctx.Orders.AsNoTracking()
            join s in ctx.Services.AsNoTracking() on o.ServiceId equals s.Id into svcJoin
            from s in svcJoin.DefaultIfEmpty()
            where o.ClientId == clientId
                && (o.Status == "concluido" || o.Status == "auto_concluido"
                    || o.Status == Domain.Enums.OrderStatus.Completed
                    || o.Status == Domain.Enums.OrderStatus.Evaluated)
                && o.CreatedAt >= since
                && !ctx.Reviews.Any(r => r.OrderId == o.Id)
            orderby o.CreatedAt descending
            select new
            {
                id = o.Id,
                createdAt = o.CreatedAt,
                scheduledAt = o.Date,
                status = o.Status,
                serviceName = s != null ? s.Name : "Serviço"
            }
        ).ToListAsync(ct);

        return rows.Cast<object>().ToList();
    }

    // ─── Phase 3: expanded review creation ──────────────────────────────────

    public async Task<object> CreateExpandedAsync(
        string professionalId, string clientId, string orderId,
        int rating, string? comment,
        int? punctualityRating, int? qualityRating,
        int? communicationRating, int? cleanlinessRating,
        string? photoUrls, bool isVerified,
        CancellationToken ct)
    {
        var review = new Review(
            Id: Guid.NewGuid().ToString(),
            OrderId: orderId,
            ProfessionalId: professionalId,
            ClientId: clientId,
            Rating: rating,
            Comment: comment,
            CreatedAt: DateTime.UtcNow,
            PunctualityRating: punctualityRating,
            QualityRating: qualityRating,
            CommunicationRating: communicationRating,
            CleanlinessRating: cleanlinessRating,
            PhotoUrls: photoUrls,
            IsVerified: isVerified);

        ctx.Reviews.Add(review);
        await ctx.SaveChangesAsync(ct);

        var (average, count) = await RecalculateAndUpdateRatingAsync(professionalId, ct);

        await UnlockDoubleBlindIfReadyAsync(orderId, ct);

        return new
        {
            ok = true,
            review = new
            {
                id = review.Id,
                rating = review.Rating,
                comment = review.Comment,
                punctualityRating = review.PunctualityRating,
                qualityRating = review.QualityRating,
                communicationRating = review.CommunicationRating,
                cleanlinessRating = review.CleanlinessRating,
                photoUrls = review.PhotoUrls,
                isVerified = review.IsVerified,
                createdAt = review.CreatedAt
            },
            average,
            count
        };
    }

    // ─── Phase 3: professional reviews client ────────────────────────────────

    public async Task<bool> OrderBelongsToProfessionalAsync(string orderId, string professionalId, CancellationToken ct)
        => await ctx.Orders.AsNoTracking()
            .AnyAsync(o => o.Id == orderId && o.ProfessionalId == professionalId, ct);

    public async Task<bool> ProfessionalAlreadyReviewedClientAsync(string orderId, CancellationToken ct)
        => await ctx.Reviews.AsNoTracking()
            .AnyAsync(r => r.OrderId == orderId && r.ProfessionalReviewOfClient != null, ct);

    public async Task<object> AddProfessionalReviewOfClientAsync(
        string orderId, string professionalId, string review, int? rating, CancellationToken ct)
    {
        // The professional's review is stored on the SAME row as the client's review (double-blind model).
        // The client must have already reviewed. The unique index on orderId means one row per order.
        var existing = await ctx.Reviews
            .FirstOrDefaultAsync(r => r.OrderId == orderId && r.ProfessionalId == professionalId, ct);

        if (existing is null)
            throw new InvalidOperationException("Nenhuma avaliação do cliente encontrada para este pedido. O cliente deve avaliar primeiro.");

        await ctx.Reviews
            .Where(r => r.Id == existing.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ProfessionalReviewOfClient, review)
                .SetProperty(r => r.ProfessionalRatingOfClient, rating), ct);

        await UnlockDoubleBlindIfReadyAsync(orderId, ct);

        return new { ok = true, orderId };
    }

    // ─── Phase 3: double-blind visibility unlock ─────────────────────────────

    public async Task UnlockDoubleBlindIfReadyAsync(string orderId, CancellationToken ct)
    {
        var review = await ctx.Reviews
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

        if (review is null) return;

        bool bothReviewed = review.Rating > 0 && review.ProfessionalReviewOfClient != null;
        bool timeout = review.CreatedAt < DateTime.UtcNow.AddDays(-7);

        if (!bothReviewed && !timeout) return;
        if (review.ClientVisibleAt is not null && review.ProfessionalVisibleAt is not null) return;

        var now = DateTime.UtcNow;
        await ctx.Reviews
            .Where(r => r.OrderId == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ClientVisibleAt,       now)
                .SetProperty(r => r.ProfessionalVisibleAt, now), ct);
    }

    private async Task<(double average, long count)> RecalculateAndUpdateRatingAsync(
        string professionalId, CancellationToken ct)
    {
        var agg = await ctx.Reviews
            .AsNoTracking()
            .Where(r => r.ProfessionalId == professionalId)
            .GroupBy(_ => 1)
            .Select(g => new { avg = g.Average(r => (double)r.Rating), cnt = g.Count() })
            .FirstOrDefaultAsync(ct);

        var average = agg?.avg ?? 0.0;
        var count = (long)(agg?.cnt ?? 0);

        await ctx.Professionals
            .Where(p => p.Id == professionalId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Rating, average), ct);

        return (average, count);
    }
}
