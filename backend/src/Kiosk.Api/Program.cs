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
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Dev:SeedDemoData"))
        await DemoDataSeeder.SeedAsync(db, app.Configuration["Dev:KioskToken"]);
}

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
