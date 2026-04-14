using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Infrastructure.Repositories;

public sealed class AvatarStorageRepository(
    IAmazonS3 s3,
    ILogger<AvatarStorageRepository> logger) : IAvatarStorageRepository
{
    public async Task<string?> UploadProfessionalAvatarAsync(
        string professionalId, Stream fileStream, string contentType, CancellationToken ct)
    {
        var bucketName = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME");
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            logger.LogError("STORAGE_BUCKET_NAME não configurado.");
            return null;
        }

        var extension = contentType.Split('/').LastOrDefault() ?? "jpg";
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var key = $"avatars/professional_{professionalId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{suffix}.{extension}";

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            CannedACL = S3CannedACL.PublicRead,
            AutoCloseStream = false,
        };

        try
        {
            await s3.PutObjectAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "S3 avatar upload failed. Key={Key}", key);
            return null;
        }

        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
        return $"https://{bucketName}.s3.{region}.amazonaws.com/{key}";
    }
}
