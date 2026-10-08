using Kiosk.Application.Abstractions;
using Kiosk.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Menu;

public sealed record MediaDto(Guid Id, MediaType Type, string Url, string? ThumbnailUrl);

public sealed record MenuModifierDto(Guid Id, string Name, decimal PriceDelta, bool IsAvailable);

public sealed record MenuModifierGroupDto(
    Guid Id, string Name, int MinSelect, int MaxSelect, bool IsRequired, IReadOnlyList<MenuModifierDto> Modifiers);

/// <summary>Sold-out products stay in the list (greyed out in place), they are not removed.</summary>
public sealed record MenuProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    bool IsSoldOut,
    IReadOnlyList<MediaDto> Media,
    IReadOnlyList<MenuModifierGroupDto> ModifierGroups);

public sealed record MenuCategoryDto(Guid Id, string Name, IReadOnlyList<MenuProductDto> Products);

public sealed record MenuDto(IReadOnlyList<MenuCategoryDto> Categories);

public sealed class MenuQueryService(IAppDbContext db)
{
    public async Task<MenuDto> GetKioskMenuAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Include(c => c.Products).ThenInclude(p => p.Media)
            .Include(c => c.Products).ThenInclude(p => p.ModifierGroups).ThenInclude(pmg => pmg.ModifierGroup!).ThenInclude(g => g.Modifiers)
            .ToListAsync(ct);

        return new MenuDto(categories.Select(c => new MenuCategoryDto(
            c.Id,
            c.Name,
            c.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).Select(ToDto).ToList())).ToList());
    }

    private static MenuProductDto ToDto(Product p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.BasePrice,
        p.IsSoldOut,
        p.Media.OrderBy(m => m.SortOrder).Select(m => new MediaDto(m.Id, m.Type, m.Url, m.ThumbnailUrl)).ToList(),
        p.ModifierGroups.OrderBy(pmg => pmg.SortOrder).Select(pmg => pmg.ModifierGroup!).Select(g => new MenuModifierGroupDto(
            g.Id, g.Name, g.EffectiveMin, g.MaxSelect, g.IsRequired,
            g.Modifiers.OrderBy(m => m.SortOrder).Select(m => new MenuModifierDto(m.Id, m.Name, m.PriceDelta, m.IsAvailable)).ToList())).ToList());
}
