using Domain.Entities;

namespace Application.Abstractions;

public interface IMessageAttachmentRepository
{
    Task<MessageAttachment> CreateAsync(MessageAttachment attachment, CancellationToken ct);
    Task<IReadOnlyList<MessageAttachment>> GetByMessageIdAsync(string messageId, CancellationToken ct);
    Task<IReadOnlyList<MessageAttachment>> GetByMessageIdsAsync(IEnumerable<string> messageIds, CancellationToken ct);
}
