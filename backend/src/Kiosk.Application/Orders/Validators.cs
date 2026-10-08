using FluentValidation;
using Kiosk.Application.Common;
using Kiosk.Domain.Orders;
using Microsoft.Extensions.Options;

namespace Kiosk.Application.Orders;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public const int MaxLines = 30;
    public const int MaxQuantity = 20;

    public CreateOrderRequestValidator(IOptions<OrderingOptions> options)
    {
        var o = options.Value;

        RuleFor(x => x.OrderType).IsInEnum();
        RuleFor(x => x.Items).NotEmpty().WithMessage("The cart is empty.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(MaxLines).When(x => x.Items is not null);
        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, MaxQuantity);
            line.RuleFor(l => l.Notes).MaximumLength(200);
            line.RuleFor(l => l.ModifierIds!.Count).LessThanOrEqualTo(20).When(l => l.ModifierIds is not null);
        });

        RuleFor(x => x.TableNumber)
            .NotNull().WithMessage("Enter the number on your table stand.")
            .InclusiveBetween(o.TableNumberMin, o.TableNumberMax)
            .WithMessage($"Table number must be between {o.TableNumberMin} and {o.TableNumberMax}.")
            .When(x => x.OrderType == OrderType.ServeToTable);
        RuleFor(x => x.TableNumber).Null().WithMessage("Counter pickup orders do not take a table number.")
            .When(x => x.OrderType == OrderType.CounterPickup);
    }
}

public sealed class PayRequestValidator : AbstractValidator<PayRequest>
{
    public PayRequestValidator() => RuleFor(x => x.Method).IsInEnum();
}
