using System.Net;
using Kiosk.Application.Menu;
using Kiosk.Application.Orders;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Orders;
using Kiosk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoMenuTests(KioskApiFactory api)
{
    private async Task<MenuDto> SeededMenuAsync(HttpClient kiosk)
    {
        await using (var scope = api.Services.CreateAsyncScope())
            await DemoDataSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), seedMenu: true, kioskToken: null);
        return await (await kiosk.GetAsync("/api/kiosk/menu")).ReadAsync<MenuDto>();
    }

    [Fact]
    public async Task Meal_walks_through_every_question_and_is_priced_by_the_server()
    {
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var menu = await SeededMenuAsync(kiosk);
        var meal = menu.Categories.Single(c => c.Name == "Rice Meals").Products.Single(p => p.Name == "1-pc Chicken Meal");

        Assert.Equal(
            ["Choose your drink", "Upsize your drink?", "Upsize your fries?", "Fries flavor", "Anything to add?"],
            meal.ModifierGroups.Select(g => g.Name));

        Guid Pick(string group, string option) =>
            meal.ModifierGroups.Single(g => g.Name == group).Modifiers.Single(m => m.Name == option).Id;

        // Tapped out of order on purpose; the order comes back in question order.
        var picks = new[]
        {
            Pick("Anything to add?", "Extra gravy"), Pick("Choose your drink", "Sprite"), Pick("Upsize your drink?", "Large drink"), Pick("Upsize your fries?", "Large fries"),
            Pick("Fries flavor", "Cheese fries"),
        };
        var order = await (await kiosk.PostJsonAsync("/api/kiosk/orders", new CreateOrderRequest(
            DiningOption.TakeOut, OrderType.CounterPickup, null, [new CreateOrderLine(meal.Id, 2, picks, null)])))
            .ReadAsync<KioskOrderDto>();

        // 189 + 20 (large drink) + 25 (large fries) + 15 (gravy) = 249 each, × 2
        Assert.Equal(498m, order.Order.Total);
        Assert.Equal(
            ["Sprite", "Large drink", "Large fries", "Cheese fries", "Extra gravy"],
            order.Order.Items.Single().Modifiers.Select(m => m.Name));

        // Reading it back from the database keeps that order too.
        var reloaded = await (await kiosk.GetAsync($"/api/kiosk/orders/{order.Order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(order.Order.Items.Single().Modifiers, reloaded.Order.Items.Single().Modifiers);
    }

    [Fact]
    public async Task Meal_without_its_required_answers_is_rejected()
    {
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var menu = await SeededMenuAsync(kiosk);
        var meal = menu.Categories.Single(c => c.Name == "Rice Meals").Products.Single(p => p.Name == "1-pc Chicken Meal");

        (await kiosk.OrderAsync(meal.Id)).AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sold_out_demo_item_is_listed_but_greyed()
    {
        var menu = await SeededMenuAsync(await api.DeviceAsync(DeviceKind.Kiosk));
        Assert.True(menu.Categories.Single(c => c.Name == "Sides").Products.Single(p => p.Name == "Corn Cup").IsSoldOut);
    }

    [Fact]
    public async Task Simulated_payment_endpoint_is_unreachable_outside_development()
    {
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        (await kiosk.PostAsync($"/api/dev/orders/{Guid.NewGuid()}/simulate-payment", null)).AssertStatus(HttpStatusCode.NotFound);
    }
}
