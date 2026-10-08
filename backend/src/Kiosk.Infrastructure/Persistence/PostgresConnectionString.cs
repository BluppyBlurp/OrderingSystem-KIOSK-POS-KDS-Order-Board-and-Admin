using Npgsql;

namespace Kiosk.Infrastructure.Persistence;

/// <summary>
/// Accepts both connection string styles: Npgsql's "Host=…;Database=…" and the URL style that Neon's dashboard
/// (and most hosts) hand out, "postgresql://user:password@host/db?sslmode=require&amp;channel_binding=require".
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalize(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            // Never echo the value: it contains the password.
            throw new InvalidOperationException("ConnectionStrings:Default looks like a postgresql:// URL but could not be parsed.");

        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var setting = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]).Replace("-", "") : "";
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(setting, ignoreCase: true); // require, verify-full, …
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(setting, ignoreCase: true);
                    break;
                // Other libpq options (e.g. options=endpoint%3D…) aren't needed by Neon's current hostnames.
            }
        }

        return builder.ConnectionString;
    }
}
