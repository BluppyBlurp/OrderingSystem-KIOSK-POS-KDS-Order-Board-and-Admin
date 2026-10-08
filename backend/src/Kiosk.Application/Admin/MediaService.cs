using System.Security.Cryptography;
using FluentValidation;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Application.Menu;
using Kiosk.Domain.Common;
using Kiosk.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kiosk.Application.Admin;

public sealed class MediaOptions
{
    public const string Section = "Media";

    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 50 * 1024 * 1024;
    public int MaxVideoSeconds { get; set; } = 30;
    public int UploadUrlMinutes { get; set; } = 10;
}

public sealed record PresignMediaRequest(MediaType Type, string ContentType, long SizeBytes);

/// <summary>PUT the file to <c>UploadUrl</c> with the same Content-Type, then call <c>media/uploaded</c> with <c>Key</c>.</summary>
public sealed record PresignedUploadDto(string Key, string UploadUrl, string ContentType, long MaxBytes, DateTimeOffset ExpiresAt);

public sealed record CompleteMediaUploadRequest(string Key, MediaType Type, int SortOrder);

public sealed class PresignMediaRequestValidator : AbstractValidator<PresignMediaRequest>
{
    public PresignMediaRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.ContentType)
            .Must((r, ct) => MediaService.AllowedContentTypes(r.Type).Contains(ct))
            .WithMessage(r => $"{r.Type} uploads must be one of: {string.Join(", ", MediaService.AllowedContentTypes(r.Type))}.");
        RuleFor(x => x.SizeBytes).GreaterThan(0);
    }
}

/// <summary>
/// Menu media in Cloudflare R2 (docs §3.2). The browser uploads the original straight to the bucket with a presigned
/// PUT, so large videos never pass through the API. The API then reads it back once to check it really is what it
/// claims, writes the public variants under content-hashed keys (cacheable forever), and removes the upload.
/// </summary>
public sealed class MediaService(
    IAppDbContext db,
    IMediaStorage storage,
    IImageProcessor images,
    IOrderNotifier notifier,
    IServiceProvider services,
    BusinessClock clock,
    IOptions<MediaOptions> options)
{
    private const string UploadPrefix = "uploads/";
    private const string PublicPrefix = "media/";

    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>MP4 only: every kiosk browser plays it, and the API can read its duration without a video library.</summary>
    private static readonly string[] VideoTypes = ["video/mp4"];

    public static IReadOnlyList<string> AllowedContentTypes(MediaType type) => type == MediaType.Video ? VideoTypes : ImageTypes;

    public async Task<PresignedUploadDto> PresignAsync(PresignMediaRequest r, CancellationToken ct)
    {
        EnsureConfigured();
        await services.GetRequiredService<IValidator<PresignMediaRequest>>().ValidateAndThrowAsync(r, ct);
        var maxBytes = MaxBytes(r.Type);
        if (r.SizeBytes > maxBytes)
            throw new DomainException("file_too_large", $"{r.Type} files can be at most {maxBytes / (1024 * 1024)} MB.");

        var validFor = TimeSpan.FromMinutes(options.Value.UploadUrlMinutes);
        var key = $"{UploadPrefix}{Guid.NewGuid():N}";
        var url = storage.PresignUpload(key, r.ContentType, validFor);
        return new PresignedUploadDto(key, url.ToString(), r.ContentType, maxBytes, clock.Now + validFor);
    }

    public async Task<MediaDto> CompleteUploadAsync(Guid productId, CompleteMediaUploadRequest r, CancellationToken ct)
    {
        EnsureConfigured();
        // Only keys this API handed out; never let a caller point at some other object in the bucket.
        if (!r.Key.StartsWith(UploadPrefix, StringComparison.Ordinal) || !Guid.TryParseExact(r.Key[UploadPrefix.Length..], "N", out _))
            throw new DomainException("invalid_upload_key", "Unknown upload. Request a new upload URL and try again.");
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            throw new NotFoundException("Product not found.");

        var size = await storage.GetSizeAsync(r.Key, ct)
                   ?? throw new DomainException("upload_missing", "The file has not finished uploading.");
        try
        {
            var maxBytes = MaxBytes(r.Type);
            if (size > maxBytes)
                throw new DomainException("file_too_large", $"{r.Type} files can be at most {maxBytes / (1024 * 1024)} MB.");

            var original = await storage.ReadAsync(r.Key, ct);
            var hash = Convert.ToHexStringLower(SHA256.HashData(original))[..32];

            var media = new ProductMedia { ProductId = productId, Type = r.Type, Url = "", SortOrder = r.SortOrder };
            if (r.Type == MediaType.Image)
            {
                var variants = images.CreateVariants(original);
                await storage.PutPublicAsync($"{PublicPrefix}{hash}-original.{variants.OriginalExtension}", original, variants.OriginalContentType, ct);
                await storage.PutPublicAsync($"{PublicPrefix}{hash}.webp", variants.Display, "image/webp", ct);
                await storage.PutPublicAsync($"{PublicPrefix}{hash}-thumb.webp", variants.Thumbnail, "image/webp", ct);
                media.Url = storage.PublicUrl($"{PublicPrefix}{hash}.webp");
                media.ThumbnailUrl = storage.PublicUrl($"{PublicPrefix}{hash}-thumb.webp");
            }
            else
            {
                var seconds = Mp4.ReadDurationSeconds(original)
                              ?? throw new DomainException("invalid_video", "That file is not a playable MP4 video.");
                if (seconds > options.Value.MaxVideoSeconds)
                    throw new DomainException("video_too_long", $"Videos can be at most {options.Value.MaxVideoSeconds} seconds (this one is {seconds:0} s).");
                await storage.PutPublicAsync($"{PublicPrefix}{hash}.mp4", original, "video/mp4", ct);
                media.Url = storage.PublicUrl($"{PublicPrefix}{hash}.mp4");
            }

            db.ProductMedia.Add(media);
            await db.SaveChangesAsync(ct);
            await notifier.MenuChangedAsync(ct);
            return new MediaDto(media.Id, media.Type, media.Url, media.ThumbnailUrl);
        }
        finally
        {
            // Accepted or rejected, the raw upload has served its purpose.
            await storage.DeleteAsync(r.Key, CancellationToken.None);
        }
    }

    private long MaxBytes(MediaType type) => type == MediaType.Video ? options.Value.MaxVideoBytes : options.Value.MaxImageBytes;

    private void EnsureConfigured()
    {
        if (!storage.IsConfigured)
            throw new ServiceUnavailableException("Media storage is not configured yet. Set the Storage:R2 settings on the API.");
    }
}
