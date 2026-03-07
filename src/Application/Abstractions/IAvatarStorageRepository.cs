using System.IO;

namespace Application.Abstractions;

public interface IAvatarStorageRepository
{
    Task<string?> UploadProfessionalAvatarAsync(string professionalId, Stream fileStream, string contentType, CancellationToken ct);
}
