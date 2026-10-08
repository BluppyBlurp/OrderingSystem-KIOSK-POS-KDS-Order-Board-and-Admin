using Kiosk.Api.Auth;
using Kiosk.Application.Admin;
using Kiosk.Application.Menu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/categories")]
[Authorize(Policy = Policies.Admin)]
public sealed class CategoriesController(AdminMenuService menu) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => menu.ListCategoriesAsync(ct);

    [HttpPost]
    public Task<CategoryDto> Create(UpsertCategoryRequest r, CancellationToken ct) => menu.CreateCategoryAsync(r, ct);

    [HttpPut("{id:guid}")]
    public Task<CategoryDto> Update(Guid id, UpsertCategoryRequest r, CancellationToken ct) => menu.UpdateCategoryAsync(id, r, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await menu.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpPut("order")]
    public async Task<IActionResult> Reorder(ReorderRequest r, CancellationToken ct)
    {
        await menu.ReorderCategoriesAsync(r, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/products")]
[Authorize(Policy = Policies.Admin)]
public sealed class ProductsController(AdminMenuService menu, MediaService media) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminProductDto>> List([FromQuery] Guid? categoryId, CancellationToken ct) =>
        menu.ListProductsAsync(categoryId, ct);

    [HttpGet("{id:guid}")]
    public Task<AdminProductDto> Get(Guid id, CancellationToken ct) => menu.GetProductAsync(id, ct);

    [HttpPost]
    public Task<AdminProductDto> Create(UpsertProductRequest r, CancellationToken ct) => menu.CreateProductAsync(r, ct);

    /// <summary>Does not change stock; use PATCH /stock.</summary>
    [HttpPut("{id:guid}")]
    public Task<AdminProductDto> Update(Guid id, UpsertProductRequest r, CancellationToken ct) => menu.UpdateProductAsync(id, r, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await menu.DeleteProductAsync(id, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/stock")]
    public Task<AdminProductDto> SetStock(Guid id, SetStockRequest r, CancellationToken ct) => menu.SetStockAsync(id, r, ct);

    [HttpPatch("{id:guid}/availability")]
    public Task<AdminProductDto> SetAvailability(Guid id, SetAvailabilityRequest r, CancellationToken ct) =>
        menu.SetAvailabilityAsync(id, r, ct);

    [HttpPut("order")]
    public async Task<IActionResult> Reorder(ReorderRequest r, CancellationToken ct)
    {
        await menu.ReorderProductsAsync(r, ct);
        return NoContent();
    }

    /// <summary>Step 2 of an upload (step 1: POST /api/admin/media/presign): checks the file, writes the variants, attaches it.</summary>
    [HttpPost("{id:guid}/media/uploaded")]
    public Task<MediaDto> CompleteUpload(Guid id, CompleteMediaUploadRequest r, CancellationToken ct) =>
        media.CompleteUploadAsync(id, r, ct);

    /// <summary>Registers media that is already hosted elsewhere, by https URL.</summary>
    [HttpPost("{id:guid}/media")]
    public Task<MediaDto> AddMedia(Guid id, AddMediaRequest r, CancellationToken ct) => menu.AddMediaAsync(id, r, ct);

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> DeleteMedia(Guid id, Guid mediaId, CancellationToken ct)
    {
        await menu.DeleteMediaAsync(id, mediaId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/modifier-groups")]
[Authorize(Policy = Policies.Admin)]
public sealed class ModifierGroupsController(AdminMenuService menu) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ModifierGroupDto>> List(CancellationToken ct) => menu.ListModifierGroupsAsync(ct);

    [HttpPost]
    public Task<ModifierGroupDto> Create(UpsertModifierGroupRequest r, CancellationToken ct) => menu.CreateModifierGroupAsync(r, ct);

    [HttpPut("{id:guid}")]
    public Task<ModifierGroupDto> Update(Guid id, UpsertModifierGroupRequest r, CancellationToken ct) =>
        menu.UpdateModifierGroupAsync(id, r, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await menu.DeleteModifierGroupAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{groupId:guid}/modifiers")]
    public Task<ModifierDto> CreateModifier(Guid groupId, UpsertModifierRequest r, CancellationToken ct) =>
        menu.CreateModifierAsync(groupId, r, ct);

    [HttpPut("{groupId:guid}/modifiers/{id:guid}")]
    public Task<ModifierDto> UpdateModifier(Guid groupId, Guid id, UpsertModifierRequest r, CancellationToken ct) =>
        menu.UpdateModifierAsync(groupId, id, r, ct);

    [HttpDelete("{groupId:guid}/modifiers/{id:guid}")]
    public async Task<IActionResult> DeleteModifier(Guid groupId, Guid id, CancellationToken ct)
    {
        await menu.DeleteModifierAsync(groupId, id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/media")]
[Authorize(Policy = Policies.Admin)]
public sealed class MediaController(MediaService media) : ControllerBase
{
    /// <summary>Step 1 of an upload: a short-lived URL the browser PUTs the file to, straight into R2.</summary>
    [HttpPost("presign")]
    public Task<PresignedUploadDto> Presign(PresignMediaRequest r, CancellationToken ct) => media.PresignAsync(r, ct);
}
