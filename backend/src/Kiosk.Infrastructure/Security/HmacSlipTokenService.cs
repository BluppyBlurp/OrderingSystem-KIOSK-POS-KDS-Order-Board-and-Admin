using System.Buffers.Binary;
using System.Security.Cryptography;
using Kiosk.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Kiosk.Infrastructure.Security;

public sealed class SlipOptions
{
    public const string Section = "Slip";

    /// <summary>Base64 key of at least 32 bytes. Rotating it invalidates slips still on screen.</summary>
    public string SigningKey { get; set; } = "";
}

/// <summary>
/// Token = base64url(orderId ‖ expiryUnixSeconds) "." base64url(HMAC-SHA256 truncated to 16 bytes).
/// About 50 characters, so the QR stays small and scans fast.
/// </summary>
internal sealed class HmacSlipTokenService : ISlipTokenService
{
    private const int MacLength = 16;
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public HmacSlipTokenService(IOptions<SlipOptions> options, TimeProvider time)
    {
        _key = Convert.FromBase64String(options.Value.SigningKey);
        if (_key.Length < 32)
            throw new InvalidOperationException("Slip:SigningKey must be a base64 key of at least 32 bytes.");
        _time = time;
    }

    public string Create(Guid orderId, DateTimeOffset expiresAt)
    {
        var payload = new byte[20];
        orderId.TryWriteBytes(payload);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16), (uint)expiresAt.ToUnixTimeSeconds());
        return $"{Base64Url.Encode(payload)}.{Base64Url.Encode(Mac(payload))}";
    }

    public Guid? Read(string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || token.Length > 100)
            return null;

        var payload = Base64Url.TryDecode(token[..dot]);
        var mac = Base64Url.TryDecode(token[(dot + 1)..]);
        if (payload is not { Length: 20 } || mac is not { Length: MacLength })
            return null;
        if (!CryptographicOperations.FixedTimeEquals(mac, Mac(payload)))
            return null;

        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(16)));
        return _time.GetUtcNow() > expires ? null : new Guid(payload.AsSpan(0, 16));
    }

    private byte[] Mac(byte[] payload) => HMACSHA256.HashData(_key, payload)[..MacLength];
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? TryDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        var buffer = new byte[b64.Length];
        return Convert.TryFromBase64String(b64, buffer, out var written) ? buffer[..written] : null;
    }
}
