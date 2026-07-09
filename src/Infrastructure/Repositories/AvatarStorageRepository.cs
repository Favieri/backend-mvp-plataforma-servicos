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
        string professionalId,
        Stream fileStream,
        string contentType,
        CancellationToken ct)
    {
        var bucketName = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME");
        var region     = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            logger.LogError("STORAGE_BUCKET_NAME não configurado.");
            return null;
        }

        var extension = contentType.Split('/').LastOrDefault() ?? "jpg";
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var key       = $"avatars/professional_{professionalId}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{randomHex}.{extension}";

        logger.LogInformation("S3 upload iniciado. Bucket={Bucket} Region={Region} Key={Key}",
            bucketName, region, key);

        try
        {
            var request = new PutObjectRequest
            {
                BucketName  = bucketName,
                Key         = key,
                InputStream = fileStream,
                ContentType = contentType,
            };

            var response = await s3.PutObjectAsync(request, ct);

            logger.LogInformation("S3 upload concluído. HttpStatus={Status}", (int)response.HttpStatusCode);

            var publicUrl = $"https://{bucketName}.s3.{region}.amazonaws.com/{key}";
            return publicUrl;
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex,
                "S3 upload falhou. Bucket={Bucket} Region={Region} Key={Key} ErrorCode={Code}",
                bucketName, region, key, ex.ErrorCode);
            return null;
        }
    }
}
