using Kiosk.Domain.Auditing;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kiosk.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductMedia> ProductMedia { get; }
    DbSet<ModifierGroup> ModifierGroups { get; }
    DbSet<Modifier> Modifiers { get; }
    DbSet<ProductModifierGroup> ProductModifierGroups { get; }
    DbSet<Order> Orders { get; }
    DbSet<Payment> Payments { get; }
    DbSet<OrderEvent> OrderEvents { get; }
    DbSet<Device> Devices { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>True when the save failed on a unique constraint (e.g. a duplicate webhook event id).</summary>
    bool IsUniqueViolation(DbUpdateException exception);
}
