using Microsoft.Extensions.Options;

namespace Kiosk.Application.Common;

public class NotFoundException(string message) : Exception(message);

/// <summary>A dependency the request needs is not set up on this deployment (answered as 503).</summary>
public class ServiceUnavailableException(string message) : Exception(message);

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

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(Now).DateTime);

    /// <summary>Store-local wall time, for printed receipts and reports.</summary>
    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, _zone);

    /// <summary>The instant a business date starts (local midnight), in UTC as Npgsql requires for timestamptz.</summary>
    public DateTimeOffset StartOf(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, _zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}

/// <summary>Header and footer printed on receipts and slips.</summary>
public sealed class ReceiptOptions
{
    public const string Section = "Receipt";

    public string StoreName { get; set; } = "Food Ordering Kiosk";
    public string[] HeaderLines { get; set; } = [];
    public string Footer { get; set; } = "Thank you! Please keep this receipt.";
}
