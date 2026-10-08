using System.Text.Json;
using System.Text.Json.Serialization;
using Kiosk.Api;
using Kiosk.Api.Auth;
using Kiosk.Api.Hubs;
using Kiosk.Api.Middleware;
using Kiosk.Application;
using Kiosk.Application.Abstractions;
using Kiosk.Infrastructure;
using Kiosk.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

DotEnv.LoadForDevelopment(); // must run before the builder reads environment variables

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, log) => log.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKioskAuth(builder.Configuration);
builder.Services.AddKioskRateLimits();

// Same JSON rules for controllers and for the OpenAPI generator, so the TypeScript client matches the wire format:
// enums as strings, and numbers only as numbers (the web default also accepts "12.5" strings).
static void ConfigureJson(JsonSerializerOptions o)
{
    o.Converters.Add(new JsonStringEnumConverter());
    o.NumberHandling = JsonNumberHandling.Strict;
}
builder.Services.AddControllers().AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services
    .AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IOrderNotifier, SignalROrderNotifier>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

// Behind a hosting proxy (Render): take the client IP from the last X-Forwarded-For hop only, i.e. the one the
// proxy itself appended, so a caller cannot fake their IP to dodge the per-IP rate limits.
var behindProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear(); // the proxy's addresses aren't published; trust the single hop it adds
        o.KnownProxies.Clear();
    });
}

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // SignalR negotiate sends credentials

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Sample menu + one kiosk device. Development: Dev:* (on). Elsewhere: Bootstrap:* (opt-in), until the Admin app exists.
var isDev = app.Environment.IsDevelopment();
var seedMenu = app.Configuration.GetValue<bool>(isDev ? "Dev:SeedDemoData" : "Bootstrap:SeedDemoMenu");
var kioskToken = app.Configuration[isDev ? "Dev:KioskToken" : "Bootstrap:KioskToken"];
if (!isDev && !string.IsNullOrEmpty(kioskToken) && (!kioskToken.StartsWith("dev_", StringComparison.Ordinal) || kioskToken.Length < 36))
    throw new InvalidOperationException("Bootstrap:KioskToken must be \"dev_\" followed by at least 32 random characters.");
if (seedMenu || !string.IsNullOrEmpty(kioskToken))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoDataSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), seedMenu, kioskToken,
        isDev ? "Dev Kiosk" : "Kiosk 1");
}

if (behindProxy)
    app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "database unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.MapControllers();
app.MapHub<OrdersHub>("/hubs/orders");

app.Run();

public partial class Program;
