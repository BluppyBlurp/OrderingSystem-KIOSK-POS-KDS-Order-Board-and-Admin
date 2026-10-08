using Kiosk.Domain.Common;

namespace Kiosk.Domain.Menu;

public class Product : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }

    /// <summary>Units on hand. <c>null</c> means the product is not stock-tracked.</summary>
    public int? Stock { get; set; }

    public bool IsAvailable { get; set; } = true;
    public int SortOrder { get; set; }
    public List<ProductMedia> Media { get; set; } = [];
    public List<ProductModifierGroup> ModifierGroups { get; set; } = [];

    public bool IsSoldOut => !IsAvailable || Stock is <= 0;
}
