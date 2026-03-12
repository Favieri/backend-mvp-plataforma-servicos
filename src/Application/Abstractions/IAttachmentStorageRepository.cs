namespace Application.Abstractions;

public interface IAttachmentStorageRepository
{
    /// <summary>
    /// Faz upload de um anexo de chat para o Supabase Storage (bucket: chat-attachments).
    /// Retorna a URL pública do arquivo ou null em caso de falha.
    /// </summary>
    Task<string?> UploadAsync(
        string messageId,
        Stream fileStream,
        string contentType,
        string originalFileName,
        CancellationToken ct);
}
