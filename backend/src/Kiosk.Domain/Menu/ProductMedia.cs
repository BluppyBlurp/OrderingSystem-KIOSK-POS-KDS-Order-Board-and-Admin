using Kiosk.Domain.Common;

namespace Kiosk.Domain.Menu;

public enum MediaType { Image, Video }

public class ProductMedia : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public MediaType Type { get; set; }
    public required string Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int SortOrder { get; set; }
}
