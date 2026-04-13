using System.IO;

namespace Application.Abstractions;

public interface ILogoStorageRepository
{
    Task<string?> UploadProfessionalLogoAsync(string professionalId, Stream fileStream, string contentType, CancellationToken ct);
}
