using Kiosk.Domain.Common;

namespace Kiosk.Domain.Devices;

public enum DeviceKind { Kiosk, Board }

/// <summary>A registered kiosk or order board. Authenticates with a token whose SHA-256 hash is stored here.</summary>
public class Device : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DeviceKind Kind { get; set; }
    public required string TokenHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
