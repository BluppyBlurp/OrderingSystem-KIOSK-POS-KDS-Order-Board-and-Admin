using Kiosk.Application.Common;
using Kiosk.Application.Orders;
using Kiosk.Domain.Common;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.Extensions.Options;

namespace Kiosk.UnitTests;

public class ModifierRulesTests
{
    private static (Product Product, Modifier Medium, Modifier Large, Modifier Cheese, Modifier Bacon) Burger()
    {
        var size = new ModifierGroup { Name = "Size", MinSelect = 1, MaxSelect = 1, IsRequired = true };
        var medium = new Modifier { Name = "Medium", ModifierGroupId = size.Id };
        var large = new Modifier { Name = "Large", ModifierGroupId = size.Id, PriceDelta = 20 };
        size.Modifiers.AddRange([medium, large]);

        var addOns = new ModifierGroup { Name = "Add-ons", MinSelect = 0, MaxSelect = 2 };
        var cheese = new Modifier { Name = "Cheese", ModifierGroupId = addOns.Id, PriceDelta = 15 };
        var bacon = new Modifier { Name = "Bacon", ModifierGroupId = addOns.Id, PriceDelta = 25, IsAvailable = false };
        addOns.Modifiers.AddRange([cheese, bacon]);

        var product = new Product { Name = "Burger", BasePrice = 99 };
        product.ModifierGroups.Add(new ProductModifierGroup { ModifierGroup = size, ModifierGroupId = size.Id });
        product.ModifierGroups.Add(new ProductModifierGroup { ModifierGroup = addOns, ModifierGroupId = addOns.Id });
        return (product, medium, large, cheese, bacon);
    }

    [Fact]
    public void Valid_selection_is_returned()
    {
        var b = Burger();
        var chosen = ModifierRules.Resolve(b.Product, [b.Large.Id, b.Cheese.Id]);
        Assert.Equal(35, chosen.Sum(m => m.PriceDelta));
    }

    [Fact]
    public void Required_group_must_be_picked() =>
        Assert.Equal("modifier_min", Assert.Throws<DomainException>(() => ModifierRules.Resolve(Burger().Product, [])).Code);

    [Fact]
    public void Max_select_is_enforced()
    {
        var b = Burger();
        Assert.Equal("modifier_max",
            Assert.Throws<DomainException>(() => ModifierRules.Resolve(b.Product, [b.Medium.Id, b.Large.Id])).Code);
    }

    [Fact]
    public void Unavailable_modifier_is_rejected()
    {
        var b = Burger();
        Assert.Equal("modifier_unavailable",
            Assert.Throws<DomainException>(() => ModifierRules.Resolve(b.Product, [b.Medium.Id, b.Bacon.Id])).Code);
    }

    [Fact]
    public void Modifier_from_another_product_is_rejected()
    {
        var b = Burger();
        Assert.Equal("invalid_modifier",
            Assert.Throws<DomainException>(() => ModifierRules.Resolve(b.Product, [b.Medium.Id, Guid.NewGuid()])).Code);
    }

    [Fact]
    public void Duplicate_modifier_is_rejected()
    {
        var b = Burger();
        Assert.Equal("duplicate_modifier",
            Assert.Throws<DomainException>(() => ModifierRules.Resolve(b.Product, [b.Medium.Id, b.Cheese.Id, b.Cheese.Id])).Code);
    }
}

public class OrderNumberAndMoneyTests
{
    [Fact]
    public void First_order_of_the_day_is_A101() => Assert.Equal("A-101", OrderNumber.Format(1));

    [Theory]
    [InlineData("A-101", "A-101")]
    [InlineData("a101", "A-101")]
    [InlineData(" 101 ", "A-101")]
    [InlineData("A - 205", "A-205")]
    [InlineData("B-101", null)]
    [InlineData("abc", null)]
    public void Typed_numbers_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, OrderNumber.Normalize(input));

    [Theory]
    [InlineData(112.00, 12.00)]
    [InlineData(285.00, 30.54)]
    [InlineData(0, 0)]
    public void Vat_portion_of_inclusive_amount(decimal amount, decimal vat) =>
        Assert.Equal(vat, Money.VatPortion(amount, 0.12m));
}

public class CreateOrderValidatorTests
{
    private static readonly CreateOrderRequestValidator Validator = new(Options.Create(new OrderingOptions()));
    private static readonly CreateOrderLine Line = new(Guid.NewGuid(), 1, null, null);

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Table_number_range_for_serve_to_table(int? table, bool valid) =>
        Assert.Equal(valid, Validator.Validate(new CreateOrderRequest(OrderType.ServeToTable, table, [Line])).IsValid);

    [Fact]
    public void Empty_cart_is_invalid() =>
        Assert.False(Validator.Validate(new CreateOrderRequest(OrderType.CounterPickup, null, [])).IsValid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Quantity_bounds(int qty, bool valid) =>
        Assert.Equal(valid, Validator.Validate(new CreateOrderRequest(OrderType.CounterPickup, null, [Line with { Quantity = qty }])).IsValid);
}
