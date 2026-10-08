using Kiosk.Domain.Common;

namespace Kiosk.Domain.Menu;

/// <summary>"Pick a size", "Add-ons", … Drives how many modifiers a customer may pick.</summary>
public class ModifierGroup : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int MinSelect { get; set; }
    public int MaxSelect { get; set; } = 1;
    public bool IsRequired { get; set; }
    public List<Modifier> Modifiers { get; set; } = [];

    /// <summary>Required groups need at least one pick even if <see cref="MinSelect"/> is 0.</summary>
    public int EffectiveMin => IsRequired ? Math.Max(1, MinSelect) : MinSelect;
}

public class Modifier : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModifierGroupId { get; set; }
    public ModifierGroup? ModifierGroup { get; set; }
    public required string Name { get; set; }
    public decimal PriceDelta { get; set; }
    public bool IsAvailable { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ProductModifierGroup
{
    public Guid ProductId { get; set; }
    public Guid ModifierGroupId { get; set; }
    public ModifierGroup? ModifierGroup { get; set; }
    public int SortOrder { get; set; }
}
