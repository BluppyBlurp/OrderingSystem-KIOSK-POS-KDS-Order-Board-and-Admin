using Kiosk.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kiosk.Infrastructure.Persistence;

/// <summary>
/// One atomic upsert per order. The counter row stays locked until the caller's transaction commits,
/// so two kiosks can never get the same number, and a rolled-back order does not burn one.
/// A new business date simply starts a new row, so no reset job is needed.
/// </summary>
internal sealed class PostgresOrderNumberGenerator(AppDbContext db) : IOrderNumberGenerator
{
    public async Task<int> NextAsync(DateOnly businessDate, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction
                 ?? throw new InvalidOperationException("Order numbers must be generated inside the order's transaction.");

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = tx.GetDbTransaction();
        cmd.CommandText = """
            INSERT INTO "DailyOrderCounters" ("BusinessDate", "LastValue") VALUES (@date, 1)
            ON CONFLICT ("BusinessDate") DO UPDATE SET "LastValue" = "DailyOrderCounters"."LastValue" + 1
            RETURNING "LastValue";
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "date";
        p.Value = businessDate;
        cmd.Parameters.Add(p);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}
