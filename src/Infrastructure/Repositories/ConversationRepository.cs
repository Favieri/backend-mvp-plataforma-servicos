using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class ConversationRepository(AppDbContext ctx, IContactMaskingService maskingSvc) : IConversationRepository
{
    public async Task<string?> ResolveProfessionalUserIdAsync(string professionalIdOrUserId, CancellationToken ct)
    {
        // Check if it's a Professional.id (returns userId)
        var userId = await ctx.Professionals
            .AsNoTracking()
            .Where(p => p.Id == professionalIdOrUserId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);

        if (userId is not null) return userId;

        // Check if it's a User.id directly
        var exists = await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == professionalIdOrUserId, ct);
        return exists ? professionalIdOrUserId : null;
    }

    public async Task<IReadOnlyList<object>> GetByParticipantAsync(
        string? clientId, string? professionalId, CancellationToken ct)
    {
        var query =
            from c in ctx.Conversations.AsNoTracking()
            join client in ctx.Users.AsNoTracking() on c.ClientId equals client.Id
            join pro in ctx.Users.AsNoTracking() on c.ProfessionalId equals pro.Id
            select new { c, client, pro };

        if (!string.IsNullOrWhiteSpace(clientId))
            query = query.Where(x => x.c.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(professionalId))
            query = query.Where(x => x.c.ProfessionalId == professionalId);

        var conversations = await query
            .Select(x => new
            {
                x.c.Id,
                x.c.OrderId,
                x.c.ClientId,
                x.c.ProfessionalId,
                x.c.CreatedAt,
                x.c.ClientLastReadAt,
                x.c.ProfessionalLastReadAt,
                x.c.Status,
                ClientName = x.client.Name,
                ClientEmail = x.client.Email,
                ClientPhone = x.client.Phone,
                ProName = x.pro.Name,
                ProEmail = x.pro.Email,
                ProPhone = x.pro.Phone
            })
            .ToListAsync(ct);

        // Load last message for each conversation (avoids LATERAL JOIN, trades one extra query for simplicity)
        var conversationIds = conversations.Select(c => c.Id).ToArray();
        var lastMessages = await ctx.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.SentAt).First())
            .ToDictionaryAsync(m => m.ConversationId, ct);

        // Resolve order statuses for masking
        var orderIds = conversations
            .Where(c => c.OrderId != null)
            .Select(c => c.OrderId!)
            .Distinct()
            .ToArray();

        var orderStatuses = orderIds.Length > 0
            ? await ctx.Orders
                .AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Status, ct)
            : new Dictionary<string, string>();

        return conversations
            .OrderByDescending(c => lastMessages.TryGetValue(c.Id, out var lm) ? lm.SentAt : c.CreatedAt)
            .Select(c =>
            {
                lastMessages.TryGetValue(c.Id, out var lm);
                var orderStatus = c.OrderId != null && orderStatuses.TryGetValue(c.OrderId, out var os) ? os : null;
                var shouldMask = maskingSvc.ShouldMask(orderStatus);

                return (object)new
                {
                    id = c.Id,
                    orderId = c.OrderId,
                    status = c.Status,
                    clientId = c.ClientId,
                    professionalId = c.ProfessionalId,
                    createdAt = c.CreatedAt,
                    clientLastReadAt = c.ClientLastReadAt,
                    professionalLastReadAt = c.ProfessionalLastReadAt,
                    client = new
                    {
                        id = c.ClientId,
                        name = c.ClientName,
                        email = shouldMask ? maskingSvc.MaskEmail(c.ClientEmail) : c.ClientEmail,
                        phone = shouldMask ? maskingSvc.MaskPhone(c.ClientPhone) : c.ClientPhone
                    },
                    professional = new
                    {
                        id = c.ProfessionalId,
                        name = c.ProName,
                        email = shouldMask ? maskingSvc.MaskEmail(c.ProEmail) : c.ProEmail,
                        phone = shouldMask ? maskingSvc.MaskPhone(c.ProPhone) : c.ProPhone
                    },
                    lastMessage = lm is null
                        ? null
                        : (object)new { id = lm.Id, text = lm.Text, sentAt = lm.SentAt, senderId = lm.SenderId, type = lm.Type }
                };
            })
            .ToList();
    }

    public async Task<object?> GetOrCreateAsync(
        string clientId, string professionalUserId, string? orderId, CancellationToken ct)
    {
        Conversation? existing = null;

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            existing = await ctx.Conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.OrderId == orderId, ct);
        }

        existing ??= await ctx.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ProfessionalId == professionalUserId, ct);

        if (existing is null)
        {
            var newConv = new Conversation(
                Id: Guid.NewGuid().ToString(),
                OrderId: orderId,
                ClientId: clientId,
                ProfessionalId: professionalUserId,
                CreatedAt: DateTime.UtcNow,
                ClientLastReadAt: null,
                ProfessionalLastReadAt: null,
                Status: ConversationStatus.Active);

            ctx.Conversations.Add(newConv);
            await ctx.SaveChangesAsync(ct);
            return newConv;
        }

        // Link orderId if not yet linked
        if (existing.OrderId is null && !string.IsNullOrWhiteSpace(orderId))
        {
            try
            {
                await ctx.Conversations
                    .Where(c => c.Id == existing.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrderId, orderId), ct);

                existing = existing with { OrderId = orderId };
            }
            catch { /* ignore unique constraint conflict */ }
        }

        return existing;
    }

    public async Task<IReadOnlyList<object>> GetMessagesAsync(string conversationId, CancellationToken ct)
    {
        var messages = await (
            from m in ctx.Messages.AsNoTracking()
            join u in ctx.Users.AsNoTracking() on m.SenderId equals u.Id
            where m.ConversationId == conversationId
            orderby m.SentAt ascending
            select new
            {
                m.Id,
                m.ConversationId,
                m.SenderId,
                m.Text,
                m.SentAt,
                m.Type,
                m.Metadata,
                m.ReplyToId,
                SenderName = u.Name
            }
        ).ToListAsync(ct);

        // Load attachments for all messages in one query
        var messageIds = messages.Select(m => m.Id).ToArray();
        var attachments = messageIds.Length > 0
            ? await ctx.MessageAttachments
                .AsNoTracking()
                .Where(a => messageIds.Contains(a.MessageId))
                .ToListAsync(ct)
            : [];

        var attachmentsByMessage = attachments
            .GroupBy(a => a.MessageId)
            .ToDictionary(g => g.Key, g => g.Select(a => new
            {
                id = a.Id,
                type = a.Type,
                url = a.Url,
                thumbnailUrl = a.ThumbnailUrl,
                fileName = a.FileName,
                sizeBytes = a.SizeBytes
            }).ToList());

        return messages.Select(m => (object)new
        {
            id = m.Id,
            conversationId = m.ConversationId,
            senderId = m.SenderId,
            text = m.Text,
            sentAt = m.SentAt,
            type = m.Type,
            metadata = m.Metadata,
            replyToId = m.ReplyToId,
            sender = new { id = m.SenderId, name = m.SenderName },
            attachments = attachmentsByMessage.TryGetValue(m.Id, out var att) ? att : []
        }).ToList();
    }

    public async Task<object> CreateMessageAsync(
        string conversationId,
        string senderId,
        string text,
        string type,
        string? metadata,
        string? replyToId,
        CancellationToken ct)
    {
        var message = new Message(
            Id: Guid.NewGuid().ToString(),
            ConversationId: conversationId,
            SenderId: senderId,
            Text: text,
            SentAt: DateTime.UtcNow,
            Type: type,
            Metadata: metadata,
            ReplyToId: replyToId);

        ctx.Messages.Add(message);

        var sender = await ctx.Users
            .AsNoTracking()
            .Where(u => u.Id == senderId)
            .Select(u => new { u.Name, u.Email })
            .FirstOrDefaultAsync(ct);

        await ctx.SaveChangesAsync(ct);

        return new
        {
            id = message.Id,
            conversationId = message.ConversationId,
            senderId = message.SenderId,
            text = message.Text,
            sentAt = message.SentAt,
            type = message.Type,
            metadata = message.Metadata,
            replyToId = message.ReplyToId,
            senderName = sender?.Name,
            senderEmail = sender?.Email,
            attachments = Array.Empty<object>()
        };
    }

    public async Task<object?> GetConversationForReadAsync(string conversationId, CancellationToken ct)
    {
        return await (
            from c in ctx.Conversations.AsNoTracking()
            join client in ctx.Users.AsNoTracking() on c.ClientId equals client.Id
            join pro in ctx.Users.AsNoTracking() on c.ProfessionalId equals pro.Id
            where c.Id == conversationId
            select new
            {
                id = c.Id,
                status = c.Status,
                clientId = c.ClientId,
                professionalId = c.ProfessionalId,
                orderId = c.OrderId,
                clientLastReadAt = c.ClientLastReadAt,
                professionalLastReadAt = c.ProfessionalLastReadAt,
                clientEmail = client.Email,
                clientName = client.Name,
                professionalEmail = pro.Email,
                professionalName = pro.Name
            }
        ).FirstOrDefaultAsync(ct);
    }

    public async Task MarkReadAsync(string conversationId, bool isClient, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (isClient)
        {
            await ctx.Conversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ClientLastReadAt, now), ct);
        }
        else
        {
            await ctx.Conversations
                .Where(c => c.Id == conversationId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProfessionalLastReadAt, now), ct);
        }
    }

    public async Task UpdateConversationStatusAsync(string conversationId, string status, CancellationToken ct)
    {
        await ctx.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status), ct);
    }

    /// <summary>
    /// Returns the available transactional actions for a conversation based on its current state.
    /// Actions vary by the conversation's linked order status and the requesting user's role.
    /// </summary>
    public async Task<object> GetConversationActionsAsync(
        string conversationId, string requestingUserId, CancellationToken ct)
    {
        var conv = await ctx.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conv is null)
            return new { conversationId, actions = Array.Empty<object>() };

        var isClient = conv.ClientId == requestingUserId;
        var isProfessional = conv.ProfessionalId == requestingUserId;

        // Resolve order status if linked
        string? orderStatus = null;
        string? proposalStatus = null;

        if (conv.OrderId is not null)
        {
            orderStatus = await ctx.Orders
                .AsNoTracking()
                .Where(o => o.Id == conv.OrderId)
                .Select(o => o.Status)
                .FirstOrDefaultAsync(ct);
        }

        // Check for pending/sent proposal linked to this conversation
        var openProposal = await ctx.Proposals
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId
                && (p.Status == Domain.Enums.ProposalStatus.Sent || p.Status == Domain.Enums.ProposalStatus.Negotiating))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Status, p.PriceTotalCents, p.Scope })
            .FirstOrDefaultAsync(ct);

        proposalStatus = openProposal?.Status;

        var actions = new List<object>();

        // Professional can send a proposal when no order exists and no open proposal
        if (isProfessional && conv.OrderId is null && openProposal is null)
        {
            actions.Add(new { type = "send_proposal", label = "Enviar proposta" });
        }

        // Professional can suggest a schedule
        if (isProfessional && (orderStatus is null || orderStatus == OrderStatus.Draft))
        {
            actions.Add(new { type = "suggest_schedule", label = "Sugerir horário" });
        }

        // Client can accept/reject open proposal
        if (isClient && openProposal is not null)
        {
            actions.Add(new
            {
                type = "accept_proposal",
                label = "Aceitar proposta",
                proposalId = openProposal.Id,
                priceTotalCents = openProposal.PriceTotalCents
            });
            actions.Add(new
            {
                type = "reject_proposal",
                label = "Recusar proposta",
                proposalId = openProposal.Id
            });
        }

        // Client can request negotiation on open proposal
        if (isClient && openProposal is not null && proposalStatus == Domain.Enums.ProposalStatus.Sent)
        {
            actions.Add(new
            {
                type = "negotiate_proposal",
                label = "Negociar proposta",
                proposalId = openProposal.Id
            });
        }

        return new
        {
            conversationId,
            orderStatus,
            proposalStatus,
            actions
        };
    }
}
