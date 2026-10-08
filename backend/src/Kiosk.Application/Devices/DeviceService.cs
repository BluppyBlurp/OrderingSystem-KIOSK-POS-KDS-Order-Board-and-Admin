using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Devices;

public sealed record DeviceDto(Guid Id, string Name, DeviceKind Kind, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt);

public sealed record RegisterDeviceRequest(string Name, DeviceKind Kind);

/// <summary>The plain token is returned exactly once; only its hash is stored.</summary>
public sealed record RegisteredDeviceDto(DeviceDto Device, string Token);

public sealed record DeviceIdentity(Guid Id, string Name, DeviceKind Kind);

public sealed class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed class DeviceService(IAppDbContext db, IValidator<RegisterDeviceRequest> validator, TimeProvider time)
{
    /// <summary>Device tokens carry this prefix so the API can tell them apart from Clerk JWTs.</summary>
    public const string TokenPrefix = "dev_";

    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(1);

    public async Task<RegisteredDeviceDto> RegisterAsync(RegisterDeviceRequest r, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(r, ct);

        var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var device = new Device
        {
            Name = r.Name.Trim(), Kind = r.Kind, TokenHash = Hash(token), CreatedAt = time.GetUtcNow(),
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return new RegisteredDeviceDto(ToDto(device), token);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct) =>
        (await db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var device = await db.Devices.FindAsync([id], ct) ?? throw new NotFoundException("Device not found.");
        device.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves an active device from its token, or null. Updates LastSeenAt at most once a minute.</summary>
    public async Task<DeviceIdentity?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) || token.Length > 200)
            return null;

        var hash = Hash(token);
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.TokenHash == hash && d.IsActive, ct);
        if (device is null)
            return null;

        var now = time.GetUtcNow();
        if (device.LastSeenAt is null || now - device.LastSeenAt > LastSeenResolution)
        {
            // Bulk update: bypasses the audit log, which is for human edits, not heartbeats.
            await db.Devices.Where(d => d.Id == device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, now), ct);
        }

        return new DeviceIdentity(device.Id, device.Name, device.Kind);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DeviceDto ToDto(Device d) => new(d.Id, d.Name, d.Kind, d.IsActive, d.CreatedAt, d.LastSeenAt);
}
