using Microsoft.Extensions.Options;

namespace Kiosk.Application.Common;

public class NotFoundException(string message) : Exception(message);

public sealed class OrderingOptions
{
    public const string Section = "Ordering";

    /// <summary>How long an unpaid order holds its stock before it expires.</summary>
    public int PaymentWindowMinutes { get; set; } = 15;

    public int TableNumberMin { get; set; } = 1;
    public int TableNumberMax { get; set; } = 60;

    /// <summary>VAT rate; menu prices are VAT-inclusive.</summary>
    public decimal VatRate { get; set; } = 0.12m;

    /// <summary>IANA zone used for the business date (order numbers reset at local midnight).</summary>
    public string TimeZone { get; set; } = "Asia/Manila";
}

public sealed class BusinessClock(TimeProvider time, IOptions<OrderingOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset Now => time.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, _zone).DateTime);
}
