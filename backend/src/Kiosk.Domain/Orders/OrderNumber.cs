using System.Text.RegularExpressions;

namespace Kiosk.Domain.Orders;

/// <summary>Daily order numbers: the first order of the day is A-101.</summary>
public static partial class OrderNumber
{
    private const string Prefix = "A-";
    private const int Offset = 100;

    public static string Format(int dailySequence) => $"{Prefix}{Offset + dailySequence}";

    /// <summary>Accepts what a cashier might type ("A-101", "a101", "101") and returns "A-101", or null.</summary>
    public static string? Normalize(string input)
    {
        var match = TypedNumber().Match(input.Trim());
        return match.Success ? $"{Prefix}{int.Parse(match.Groups[1].Value)}" : null;
    }

    [GeneratedRegex(@"^(?:[Aa]\s*-?\s*)?(\d{1,6})$")]
    private static partial Regex TypedNumber();
}
