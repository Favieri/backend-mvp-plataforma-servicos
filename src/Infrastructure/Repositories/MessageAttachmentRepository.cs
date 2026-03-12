using Application.Abstractions;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class MessageAttachmentRepository(AppDbContext ctx) : IMessageAttachmentRepository
{
    public async Task<MessageAttachment> CreateAsync(MessageAttachment attachment, CancellationToken ct)
    {
        ctx.MessageAttachments.Add(attachment);
        await ctx.SaveChangesAsync(ct);
        return attachment;
    }

    public async Task<IReadOnlyList<MessageAttachment>> GetByMessageIdAsync(string messageId, CancellationToken ct)
    {
        return await ctx.MessageAttachments
            .AsNoTracking()
            .Where(a => a.MessageId == messageId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MessageAttachment>> GetByMessageIdsAsync(
        IEnumerable<string> messageIds, CancellationToken ct)
    {
        var ids = messageIds.ToArray();
        return await ctx.MessageAttachments
            .AsNoTracking()
            .Where(a => ids.Contains(a.MessageId))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }
}
