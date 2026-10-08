namespace Kiosk.Domain.Common;

/// <summary>A business rule was violated. Maps to 409/422, never 500.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
