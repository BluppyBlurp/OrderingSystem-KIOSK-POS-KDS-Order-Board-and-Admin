using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiosk.Api.Auth;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Staff;
using Kiosk.Application.Devices;
using Kiosk.Domain.Devices;
using Kiosk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Kiosk.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway database on a local Postgres (see scripts/dev-db.ps1).
/// Override the server with KIOSK_TEST_DB, e.g. "Host=localhost;Port=5433;Username=kiosk;Password=kiosk".
/// Clerk is replaced by a local signing key; everything else (auth policies, EF, SQL) is the production code path.
/// </summary>
public sealed class KioskApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://clerk.test";
    public const string WebhookSecret = "whsk_integration_tests";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("integration-tests-signing-key-32-bytes-min!!"));

    private readonly string _server =
        Environment.GetEnvironmentVariable("KIOSK_TEST_DB") ?? "Host=localhost;Port=5433;Username=kiosk;Password=kiosk";
    private readonly string _database = $"kiosk_test_{Guid.NewGuid():N}";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public FakeMediaStorage Media { get; } = new();

    public FakeStaffDirectory StaffDirectory { get; } = new();

    private string ConnectionString => new NpgsqlConnectionStringBuilder(_server) { Database = _database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_database}\"");
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Jobs:OrderExpiry", "false");
        builder.UseSetting("Slip:SigningKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()));
        builder.UseSetting("PayMongo:UseStub", "true");
        builder.UseSetting("PayMongo:WebhookSecret", WebhookSecret);
        builder.UseSetting("Cors:Origins:0", "https://admin.example.com");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IMediaStorage>(Media);
            services.AddScoped<IStaffDirectory>(_ => StaffDirectory);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Authority = null;
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });
        });
    }

    /// <summary>A client signed in as a Clerk staff user with the given role.</summary>
    public HttpClient Staff(string role, string userId = "user_test")
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Subject = new ClaimsIdentity([new Claim(Claims.Subject, $"{userId}_{role}"), new Claim(Claims.Role, role)]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
        return WithBearer(token);
    }

    /// <summary>Registers a device through the admin API (the real flow) and returns a client using its token.</summary>
    public async Task<HttpClient> DeviceAsync(DeviceKind kind)
    {
        var response = await Staff(Roles.Admin).PostAsJsonAsync("/api/admin/devices",
            new RegisterDeviceRequest($"{kind} {Guid.NewGuid():N}", kind), Json);
        response.EnsureSuccessStatusCode();
        var registered = await response.Content.ReadFromJsonAsync<RegisteredDeviceDto>(Json);
        return WithBearer(registered!.Token);
    }

    public HttpClient WithBearer(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_server) { Database = "postgres" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<KioskApiFactory>
{
    public const string Name = "api";
}
