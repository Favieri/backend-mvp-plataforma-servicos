using Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Infrastructure.Repositories;

public sealed class AttachmentStorageRepository(
    IHttpClientFactory httpClientFactory,
    ILogger<AttachmentStorageRepository> logger) : IAttachmentStorageRepository
{
    private const string Bucket = "chat-attachments";

    public async Task<string?> UploadAsync(
        string messageId,
        Stream fileStream,
        string contentType,
        string originalFileName,
        CancellationToken ct)
    {
        var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")?.TrimEnd('/');
        var serviceRoleKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            logger.LogError("Supabase environment variables are missing.");
            return null;
        }

        var extension = Path.GetExtension(originalFileName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = contentType.Split('/').LastOrDefault() ?? "bin";

        var randomSuffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var key = $"messages/{messageId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{randomSuffix}.{extension}";
        var uploadUrl = $"{supabaseUrl}/storage/v1/object/{Bucket}/{Uri.EscapeDataString(key)}";
        var publicUrl = $"{supabaseUrl}/storage/v1/object/public/{Bucket}/{key}";

        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = streamContent
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        requestMessage.Headers.Add("apikey", serviceRoleKey);
        requestMessage.Headers.Add("x-upsert", "true");

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(requestMessage, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Supabase attachment upload failed. Status={StatusCode}; Body={Body}",
                (int)response.StatusCode, body);
            return null;
        }

        return publicUrl;
    }
}
