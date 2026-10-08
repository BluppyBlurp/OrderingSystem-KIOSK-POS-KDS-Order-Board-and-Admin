using FluentValidation;
using Kiosk.Domain.Menu;

namespace Kiosk.Application.Admin;

public sealed record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive, int ProductCount);

public sealed record UpsertCategoryRequest(string Name, int SortOrder, bool IsActive);

public sealed record AdminProductDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    decimal BasePrice,
    int? Stock,
    bool IsAvailable,
    int SortOrder,
    IReadOnlyList<Menu.MediaDto> Media,
    IReadOnlyList<Guid> ModifierGroupIds);

/// <summary><c>Stock</c> is used on create only; use <see cref="SetStockRequest"/> to change it later.</summary>
public sealed record UpsertProductRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    decimal BasePrice,
    int? Stock,
    bool IsAvailable,
    int SortOrder,
    IReadOnlyList<Guid>? ModifierGroupIds);

/// <summary><c>Stock = null</c> turns stock tracking off for the product.</summary>
public sealed record SetStockRequest(int? Stock);

public sealed record SetAvailabilityRequest(bool IsAvailable);

/// <summary>Ids in their new display order (drag-to-reorder).</summary>
public sealed record ReorderRequest(IReadOnlyList<Guid> Ids);

public sealed record AddMediaRequest(MediaType Type, string Url, string? ThumbnailUrl, int SortOrder);

public sealed record ModifierDto(Guid Id, string Name, decimal PriceDelta, bool IsAvailable, int SortOrder);

public sealed record ModifierGroupDto(
    Guid Id, string Name, int MinSelect, int MaxSelect, bool IsRequired, IReadOnlyList<ModifierDto> Modifiers);

public sealed record UpsertModifierGroupRequest(string Name, int MinSelect, int MaxSelect, bool IsRequired);

public sealed record UpsertModifierRequest(string Name, decimal PriceDelta, bool IsAvailable, int SortOrder);

public sealed class UpsertCategoryRequestValidator : AbstractValidator<UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
}

public sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.BasePrice).InclusiveBetween(0, 100_000).PrecisionScale(10, 2, true);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
    }
}

public sealed class SetStockRequestValidator : AbstractValidator<SetStockRequest>
{
    public SetStockRequestValidator() => RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
}

public sealed class AddMediaRequestValidator : AbstractValidator<AddMediaRequest>
{
    public AddMediaRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1000).Must(BeHttps).WithMessage("Media URL must be an absolute https URL.");
        RuleFor(x => x.ThumbnailUrl).MaximumLength(1000).Must(u => u is null || BeHttps(u));
    }

    private static bool BeHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

public sealed class UpsertModifierGroupRequestValidator : AbstractValidator<UpsertModifierGroupRequest>
{
    public UpsertModifierGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.MinSelect).InclusiveBetween(0, 20);
        RuleFor(x => x.MaxSelect).InclusiveBetween(1, 20).GreaterThanOrEqualTo(x => x.MinSelect);
    }
}

public sealed class UpsertModifierRequestValidator : AbstractValidator<UpsertModifierRequest>
{
    public UpsertModifierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PriceDelta).InclusiveBetween(-10_000, 10_000).PrecisionScale(10, 2, true);
    }
}
