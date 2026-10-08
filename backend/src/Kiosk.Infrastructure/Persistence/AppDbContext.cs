using System.Text.Json;
using Kiosk.Application.Abstractions;
using Kiosk.Domain.Auditing;
using Kiosk.Domain.Common;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Kiosk.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentActor actor)
    : DbContext(options), IAppDbContext
{
    /// <summary>Never copied into the audit log.</summary>
    private static readonly HashSet<string> SecretProperties = [nameof(Device.TokenHash)];

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<ProductModifierGroup> ProductModifierGroups => Set<ProductModifierGroup>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    internal DbSet<DailyOrderCounter> DailyOrderCounters => Set<DailyOrderCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Entities assign their own Guid ids in code. Without this, EF assumes a new child with a non-empty
        // generated key (e.g. a Payment added to a loaded Order) already exists, and UPDATEs instead of INSERTing.
        foreach (var key in modelBuilder.Model.GetEntityTypes().Select(e => e.FindPrimaryKey()))
        {
            if (key is { Properties: [{ ClrType: var type } property] } && type == typeof(Guid))
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(10, 2);
        builder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<DiningOption>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<OrderType>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<DeviceKind>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<MediaType>().HaveConversion<string>().HaveMaxLength(20);
    }

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        WriteAuditEntries();
        return await base.SaveChangesAsync(ct);
    }

    private void WriteAuditEntries()
    {
        ChangeTracker.DetectChanges();
        var now = DateTimeOffset.UtcNow;
        var entries = ChangeTracker.Entries<IAuditable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var changed = entry.Properties
                .Where(p => !SecretProperties.Contains(p.Metadata.Name))
                .Where(p => entry.State != EntityState.Modified || p.IsModified)
                .ToList();
            if (entry.State == EntityState.Modified && changed.Count == 0)
                continue;

            AuditLogs.Add(new AuditLog
            {
                At = now,
                ActorId = actor.ActorId,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id.ToString(),
                Action = entry.State.ToString(),
                Before = entry.State == EntityState.Added ? null : Serialize(changed, p => p.OriginalValue),
                After = entry.State == EntityState.Deleted ? null : Serialize(changed, p => p.CurrentValue),
            });
        }
    }

    private static string Serialize(IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> value) =>
        JsonSerializer.Serialize(properties.ToDictionary(p => p.Metadata.Name, value));
}

/// <summary>One row per business day; the order number generator increments it atomically.</summary>
internal sealed class DailyOrderCounter
{
    public DateOnly BusinessDate { get; set; }
    public int LastValue { get; set; }
}
