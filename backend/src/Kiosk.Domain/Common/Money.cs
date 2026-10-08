namespace Kiosk.Domain.Common;

public static class Money
{
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>VAT contained in a VAT-inclusive amount, e.g. 112.00 at 12% → 12.00.</summary>
    public static decimal VatPortion(decimal inclusiveAmount, decimal vatRate) =>
        vatRate <= 0 ? 0m : Round(inclusiveAmount * vatRate / (1 + vatRate));
}
