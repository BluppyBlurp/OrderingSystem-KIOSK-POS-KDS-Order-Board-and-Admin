using System.Buffers.Binary;
using System.Net;
using Kiosk.Api.Auth;
using Kiosk.Application.Admin;
using Kiosk.Application.Menu;
using Kiosk.Domain.Menu;
using SkiaSharp;

namespace Kiosk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MediaUploadTests(KioskApiFactory api)
{
    private HttpClient Admin => api.Staff(Roles.Manager);

    [Fact]
    public async Task Image_upload_produces_hashed_webp_variants_and_attaches_them()
    {
        var product = await api.CreateProductAsync("Photo Burger", 99m, stock: null);
        var png = Png(2000, 1000);

        var upload = await (await Admin.PostJsonAsync("/api/admin/media/presign",
            new PresignMediaRequest(MediaType.Image, "image/png", png.Length))).ReadAsync<PresignedUploadDto>();
        Assert.StartsWith("uploads/", upload.Key);
        Assert.Contains(upload.Key, upload.UploadUrl);

        api.Media.Upload(upload.Key, png, "image/png"); // the browser's PUT
        var media = await (await Admin.PostJsonAsync($"/api/admin/products/{product.Id}/media/uploaded",
            new CompleteMediaUploadRequest(upload.Key, MediaType.Image, 0))).ReadAsync<MediaDto>();

        Assert.Matches(@"^https://media\.test/media/[0-9a-f]{32}\.webp$", media.Url);
        Assert.Matches(@"^https://media\.test/media/[0-9a-f]{32}-thumb\.webp$", media.ThumbnailUrl!);
        Assert.False(api.Media.Objects.ContainsKey(upload.Key)); // raw upload removed

        var display = Decode(Object(media.Url));
        Assert.Equal((1600, 800), (display.Width, display.Height));
        var thumb = Decode(Object(media.ThumbnailUrl!));
        Assert.Equal((480, 240), (thumb.Width, thumb.Height));
        Assert.Contains(api.Media.Objects.Keys, k => k.EndsWith("-original.png"));

        var saved = await (await Admin.GetAsync($"/api/admin/products/{product.Id}")).ReadAsync<Kiosk.Application.Admin.AdminProductDto>();
        Assert.Contains(saved.Media, m => m.Url == media.Url);
    }

    [Fact]
    public async Task A_file_that_is_not_the_image_it_claims_is_rejected_and_removed()
    {
        var product = await api.CreateProductAsync("Fake Photo", 99m, stock: null);
        var upload = await (await Admin.PostJsonAsync("/api/admin/media/presign",
            new PresignMediaRequest(MediaType.Image, "image/jpeg", 5000))).ReadAsync<PresignedUploadDto>();
        api.Media.Upload(upload.Key, "<html>not an image</html>"u8.ToArray(), "image/jpeg");

        (await Admin.PostJsonAsync($"/api/admin/products/{product.Id}/media/uploaded",
            new CompleteMediaUploadRequest(upload.Key, MediaType.Image, 0))).AssertStatus(HttpStatusCode.Conflict);
        Assert.False(api.Media.Objects.ContainsKey(upload.Key));
    }

    [Fact]
    public async Task Videos_must_be_short_mp4s()
    {
        var product = await api.CreateProductAsync("Video Burger", 99m, stock: null);

        async Task<HttpResponseMessage> UploadVideoAsync(byte[] file)
        {
            var upload = await (await Admin.PostJsonAsync("/api/admin/media/presign",
                new PresignMediaRequest(MediaType.Video, "video/mp4", file.Length))).ReadAsync<PresignedUploadDto>();
            api.Media.Upload(upload.Key, file, "video/mp4");
            return await Admin.PostJsonAsync($"/api/admin/products/{product.Id}/media/uploaded",
                new CompleteMediaUploadRequest(upload.Key, MediaType.Video, 1));
        }

        var ok = await (await UploadVideoAsync(Mp4Header(seconds: 12))).ReadAsync<MediaDto>();
        Assert.Matches(@"^https://media\.test/media/[0-9a-f]{32}\.mp4$", ok.Url);
        Assert.Null(ok.ThumbnailUrl);

        (await UploadVideoAsync(Mp4Header(seconds: 45))).AssertStatus(HttpStatusCode.Conflict);
        (await UploadVideoAsync(Png(10, 10))).AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Presign_enforces_types_and_sizes_and_keys_must_be_ours()
    {
        (await Admin.PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(MediaType.Image, "image/gif", 100)))
            .AssertStatus(HttpStatusCode.BadRequest);
        (await Admin.PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(MediaType.Video, "video/webm", 100)))
            .AssertStatus(HttpStatusCode.BadRequest);
        (await Admin.PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(MediaType.Image, "image/png", 11 * 1024 * 1024)))
            .AssertStatus(HttpStatusCode.Conflict);

        var product = await api.CreateProductAsync("Key Check", 99m, stock: null);
        api.Media.Upload("media/someone-elses.webp", Png(10, 10), "image/webp");
        (await Admin.PostJsonAsync($"/api/admin/products/{product.Id}/media/uploaded",
            new CompleteMediaUploadRequest("media/someone-elses.webp", MediaType.Image, 0))).AssertStatus(HttpStatusCode.Conflict);
        Assert.True(api.Media.Objects.ContainsKey("media/someone-elses.webp"));

        (await api.Staff(Roles.Cashier).PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(MediaType.Image, "image/png", 100)))
            .AssertStatus(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_storage_configured_media_endpoints_answer_503()
    {
        api.Media.IsConfigured = false;
        try
        {
            (await Admin.PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(MediaType.Image, "image/png", 100)))
                .AssertStatus(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            api.Media.IsConfigured = true;
        }
    }

    private byte[] Object(string url) => api.Media.Objects[url[(FakeMediaStorage.PublicBase.Length + 1)..]].Content;

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Not an image.");

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.OrangeRed);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>ftyp + moov/mvhd (version 0) — all <see cref="Mp4"/> reads. Timescale 1000, so duration is in ms.</summary>
    internal static byte[] Mp4Header(double seconds)
    {
        var ftyp = Box("ftyp", [.. "isom"u8, 0, 0, 2, 0, .. "isomiso2mp41"u8]);
        var mvhdBody = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhdBody.AsSpan(12), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhdBody.AsSpan(16), (uint)(seconds * 1000));
        var moov = Box("moov", Box("mvhd", mvhdBody));
        return [.. ftyp, .. moov];
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
