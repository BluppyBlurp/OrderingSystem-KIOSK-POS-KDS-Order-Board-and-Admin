namespace Kiosk.Domain.Common;

/// <summary>Marker: every change to this entity is written to <see cref="Auditing.AuditLog"/>.</summary>
public interface IAuditable
{
    Guid Id { get; }
}
