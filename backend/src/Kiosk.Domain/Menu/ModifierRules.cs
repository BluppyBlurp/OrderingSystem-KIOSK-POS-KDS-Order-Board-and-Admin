using Kiosk.Domain.Common;

namespace Kiosk.Domain.Menu;

public static class ModifierRules
{
    /// <summary>
    /// Validates a customer's modifier picks against the product's groups (offered, available, min/max/required)
    /// and returns the chosen modifiers in question order (group order, then option order).
    /// Requires the product's groups and their modifiers to be loaded.
    /// </summary>
    public static IReadOnlyList<Modifier> Resolve(Product product, IReadOnlyCollection<Guid> selectedIds)
    {
        if (selectedIds.Distinct().Count() != selectedIds.Count)
            throw new DomainException("duplicate_modifier", $"{product.Name}: a modifier was selected twice.");

        var groups = product.ModifierGroups.OrderBy(pmg => pmg.SortOrder).Select(pmg => pmg.ModifierGroup!).ToList();
        var offered = groups.SelectMany(g => g.Modifiers).ToDictionary(m => m.Id);

        var chosen = new List<Modifier>(selectedIds.Count);
        foreach (var id in selectedIds)
        {
            if (!offered.TryGetValue(id, out var modifier))
                throw new DomainException("invalid_modifier", $"{product.Name}: that option is not offered.");
            if (!modifier.IsAvailable)
                throw new DomainException("modifier_unavailable", $"{product.Name}: {modifier.Name} is not available.");
            chosen.Add(modifier);
        }

        foreach (var group in groups)
        {
            var count = chosen.Count(m => m.ModifierGroupId == group.Id);
            if (count < group.EffectiveMin)
                throw new DomainException("modifier_min", $"{product.Name}: pick at least {group.EffectiveMin} from {group.Name}.");
            if (count > group.MaxSelect)
                throw new DomainException("modifier_max", $"{product.Name}: pick at most {group.MaxSelect} from {group.Name}.");
        }

        return chosen
            .OrderBy(m => groups.FindIndex(g => g.Id == m.ModifierGroupId))
            .ThenBy(m => m.SortOrder)
            .ToList();
    }
}
