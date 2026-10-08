using Kiosk.Application.Devices;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Infrastructure.Persistence;

/// <summary>
/// A sample menu so the kiosk has something to show, and optionally one kiosk device with a known token so the
/// kiosk can sign in without the Admin app. On by default in Development (Dev:*); opt-in elsewhere (Bootstrap:*)
/// until the Admin app exists. Idempotent: the menu is added once, the device only if its token is new.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool seedMenu, string? kioskToken, string kioskName = "Dev Kiosk",
        CancellationToken ct = default)
    {
        await SeedDeviceAsync(db, kioskToken, kioskName, ct);
        if (!seedMenu || await db.Categories.AnyAsync(c => c.Name == "Rice Meals", ct))
            return;

        // ---- Reusable questions (each modifier group is one screen on the kiosk) ----
        var drinkChoice = Group("Choose your drink", min: 1, max: 1, required: true,
            ("Coke", 0), ("Coke Zero", 0), ("Sprite", 0), ("Royal", 0), ("Iced Tea", 0), ("Pineapple Juice", 10));
        var upsizeDrink = Group("Upsize your drink?", min: 1, max: 1, required: true,
            ("Regular drink", 0), ("Large drink", 20));
        var upsizeFries = Group("Upsize your fries?", min: 1, max: 1, required: true,
            ("Regular fries", 0), ("Large fries", 25));
        var friesFlavor = Group("Fries flavor", min: 1, max: 1, required: true,
            ("Plain fries", 0), ("Cheese fries", 0), ("Sour cream fries", 0), ("Barbecue fries", 0));
        var addOns = Group("Anything to add?", min: 0, max: 6, required: false,
            ("Extra gravy", 15), ("Extra rice", 25), ("Coleslaw", 35), ("Mashed potato", 45), ("Sundae", 45), ("Apple pie", 45));
        var addDrink = Group("Add a drink?", min: 0, max: 1, required: false,
            ("Coke", 45), ("Sprite", 45), ("Iced Tea", 45), ("Pineapple Juice", 55));
        var addSide = Group("Add a side?", min: 0, max: 2, required: false,
            ("Regular fries", 45), ("Large fries", 65), ("Coleslaw", 35), ("Mashed potato", 45), ("Corn cup", 35));
        var drinkSize = Group("Size", min: 1, max: 1, required: true, ("Regular", 0), ("Large", 20));
        var sundaeFlavor = Group("Sundae flavor", min: 1, max: 1, required: true,
            ("Chocolate", 0), ("Caramel", 0), ("Strawberry", 0));

        ModifierGroup[] meal = [drinkChoice, upsizeDrink, upsizeFries, friesFlavor, addOns];
        ModifierGroup[] alaCarte = [addDrink, addSide, addOns];

        // ---- Categories and products (prices are VAT-inclusive pesos) ----
        var categories = new[]
        {
            Category("Rice Meals", 0,
                Product("1-pc Chicken Meal", "Chicken, rice, drink and fries", 189, meal),
                Product("1-pc Chicken with Rice", "Chicken and rice only", 129, alaCarte),
                Product("2-pc Chicken Meal", "Two chicken, rice, drink and fries", 289, meal),
                Product("Burger Steak Meal", "Burger steak, rice, drink and fries", 159, meal),
                Product("Burger Steak with Rice", "Burger steak and rice only", 99, alaCarte)),
            Category("Sandwiches", 1,
                Product("Cheeseburger Meal", "Cheeseburger, drink and fries", 149, meal),
                Product("Cheeseburger", "Burger only", 79, alaCarte),
                Product("Chicken Sandwich", "Sandwich only", 119, alaCarte)),
            Category("Pasta", 2,
                Product("Spaghetti Meal", "Spaghetti, drink and fries", 139, meal),
                Product("Spaghetti", "Spaghetti only", 79, alaCarte),
                Product("Carbonara", "Carbonara only — limited daily", 99, alaCarte, stock: 10)),
            Category("Sides", 3,
                Product("Fries", null, 55, [upsizeFries, friesFlavor]),
                Product("Coleslaw", null, 35, []),
                Product("Mashed Potato", null, 45, []),
                Product("Corn Cup", "Sold out today (demo)", 35, [], stock: 0)),
            Category("Drinks", 4,
                Product("Coke", null, 45, [drinkSize]),
                Product("Sprite", null, 45, [drinkSize]),
                Product("Iced Tea", null, 45, [drinkSize]),
                Product("Pineapple Juice", null, 55, [drinkSize]),
                Product("Bottled Water", null, 30, [])),
            Category("Desserts", 5,
                Product("Sundae", null, 45, [sundaeFlavor]),
                Product("Apple Pie", null, 45, []),
                Product("Coke Float", null, 55, [])),
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDeviceAsync(AppDbContext db, string? token, string name, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
            return;
        var hash = DeviceService.Hash(token);
        if (await db.Devices.AnyAsync(d => d.TokenHash == hash, ct))
            return;
        db.Devices.Add(new Device { Name = name, Kind = DeviceKind.Kiosk, TokenHash = hash, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    private static ModifierGroup Group(string name, int min, int max, bool required, params (string Name, decimal Delta)[] options)
    {
        var group = new ModifierGroup { Name = name, MinSelect = min, MaxSelect = max, IsRequired = required };
        group.Modifiers.AddRange(options.Select((o, i) => new Modifier
        {
            ModifierGroupId = group.Id, Name = o.Name, PriceDelta = o.Delta, SortOrder = i,
        }));
        return group;
    }

    private static Category Category(string name, int sortOrder, params Product[] products)
    {
        var category = new Category { Name = name, SortOrder = sortOrder };
        for (var i = 0; i < products.Length; i++)
        {
            products[i].CategoryId = category.Id;
            products[i].SortOrder = i;
        }
        category.Products.AddRange(products);
        return category;
    }

    private static Product Product(string name, string? description, decimal price, ModifierGroup[] groups, int? stock = null)
    {
        var product = new Product { Name = name, Description = description, BasePrice = price, Stock = stock };
        product.ModifierGroups.AddRange(groups.Select((g, i) => new ProductModifierGroup
        {
            ProductId = product.Id, ModifierGroupId = g.Id, ModifierGroup = g, SortOrder = i,
        }));
        return product;
    }
}
