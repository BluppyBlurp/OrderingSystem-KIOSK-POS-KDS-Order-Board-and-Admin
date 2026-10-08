using FluentValidation;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Application.Menu;
using Kiosk.Domain.Common;
using Kiosk.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.Application.Admin;

/// <summary>
/// Menu back office. Every save is audited by the DbContext, and every change pushes MenuChanged
/// so kiosks update without a redeploy.
/// </summary>
public sealed class AdminMenuService(IAppDbContext db, IOrderNotifier notifier, IServiceProvider services)
{
    // ---------- Categories ----------

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive, c.Products.Count))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateCategoryAsync(UpsertCategoryRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var category = new Category { Name = r.Name.Trim(), SortOrder = r.SortOrder, IsActive = r.IsActive };
        db.Categories.Add(category);
        await SaveMenuAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive, 0);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpsertCategoryRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var category = await Find(db.Categories, id, ct);
        category.Name = r.Name.Trim();
        category.SortOrder = r.SortOrder;
        category.IsActive = r.IsActive;
        await SaveMenuAsync(ct);
        var count = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        return new CategoryDto(category.Id, category.Name, category.SortOrder, category.IsActive, count);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var category = await Find(db.Categories, id, ct);
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
            throw new DomainException("category_not_empty", "Move or delete this category's products first.");
        db.Categories.Remove(category);
        await SaveMenuAsync(ct);
    }

    public async Task ReorderCategoriesAsync(ReorderRequest r, CancellationToken ct)
    {
        var categories = await db.Categories.Where(c => r.Ids.Contains(c.Id)).ToListAsync(ct);
        ApplyOrder(categories, r.Ids, c => c.Id, (c, i) => c.SortOrder = i);
        await SaveMenuAsync(ct);
    }

    // ---------- Products ----------

    public async Task<IReadOnlyList<AdminProductDto>> ListProductsAsync(Guid? categoryId, CancellationToken ct)
    {
        var products = await ProductsWithDetails()
            .Where(p => categoryId == null || p.CategoryId == categoryId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .ToListAsync(ct);
        return products.Select(ToDto).ToList();
    }

    public async Task<AdminProductDto> GetProductAsync(Guid id, CancellationToken ct) =>
        ToDto(await ProductsWithDetails().FirstOrDefaultAsync(p => p.Id == id, ct)
              ?? throw new NotFoundException("Product not found."));

    public async Task<AdminProductDto> CreateProductAsync(UpsertProductRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        await EnsureCategoryExists(r.CategoryId, ct);
        var product = new Product { Name = r.Name.Trim(), Stock = r.Stock };
        Apply(product, r);
        await SetModifierGroupsAsync(product, r.ModifierGroupIds, ct);
        db.Products.Add(product);
        await SaveMenuAsync(ct);
        return await GetProductAsync(product.Id, ct);
    }

    /// <summary>
    /// Does not touch stock: a form opened minutes ago would overwrite reservations made since.
    /// Stock changes go through <see cref="SetStockAsync"/>.
    /// </summary>
    public async Task<AdminProductDto> UpdateProductAsync(Guid id, UpsertProductRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        await EnsureCategoryExists(r.CategoryId, ct);
        var product = await db.Products.Include(p => p.ModifierGroups).FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Product not found.");
        Apply(product, r);
        await SetModifierGroupsAsync(product, r.ModifierGroupIds, ct);
        await SaveMenuAsync(ct);
        return await GetProductAsync(id, ct);
    }

    /// <summary>Past orders keep their name and price snapshots, so deleting a product never changes history.</summary>
    public async Task DeleteProductAsync(Guid id, CancellationToken ct)
    {
        db.Products.Remove(await Find(db.Products, id, ct));
        await SaveMenuAsync(ct);
    }

    public async Task<AdminProductDto> SetStockAsync(Guid id, SetStockRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var product = await Find(db.Products, id, ct);
        product.Stock = r.Stock;
        await SaveMenuAsync(ct);
        return await GetProductAsync(id, ct);
    }

    public async Task<AdminProductDto> SetAvailabilityAsync(Guid id, SetAvailabilityRequest r, CancellationToken ct)
    {
        var product = await Find(db.Products, id, ct);
        product.IsAvailable = r.IsAvailable;
        await SaveMenuAsync(ct);
        return await GetProductAsync(id, ct);
    }

    public async Task ReorderProductsAsync(ReorderRequest r, CancellationToken ct)
    {
        var products = await db.Products.Where(p => r.Ids.Contains(p.Id)).ToListAsync(ct);
        ApplyOrder(products, r.Ids, p => p.Id, (p, i) => p.SortOrder = i);
        await SaveMenuAsync(ct);
    }

    public async Task<MediaDto> AddMediaAsync(Guid productId, AddMediaRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        await Find(db.Products, productId, ct);
        var media = new ProductMedia
        {
            ProductId = productId, Type = r.Type, Url = r.Url, ThumbnailUrl = r.ThumbnailUrl, SortOrder = r.SortOrder,
        };
        db.ProductMedia.Add(media);
        await SaveMenuAsync(ct);
        return new MediaDto(media.Id, media.Type, media.Url, media.ThumbnailUrl);
    }

    public async Task DeleteMediaAsync(Guid productId, Guid mediaId, CancellationToken ct)
    {
        var media = await db.ProductMedia.FirstOrDefaultAsync(m => m.Id == mediaId && m.ProductId == productId, ct)
                    ?? throw new NotFoundException("Media not found.");
        db.ProductMedia.Remove(media);
        await SaveMenuAsync(ct);
    }

    // ---------- Modifier groups & modifiers ----------

    public async Task<IReadOnlyList<ModifierGroupDto>> ListModifierGroupsAsync(CancellationToken ct)
    {
        var groups = await db.ModifierGroups.AsNoTracking().Include(g => g.Modifiers).OrderBy(g => g.Name).ToListAsync(ct);
        return groups.Select(ToDto).ToList();
    }

    public async Task<ModifierGroupDto> CreateModifierGroupAsync(UpsertModifierGroupRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var group = new ModifierGroup { Name = r.Name.Trim() };
        Apply(group, r);
        db.ModifierGroups.Add(group);
        await SaveMenuAsync(ct);
        return ToDto(group);
    }

    public async Task<ModifierGroupDto> UpdateModifierGroupAsync(Guid id, UpsertModifierGroupRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var group = await db.ModifierGroups.Include(g => g.Modifiers).FirstOrDefaultAsync(g => g.Id == id, ct)
                    ?? throw new NotFoundException("Modifier group not found.");
        Apply(group, r);
        await SaveMenuAsync(ct);
        return ToDto(group);
    }

    public async Task DeleteModifierGroupAsync(Guid id, CancellationToken ct)
    {
        db.ModifierGroups.Remove(await Find(db.ModifierGroups, id, ct));
        await SaveMenuAsync(ct);
    }

    public async Task<ModifierDto> CreateModifierAsync(Guid groupId, UpsertModifierRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        await Find(db.ModifierGroups, groupId, ct);
        var modifier = new Modifier { ModifierGroupId = groupId, Name = r.Name.Trim() };
        Apply(modifier, r);
        db.Modifiers.Add(modifier);
        await SaveMenuAsync(ct);
        return ToDto(modifier);
    }

    public async Task<ModifierDto> UpdateModifierAsync(Guid groupId, Guid id, UpsertModifierRequest r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        var modifier = await db.Modifiers.FirstOrDefaultAsync(m => m.Id == id && m.ModifierGroupId == groupId, ct)
                       ?? throw new NotFoundException("Modifier not found.");
        Apply(modifier, r);
        await SaveMenuAsync(ct);
        return ToDto(modifier);
    }

    public async Task DeleteModifierAsync(Guid groupId, Guid id, CancellationToken ct)
    {
        var modifier = await db.Modifiers.FirstOrDefaultAsync(m => m.Id == id && m.ModifierGroupId == groupId, ct)
                       ?? throw new NotFoundException("Modifier not found.");
        db.Modifiers.Remove(modifier);
        await SaveMenuAsync(ct);
    }

    // ---------- Helpers ----------

    private IQueryable<Product> ProductsWithDetails() =>
        db.Products.AsNoTracking().Include(p => p.Media).Include(p => p.ModifierGroups);

    private async Task SaveMenuAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await notifier.MenuChangedAsync(ct);
    }

    private Task ValidateAsync<T>(T request, CancellationToken ct) =>
        services.GetRequiredService<IValidator<T>>().ValidateAndThrowAsync(request, ct);

    private static async Task<T> Find<T>(DbSet<T> set, Guid id, CancellationToken ct) where T : class =>
        await set.FindAsync([id], ct) ?? throw new NotFoundException($"{typeof(T).Name} not found.");

    private async Task EnsureCategoryExists(Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
            throw new NotFoundException("Category not found.");
    }

    private async Task SetModifierGroupsAsync(Product product, IReadOnlyList<Guid>? groupIds, CancellationToken ct)
    {
        groupIds ??= [];
        var distinct = groupIds.Distinct().ToList();
        var found = await db.ModifierGroups.CountAsync(g => distinct.Contains(g.Id), ct);
        if (found != distinct.Count)
            throw new NotFoundException("One or more modifier groups were not found.");

        // Diff rather than clear-and-re-add: re-adding the same composite key would clash in the change tracker.
        product.ModifierGroups.RemoveAll(link => !distinct.Contains(link.ModifierGroupId));
        for (var i = 0; i < distinct.Count; i++)
        {
            var link = product.ModifierGroups.FirstOrDefault(l => l.ModifierGroupId == distinct[i]);
            if (link is null)
                product.ModifierGroups.Add(new ProductModifierGroup { ProductId = product.Id, ModifierGroupId = distinct[i], SortOrder = i });
            else
                link.SortOrder = i;
        }
    }

    private static void ApplyOrder<T>(List<T> items, IReadOnlyList<Guid> ids, Func<T, Guid> key, Action<T, int> set)
    {
        var position = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        foreach (var item in items)
            set(item, position[key(item)]);
    }

    private static void Apply(Product p, UpsertProductRequest r)
    {
        p.CategoryId = r.CategoryId;
        p.Name = r.Name.Trim();
        p.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        p.BasePrice = r.BasePrice;
        p.IsAvailable = r.IsAvailable;
        p.SortOrder = r.SortOrder;
    }

    private static void Apply(ModifierGroup g, UpsertModifierGroupRequest r)
    {
        g.Name = r.Name.Trim();
        g.MinSelect = r.MinSelect;
        g.MaxSelect = r.MaxSelect;
        g.IsRequired = r.IsRequired;
    }

    private static void Apply(Modifier m, UpsertModifierRequest r)
    {
        m.Name = r.Name.Trim();
        m.PriceDelta = r.PriceDelta;
        m.IsAvailable = r.IsAvailable;
        m.SortOrder = r.SortOrder;
    }

    private static AdminProductDto ToDto(Product p) => new(
        p.Id, p.CategoryId, p.Name, p.Description, p.BasePrice, p.Stock, p.IsAvailable, p.SortOrder,
        p.Media.OrderBy(m => m.SortOrder).Select(m => new MediaDto(m.Id, m.Type, m.Url, m.ThumbnailUrl)).ToList(),
        p.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => g.ModifierGroupId).ToList());

    private static ModifierGroupDto ToDto(ModifierGroup g) => new(
        g.Id, g.Name, g.MinSelect, g.MaxSelect, g.IsRequired,
        g.Modifiers.OrderBy(m => m.SortOrder).Select(ToDto).ToList());

    private static ModifierDto ToDto(Modifier m) => new(m.Id, m.Name, m.PriceDelta, m.IsAvailable, m.SortOrder);
}
