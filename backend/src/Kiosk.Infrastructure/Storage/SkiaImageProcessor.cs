using Kiosk.Application.Abstractions;
using Kiosk.Domain.Common;
using SkiaSharp;

namespace Kiosk.Infrastructure.Storage;

/// <summary>
/// Server-side variants at upload time (docs §3.2): R2 has no on-the-fly transforms, so each menu image is resized and
/// re-encoded once. Re-encoding also strips whatever metadata (EXIF, GPS) the original carried.
/// </summary>
internal sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>Longest edge of the product-detail image; kiosk screens are 1080p portrait.</summary>
    internal const int DisplayMaxEdge = 1600;

    /// <summary>Longest edge of the menu-grid tile image.</summary>
    internal const int ThumbnailMaxEdge = 480;

    /// <summary>Refuse decompression bombs: a small file that would decode to a huge bitmap.</summary>
    private const long MaxPixels = 40_000_000;

    private const int Quality = 82;

    public ImageVariants CreateVariants(byte[] original)
    {
        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data)
                          ?? throw new DomainException("invalid_image", "That file is not a JPEG, PNG or WebP image.");

        var (extension, contentType) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ("jpg", "image/jpeg"),
            SKEncodedImageFormat.Png => ("png", "image/png"),
            SKEncodedImageFormat.Webp => ("webp", "image/webp"),
            _ => throw new DomainException("invalid_image", "Images must be JPEG, PNG or WebP."),
        };
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new DomainException("image_too_large", "That image has too many pixels. Resize it below 40 megapixels.");

        using var decoded = SKBitmap.Decode(codec)
                            ?? throw new DomainException("invalid_image", "That image could not be read.");
        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);

        return new ImageVariants(Encode(upright, DisplayMaxEdge), Encode(upright, ThumbnailMaxEdge), extension, contentType);
    }

    private static byte[] Encode(SKBitmap source, int maxEdge)
    {
        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = scale < 1.0
            ? source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : source.Copy();
        using var image = SKImage.FromBitmap(resized);
        using var webp = image.Encode(SKEncodedImageFormat.Webp, Quality);
        return webp.ToArray();
    }

    /// <summary>Phone photos are often stored sideways with an EXIF rotation flag; bake it in, since the flag is dropped.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return bitmap.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(result);
        float w = result.Width, h = result.Height;
        // Maps a source pixel (x, y) to its upright position: x' = a·x + b·y + c, y' = d·x + e·y + f.
        var (a, b, c, d, e, f) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, w, 0f, 1f, 0f),      // mirrored
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),   // upside down
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, h),    // mirrored vertically
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),       // transposed
            SKEncodedOrigin.RightTop => (0f, -1f, w, 1f, 0f, 0f),      // needs 90° clockwise
            SKEncodedOrigin.RightBottom => (0f, -1f, w, -1f, 0f, h),   // transverse
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, h),    // needs 90° counter-clockwise
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };
        canvas.SetMatrix(new SKMatrix(a, b, c, d, e, f, 0, 0, 1));
        using var source = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(source, 0, 0, SKSamplingOptions.Default); // pure 90° turns and flips: no resampling needed
        return result;
    }
}
