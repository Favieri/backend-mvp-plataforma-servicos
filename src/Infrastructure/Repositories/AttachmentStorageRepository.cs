using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Infrastructure.Repositories;

public sealed class AttachmentStorageRepository(
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
        var region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            logger.LogError("STORAGE_BUCKET_NAME não configurado.");
            return null;
        }

        var extension = Path.GetExtension(originalFileName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = contentType.Split('/').LastOrDefault() ?? "bin";

        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var key       = $"attachments/messages/{messageId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{randomHex}.{extension}";

        logger.LogInformation("S3 attachment upload iniciado. Bucket={Bucket} Region={Region} Key={Key}",
            bucketName, region, key);

        try
        {
            var config = new AmazonS3Config
            {
                ServiceURL     = $"https://s3.{region}.amazonaws.com",
                ForcePathStyle = false
            };

            using var s3 = new AmazonS3Client(config);

            var request = new PutObjectRequest
            {
                BucketName  = bucketName,
                Key         = key,
                InputStream = fileStream,
                ContentType = contentType,
            };

            var response = await s3.PutObjectAsync(request, ct);

            logger.LogInformation("S3 attachment upload concluído. HttpStatus={Status}", (int)response.HttpStatusCode);

            // Attachments são privados — URL não é pública
            // Para acesso futuro, gerar pre-signed URL no endpoint de download
            var internalUrl = $"https://{bucketName}.s3.{region}.amazonaws.com/{key}";
            return internalUrl;
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex,
                "S3 attachment upload falhou. Bucket={Bucket} Region={Region} Key={Key} ErrorCode={Code}",
                bucketName, region, key, ex.ErrorCode);
            return null;
        }
    }
}
