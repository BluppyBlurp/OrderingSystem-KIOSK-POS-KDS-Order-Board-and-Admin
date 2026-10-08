using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Kiosk.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Kiosk.Infrastructure.Storage;

public sealed class R2Options
{
    public const string Section = "Storage:R2";

    /// <summary>Cloudflare account id; the S3 endpoint is https://{AccountId}.r2.cloudflarestorage.com.</summary>
    public string AccountId { get; set; } = "";

    /// <summary>R2 API token (Object Read &amp; Write, this bucket only). Never shipped to a frontend.</summary>
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Bucket { get; set; } = "";

    /// <summary>Public base for kiosks, e.g. https://media.example.com or the bucket's r2.dev URL. No trailing slash needed.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey) && !string.IsNullOrWhiteSpace(Bucket) &&
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Cloudflare R2 through its S3-compatible API.</summary>
internal sealed class R2MediaStorage : IMediaStorage, IDisposable
{
    /// <summary>Public keys are content-hashed, so a URL's bytes never change and browsers may keep them for a year.</summary>
    internal const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private readonly R2Options _options;
    private readonly Lazy<AmazonS3Client> _client;

    public R2MediaStorage(IOptions<R2Options> options)
    {
        _options = options.Value;
        _client = new Lazy<AmazonS3Client>(() => new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{_options.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto",
                ForcePathStyle = true,
                // R2 rejects the CRC checksums newer SDKs add to every request by default.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            }));
    }

    public bool IsConfigured => _options.IsComplete;

    private AmazonS3Client Client => _client.Value;

    public Uri PresignUpload(string key, string contentType, TimeSpan validFor) =>
        new(Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Protocol = Protocol.HTTPS,
            Expires = DateTime.UtcNow + validFor,
        }));

    public async Task<long?> GetSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            var head = await Client.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return head.ContentLength;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken ct)
    {
        using var response = await Client.GetObjectAsync(_options.Bucket, key, ct);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    public async Task PutPublicAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var body = new MemoryStream(content, writable: false);
        await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = body,
            ContentType = contentType,
            Headers = { CacheControl = ImmutableCacheControl },
            DisablePayloadSigning = true, // R2 does not support streaming SigV4 payload signing
        }, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct) => Client.DeleteObjectAsync(_options.Bucket, key, ct);

    public string PublicUrl(string key) => $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";

    public void Dispose()
    {
        if (_client.IsValueCreated)
            _client.Value.Dispose();
    }
}
