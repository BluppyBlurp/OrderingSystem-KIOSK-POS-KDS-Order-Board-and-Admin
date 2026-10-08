using Kiosk.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kiosk.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only. Generating migrations does not connect to the database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KIOSK_DB")
                               ?? "Host=localhost;Port=5433;Database=kiosk;Username=kiosk;Password=kiosk";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options, new DesignTimeActor());
    }

    private sealed class DesignTimeActor : ICurrentActor
    {
        public string? ActorId => "design-time";
    }
}
