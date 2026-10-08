using System.Collections.Concurrent;
using Kiosk.Application.Abstractions;

namespace Kiosk.IntegrationTests;

/// <summary>Stands in for R2. Tests play the browser's part by calling <see cref="Upload"/> with the presigned key.</summary>
public sealed class FakeMediaStorage : IMediaStorage
{
    public const string PublicBase = "https://media.test";

    public ConcurrentDictionary<string, (byte[] Content, string ContentType)> Objects { get; } = new();

    public bool IsConfigured { get; set; } = true;

    public void Upload(string key, byte[] content, string contentType) => Objects[key] = (content, contentType);

    public Uri PresignUpload(string key, string contentType, TimeSpan validFor) =>
        new($"https://r2.test/bucket/{key}?X-Amz-Expires={(int)validFor.TotalSeconds}");

    public Task<long?> GetSizeAsync(string key, CancellationToken ct) =>
        Task.FromResult(Objects.TryGetValue(key, out var o) ? (long?)o.Content.Length : null);

    public Task<byte[]> ReadAsync(string key, CancellationToken ct) => Task.FromResult(Objects[key].Content);

    public Task PutPublicAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        Objects[key] = (content, contentType);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public string PublicUrl(string key) => $"{PublicBase}/{key}";
}
