using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using StackExchange.Redis;

namespace Infrastructure.Logic.Storage;

public sealed class S3ObjectStorageService : IObjectStorageService
{
    private const int MaxPresignSeconds = 60 * 60 * 24 * 7;
    private static readonly TimeSpan MaxPresignTtl = TimeSpan.FromSeconds(MaxPresignSeconds);
    private static readonly TimeSpan PresignCacheSkew = TimeSpan.FromSeconds(30);
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _region;
    private readonly string? _serviceUrl;
    private readonly string? _publicBaseUrl;
    private readonly bool _forcePathStyle;
    private readonly IDatabase? _cache;

    public S3ObjectStorageService(
        IConfiguration configuration,
        IConnectionMultiplexer multiplexer)
    {
        _bucket = configuration["S3:Bucket"]
                  ?? throw new InvalidOperationException("S3:Bucket is not configured");

        var accessKey = configuration["S3:AccessKey"];
        var secretKey = configuration["S3:SecretKey"];

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("S3:AccessKey or S3:SecretKey is not configured");
        }

        _region = configuration["S3:Region"] ?? "us-east-1";
        _serviceUrl = configuration["S3:ServiceUrl"];
        _publicBaseUrl = configuration["S3:PublicBaseUrl"];

        var forcePathStyleConfigured = bool.TryParse(configuration["S3:ForcePathStyle"], out var fps);
        _forcePathStyle = forcePathStyleConfigured ? fps : !string.IsNullOrWhiteSpace(_serviceUrl);

        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_region),
            ForcePathStyle = _forcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(_serviceUrl))
        {
            config.ServiceURL = _serviceUrl;
        }

        _client = new AmazonS3Client(accessKey, secretKey, config);
        _cache = multiplexer.GetDatabase();
    }

    public async Task<Result<StorageUploadResult>> UploadAsync(
        StorageUploadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = NormalizeKey(request.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result.Failure<StorageUploadResult>(
                    new Error("S3.InvalidKey", "Storage key is missing"));
            }

            var putRequest = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = request.Content,
                ContentType = request.ContentType,
                AutoCloseStream = false
            };

            if (request.ContentLength > 0)
            {
                putRequest.Headers.ContentLength = request.ContentLength;
            }

            await _client.PutObjectAsync(putRequest, cancellationToken);

            var url = BuildUrl(key);
            return Result.Success(new StorageUploadResult(key, url));
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Failure<StorageUploadResult>(
                new Error("S3.UploadFailed", ex.Message));
        }
        catch (Exception ex)
        {
            return Result.Failure<StorageUploadResult>(
                new Error("S3.UploadFailed", ex.Message));
        }
    }

    public Result<string> GetPresignedUrl(string keyOrUrl, TimeSpan? expiresIn = null)
    {
        try
        {
            var key = NormalizeKey(keyOrUrl);
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result.Failure<string>(new Error("S3.InvalidKey", "Storage key is missing"));
            }

            var ttl = NormalizePresignTtl(expiresIn);
            var cacheKey = BuildPresignCacheKey(key, ttl);
            var cached = TryGetCachedUrl(cacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                return Result.Success(cached);
            }

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Expires = DateTime.UtcNow.Add(ttl)
            };

            var url = _client.GetPreSignedURL(request);
            TrySetCachedUrl(cacheKey, url, NormalizeCacheTtl(ttl));
            return Result.Success(url);
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Failure<string>(new Error("S3.PresignFailed", ex.Message));
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(new Error("S3.PresignFailed", ex.Message));
        }
    }

    public async Task<Result<bool>> DeleteAsync(string keyOrUrl, CancellationToken cancellationToken)
    {
        try
        {
            var key = NormalizeKey(keyOrUrl);
            if (string.IsNullOrWhiteSpace(key))
            {
                return Result.Failure<bool>(new Error("S3.InvalidKey", "Storage key is missing"));
            }

            var request = new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = key
            };

            await _client.DeleteObjectAsync(request, cancellationToken);
            return Result.Success(true);
        }
        catch (AmazonS3Exception ex)
        {
            return Result.Failure<bool>(new Error("S3.DeleteFailed", ex.Message));
        }
        catch (Exception ex)
        {
            return Result.Failure<bool>(new Error("S3.DeleteFailed", ex.Message));
        }
    }

    private string? TryGetCachedUrl(string cacheKey)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var value = _cache.StringGet(cacheKey);
            return value.HasValue ? value.ToString() : null;
        }
        catch (RedisException)
        {
            return null;
        }
    }

    private void TrySetCachedUrl(string cacheKey, string url, TimeSpan ttl)
    {
        if (_cache is null)
        {
            return;
        }

        try
        {
            _cache.StringSet(cacheKey, url, ttl);
        }
        catch (RedisException)
        {
        }
    }

    private static TimeSpan NormalizePresignTtl(TimeSpan? expiresIn)
    {
        var ttl = expiresIn ?? MaxPresignTtl;
        if (ttl <= TimeSpan.Zero)
        {
            ttl = MaxPresignTtl;
        }

        if (ttl > MaxPresignTtl)
        {
            ttl = MaxPresignTtl;
        }

        return ttl;
    }

    private static TimeSpan NormalizeCacheTtl(TimeSpan presignTtl)
    {
        var ttl = presignTtl - PresignCacheSkew;
        return ttl <= TimeSpan.Zero ? presignTtl : ttl;
    }

    private string BuildPresignCacheKey(string key, TimeSpan ttl)
    {
        var ttlSeconds = (int)Math.Round(ttl.TotalSeconds);
        return $"s3:presign:{_bucket}:{ttlSeconds}:{key}";
    }

    private string BuildUrl(string key)
    {
        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
        {
            return $"{_publicBaseUrl.TrimEnd('/')}/{key}";
        }

        if (!string.IsNullOrWhiteSpace(_serviceUrl))
        {
            var baseUrl = _serviceUrl.TrimEnd('/');

            if (_forcePathStyle)
            {
                return $"{baseUrl}/{_bucket}/{key}";
            }

            var uri = new Uri(baseUrl);
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme}://{_bucket}.{uri.Host}{port}/{key}";
        }

        var regionSegment = string.Equals(_region, "us-east-1", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $".{_region}";

        return $"https://{_bucket}.s3{regionSegment}.amazonaws.com/{key}";
    }

    private string NormalizeKey(string keyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(keyOrUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(keyOrUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimStart('/');

            if (path.StartsWith($"{_bucket}/", StringComparison.OrdinalIgnoreCase))
            {
                return path[(_bucket.Length + 1)..];
            }

            return path;
        }

        return keyOrUrl.TrimStart('/');
    }
}

