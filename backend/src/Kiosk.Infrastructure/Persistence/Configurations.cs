using Kiosk.Domain.Auditing;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kiosk.Infrastructure.Persistence;

internal sealed class CategoryConfig : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.HasMany(x => x.Products).WithOne(p => p.Category).HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Ignore(x => x.IsSoldOut);
        b.HasMany(x => x.Media).WithOne().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.ModifierGroups).WithOne().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("CK_Products_Stock", "\"Stock\" IS NULL OR \"Stock\" >= 0"));
    }
}

internal sealed class ProductMediaConfig : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> b)
    {
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
    }
}

internal sealed class ModifierGroupConfig : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.Ignore(x => x.EffectiveMin);
        b.HasMany(x => x.Modifiers).WithOne(m => m.ModifierGroup).HasForeignKey(m => m.ModifierGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ModifierConfig : IEntityTypeConfiguration<Modifier>
{
    public void Configure(EntityTypeBuilder<Modifier> b) => b.Property(x => x.Name).HasMaxLength(80);
}

internal sealed class ProductModifierGroupConfig : IEntityTypeConfiguration<ProductModifierGroup>
{
    public void Configure(EntityTypeBuilder<ProductModifierGroup> b)
    {
        b.HasKey(x => new { x.ProductId, x.ModifierGroupId });
        b.HasOne(x => x.ModifierGroup).WithMany().HasForeignKey(x => x.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderConfig : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.OrderNumber).HasMaxLength(20);
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.Version).IsRowVersion(); // Postgres xmin
        b.Ignore(x => x.IsPrePaid);

        b.HasIndex(x => new { x.BusinessDate, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasIndex(x => x.TableNumber);

        // OrderItem has no FK to Product on purpose: items are snapshots and must outlive menu edits.
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class OrderItemConfig : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.Property(x => x.NameSnapshot).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(200);
        b.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.OrderItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderItemModifierConfig : IEntityTypeConfiguration<OrderItemModifier>
{
    public void Configure(EntityTypeBuilder<OrderItemModifier> b) => b.Property(x => x.NameSnapshot).HasMaxLength(80);
}

internal sealed class OrderEventConfig : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> b)
    {
        b.Property(x => x.ActorId).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => x.RefundNeeded).HasFilter("\"RefundNeeded\"");
    }
}

internal sealed class PaymentConfig : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Provider).HasMaxLength(30);
        b.Property(x => x.ProviderRef).HasMaxLength(100);
        b.Property(x => x.CheckoutUrl).HasMaxLength(1000);
        b.Property(x => x.ProcessedByUserId).HasMaxLength(100);
        b.HasIndex(x => new { x.Provider, x.ProviderRef }).IsUnique().HasFilter("\"ProviderRef\" IS NOT NULL");
    }
}

internal sealed class ProcessedWebhookEventConfig : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(100);
        b.Property(x => x.Provider).HasMaxLength(30);
        b.Property(x => x.EventType).HasMaxLength(100);
    }
}

internal sealed class DeviceConfig : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.ActorId).HasMaxLength(100);
        b.Property(x => x.EntityType).HasMaxLength(60);
        b.Property(x => x.EntityId).HasMaxLength(60);
        b.Property(x => x.Action).HasMaxLength(20);
        b.Property(x => x.Before).HasColumnType("jsonb");
        b.Property(x => x.After).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class DailyOrderCounterConfig : IEntityTypeConfiguration<DailyOrderCounter>
{
    public void Configure(EntityTypeBuilder<DailyOrderCounter> b) => b.HasKey(x => x.BusinessDate);
}
