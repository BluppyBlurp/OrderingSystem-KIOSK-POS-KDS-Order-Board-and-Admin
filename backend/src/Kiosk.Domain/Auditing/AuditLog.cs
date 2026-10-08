namespace Kiosk.Domain.Auditing;

public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? ActorId { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
