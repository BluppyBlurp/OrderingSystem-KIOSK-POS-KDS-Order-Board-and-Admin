using System.Security.Claims;
using System.Text.Encodings.Web;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kiosk.Api.Auth;

public sealed class ClerkOptions
{
    public const string Section = "Clerk";

    /// <summary>Clerk Frontend API URL, e.g. https://your-app.clerk.accounts.dev. Used as issuer and JWKS source.</summary>
    public string Authority { get; set; } = "";

    /// <summary>Origins allowed in the token's azp claim (the POS, KDS and Admin app URLs). Empty = not checked.</summary>
    public string[] AuthorizedParties { get; set; } = [];
}

public static class AuthSetup
{
    public const string SmartScheme = "Smart";
    public const string DeviceScheme = "Device";
    public const string DevStaffScheme = "DevStaff";
    private const string HubPathPrefix = "/hubs";

    public static IServiceCollection AddKioskAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var clerk = config.GetSection(ClerkOptions.Section).Get<ClerkOptions>() ?? new ClerkOptions();

        // Development only: "devstaff_<role>" tokens sign in as staff without Clerk, so POS/KDS/Admin can be
        // tested before Clerk roles are configured. Anywhere else such tokens go to JWT validation and fail.
        var devStaffLogin = env.IsDevelopment() && config.GetValue<bool>("Dev:StaffLogin");

        var auth = services
            .AddAuthentication(SmartScheme)
            // Routes each request to the right handler by looking at the token's prefix.
            .AddPolicyScheme(SmartScheme, SmartScheme, o => o.ForwardDefaultSelector = ctx =>
            {
                var token = ReadToken(ctx.Request);
                if (token?.StartsWith(DeviceService.TokenPrefix, StringComparison.Ordinal) == true)
                    return DeviceScheme;
                if (devStaffLogin && token?.StartsWith(DevStaffAuthenticationHandler.TokenPrefix, StringComparison.Ordinal) == true)
                    return DevStaffScheme;
                return JwtBearerDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceScheme, null);
        if (devStaffLogin)
            auth.AddScheme<AuthenticationSchemeOptions, DevStaffAuthenticationHandler>(DevStaffScheme, null);
        auth
            .AddJwtBearer(o =>
            {
                // Standard JWT validation against Clerk's cached JWKS; no runtime call to Clerk per request.
                o.Authority = string.IsNullOrEmpty(clerk.Authority) ? null : clerk.Authority;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = false, // Clerk session tokens carry azp, not aud
                    NameClaimType = Claims.Subject,
                    RoleClaimType = Claims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(HubPathPrefix))
                            ctx.Token = ctx.Request.Query["access_token"];
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        var azp = ctx.Principal?.FindFirstValue("azp");
                        if (clerk.AuthorizedParties.Length > 0 && (azp is null || !clerk.AuthorizedParties.Contains(azp)))
                            ctx.Fail("Token was issued for an unknown origin.");
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(Policies.Register);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        return services;
    }

    /// <summary>Bearer header, or ?access_token= on the SignalR hub (browsers cannot set WebSocket headers).</summary>
    internal static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        return request.Path.StartsWithSegments(HubPathPrefix) ? request.Query["access_token"].ToString() : null;
    }
}

/// <summary>Authenticates kiosks and order boards by their registered device token.</summary>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceService devices)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = AuthSetup.ReadToken(Request);
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var device = await devices.AuthenticateAsync(token, Context.RequestAborted);
        if (device is null)
            return AuthenticateResult.Fail("Unknown or revoked device token.");

        var identity = new ClaimsIdentity(
            [
                new Claim(Claims.DeviceId, device.Id.ToString()),
                new Claim(Claims.DeviceKind, device.Kind.ToString()),
                new Claim(ClaimTypes.Name, device.Name),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

/// <summary>Development only (see <see cref="AuthSetup.AddKioskAuth"/>): "devstaff_cashier" signs in as a cashier, etc.</summary>
public sealed class DevStaffAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TokenPrefix = "devstaff_";
    private static readonly string[] KnownRoles = [Roles.Admin, Roles.Manager, Roles.Cashier, Roles.Kitchen];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = AuthSetup.ReadToken(Request)?[TokenPrefix.Length..];
        if (role is null || !KnownRoles.Contains(role))
            return Task.FromResult(AuthenticateResult.Fail("Unknown dev staff role."));

        var identity = new ClaimsIdentity(
            [new Claim(Claims.Subject, $"dev-{role}"), new Claim(Claims.Role, role), new Claim(ClaimTypes.Name, $"Dev {role}")],
            Scheme.Name, Claims.Subject, Claims.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

internal sealed class HttpCurrentActor(IHttpContextAccessor http) : ICurrentActor
{
    public string? ActorId
    {
        get
        {
            var user = http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;
            return user.FindFirstValue(Claims.Subject)
                   ?? (user.FindFirstValue(Claims.DeviceId) is { } id ? $"device:{id}" : null);
        }
    }
}
