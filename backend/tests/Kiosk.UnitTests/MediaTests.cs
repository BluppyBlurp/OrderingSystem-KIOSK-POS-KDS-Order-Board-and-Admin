using System.Buffers.Binary;
using Kiosk.Application.Admin;
using Kiosk.Domain.Common;
using Kiosk.Infrastructure.Storage;
using SkiaSharp;

namespace Kiosk.UnitTests;

public class Mp4Tests
{
    [Fact]
    public void Reads_duration_from_a_version_0_movie_header() =>
        Assert.Equal(12.5, Mp4.ReadDurationSeconds(File(version: 0, timescale: 1000, duration: 12_500)));

    [Fact]
    public void Reads_duration_from_a_version_1_movie_header() =>
        Assert.Equal(90, Mp4.ReadDurationSeconds(File(version: 1, timescale: 600, duration: 54_000)));

    [Fact]
    public void Finds_moov_after_other_top_level_boxes()
    {
        byte[] withMdatFirst = [.. Box("ftyp", "isom"u8.ToArray()), .. Box("mdat", new byte[64]), .. Moov(0, 1000, 3000)];
        Assert.Equal(3, Mp4.ReadDurationSeconds(withMdatFirst));
    }

    [Theory]
    [InlineData("not a video at all")]
    [InlineData("")]
    public void Anything_else_is_not_an_mp4(string text) =>
        Assert.Null(Mp4.ReadDurationSeconds(System.Text.Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void Truncated_or_zero_timescale_files_are_rejected()
    {
        var file = File(version: 0, timescale: 1000, duration: 5000);
        Assert.Null(Mp4.ReadDurationSeconds(file.AsSpan(0, file.Length - 20)));
        Assert.Null(Mp4.ReadDurationSeconds(File(version: 0, timescale: 0, duration: 5000)));
    }

    private static byte[] File(byte version, uint timescale, ulong duration) =>
        [.. Box("ftyp", "isom"u8.ToArray()), .. Moov(version, timescale, duration)];

    private static byte[] Moov(byte version, uint timescale, ulong duration)
    {
        var body = new byte[version == 0 ? 100 : 112];
        body[0] = version;
        if (version == 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), timescale);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), (uint)duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(20), timescale);
            BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(24), duration);
        }
        return Box("moov", Box("mvhd", body));
    }

    private static byte[] Box(string type, byte[] body)
    {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type, box.AsSpan(4));
        body.CopyTo(box, 8);
        return box;
    }
}

public class SkiaImageProcessorTests
{
    private readonly SkiaImageProcessor _processor = new();

    [Fact]
    public void Small_images_are_not_upscaled()
    {
        var variants = _processor.CreateVariants(Encode(300, 200, SKEncodedImageFormat.Jpeg));
        Assert.Equal(("jpg", "image/jpeg"), (variants.OriginalExtension, variants.OriginalContentType));
        using var display = SKBitmap.Decode(variants.Display);
        Assert.Equal((300, 200), (display.Width, display.Height));
    }

    [Fact]
    public void Portrait_images_are_bounded_by_their_height()
    {
        var variants = _processor.CreateVariants(Encode(1000, 3000, SKEncodedImageFormat.Png));
        using var display = SKBitmap.Decode(variants.Display);
        using var thumb = SKBitmap.Decode(variants.Thumbnail);
        Assert.Equal((533, 1600), (display.Width, display.Height));
        Assert.Equal((160, 480), (thumb.Width, thumb.Height));
        Assert.Equal(SKEncodedImageFormat.Webp, SKCodec.Create(SKData.CreateCopy(variants.Display)).EncodedFormat);
    }

    [Fact]
    public void Non_images_are_a_domain_error()
    {
        var error = Assert.Throws<DomainException>(() => _processor.CreateVariants("GIF89a nope"u8.ToArray()));
        Assert.Equal("invalid_image", error.Code);
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }
}
