using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Infrastructure.Repositories;

public sealed class AttachmentStorageRepository(
    IAmazonS3 s3,
    ILogger<AttachmentStorageRepository> logger) : IAttachmentStorageRepository
{
    public async Task<string?> UploadAsync(
        string messageId,
        Stream fileStream,
        string contentType,
        string originalFileName,
        CancellationToken ct)
    {
        var bucketName = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME");
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            logger.LogError("STORAGE_BUCKET_NAME não configurado.");
            return null;
        }

        var extension = Path.GetExtension(originalFileName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = contentType.Split('/').LastOrDefault() ?? "bin";

        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var key = $"attachments/messages/{messageId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{suffix}.{extension}";

        try
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType,
                AutoCloseStream = false,
            };

            await s3.PutObjectAsync(putRequest, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "S3 attachment upload failed. Key={Key}", key);
            return null;
        }

        var urlRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddDays(7),
        };

        return s3.GetPreSignedURL(urlRequest);
    }
}
