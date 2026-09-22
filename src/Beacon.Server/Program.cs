using System.Text.Json;
using System.Text.Json.Serialization;
using Beacon.Server;
using Beacon.Server.Auth;
using Beacon.Server.Data;
using Beacon.Server.Endpoints;
using Beacon.Server.Realtime;
using Beacon.Server.Services;
using Beacon.Shared;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Docker invokes the same signed release binary for its health probe. Keeping the probe in-process
// avoids adding curl (and its patch lifecycle) to the production image.
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    var healthUrl = Environment.GetEnvironmentVariable("BEACON_HEALTH_URL")
                    ?? "http://127.0.0.1:8080/health";

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        using var response = await client.GetAsync(healthUrl);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        Environment.ExitCode = 1;
    }

    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BeaconOptions>(builder.Configuration.GetSection(BeaconOptions.SectionName));

// Resolve the data directory before anything needs it, so a bad path fails at startup rather than
// on the first upload.
var beaconOptions = builder.Configuration.GetSection(BeaconOptions.SectionName).Get<BeaconOptions>()
                     ?? new BeaconOptions();

var dataDirectory = beaconOptions.ResolveDataDirectory(builder.Environment.ContentRootPath);

// Production must fail closed instead of merely warning: an in-image database appears to work until
// the first redeploy replaces the application directory and destroys every account and image.
if (!builder.Environment.IsDevelopment() && !Path.IsPathRooted(beaconOptions.DataDirectory))
{
    throw new InvalidOperationException(
        $"Beacon:DataDirectory must be absolute in Production; got '{beaconOptions.DataDirectory}'. "
        + "Use a persistent path outside the application, such as /var/lib/beacon.");
}

Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "images"));

var databasePath = Path.Combine(dataDirectory, "beacon.db");

builder.Services.AddDbContext<BeaconDbContext>(db =>
{
    db.UseSqlite($"Data Source={databasePath}");

    if (builder.Environment.IsDevelopment())
        db.EnableSensitiveDataLogging();
});

// The plugin and server must agree byte for byte on serialisation, so both sides read the same options.
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    json.SerializerOptions.PropertyNameCaseInsensitive = true;
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    json.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    json.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
});

builder.Services
    .AddAuthentication(BeaconAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BeaconAuthHandler>(BeaconAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddBeaconRateLimiting();

builder.Services.AddSingleton<BeaconHub>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddSingleton<StageService>();
builder.Services.AddScoped<BeaconService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddHostedService<FlameSweeper>();

builder.Services.AddProblemDetails();

var app = builder.Build();

await InitialiseDatabaseAsync(app);

app.UseExceptionHandler();

// Must run before the rate limiter, which partitions anonymous callers by remote address. Only
// enabled when proxies are named, so an untrusted client can never spoof its own address.
if (beaconOptions.TrustedProxies.Count > 0)
{
    var forwarding = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };

    forwarding.KnownProxies.Clear();
    forwarding.KnownIPNetworks.Clear();
    forwarding.ForwardLimit = 1;

    foreach (var proxy in beaconOptions.TrustedProxies)
    {
        if (IPAddress.TryParse(proxy, out var address))
            forwarding.KnownProxies.Add(address);
        else
            throw new InvalidOperationException(
                $"Beacon:TrustedProxies entry '{proxy}' is not an IP address. Refusing to trust forwarded headers.");
    }

    app.UseForwardedHeaders(forwarding);
}

// Every product endpoint is version-gated. This is the release fuse: a future breaking contract
// change cannot make an old plugin send or interpret the wrong shape silently. Health remains open so
// an old plugin can learn which protocol the server requires and show an actionable update message.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isProductEndpoint = path.StartsWithSegments("/api")
                            || path.StartsWithSegments("/images")
                            || path.StartsWithSegments("/hub");

    if (isProductEndpoint)
    {
        var raw = context.Request.Headers[ApiRoutes.ProtocolHeader].ToString();
        if (!int.TryParse(raw, out var protocol) || protocol != ApiRoutes.ProtocolVersion)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Beacon protocol {ApiRoutes.ProtocolVersion} is required. Update the Beacon plugin.",
                protocol = ApiRoutes.ProtocolVersion,
            });
            return;
        }
    }

    await next(context);
});

// Authentication runs before rate limiting so the limiter can partition by account rather than by IP;
// without this ordering, everyone behind one address shares a bucket.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapAccountEndpoints();
app.MapBeaconEndpoints();
app.MapImageEndpoints();
app.MapStageEndpoints();
app.MapProfileEndpoints();

app.MapGet(ApiRoutes.Health, (HttpContext http, BeaconHub hub, IOptions<BeaconOptions> options) =>
{
    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new
    {
        status = "ok",
        protocol = ApiRoutes.ProtocolVersion,
        minimumProtocol = ApiRoutes.ProtocolVersion,
        maximumProtocol = ApiRoutes.ProtocolVersion,
        listeners = hub.SubscriberCount,
        registrationOpen = options.Value.RegistrationOpen,
    });
}).AllowAnonymous();

app.Map(ApiRoutes.Hub, async (HttpContext http, BeaconHub hub) =>
{
    if (!http.WebSockets.IsWebSocketRequest)
        return Results.BadRequest(new { error = "Expected a WebSocket upgrade." });

    using var socket = await http.WebSockets.AcceptWebSocketAsync();
    await hub.HandleAsync(socket, http.GetAccount()?.Id, http.RequestAborted);

    // The response is already complete once the socket closes.
    return Results.Empty;
}).RequireAuthorization();

app.Run();
return;

// Applies migrations and promotes configured moderators.
//
// Migrating on startup suits a single-instance self-hosted service: there is no second node to race
// with, and it removes an entire class of "I deployed but forgot to migrate" outage.
static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<BeaconOptions>>().Value;

    await db.Database.MigrateAsync();

    if (options.ModeratorAccountIds.Count == 0)
        return;

    var ids = options.ModeratorAccountIds
        .Select(raw => Guid.TryParse(raw, out var id) ? id : (Guid?)null)
        .OfType<Guid>()
        .ToList();

    if (ids.Count == 0)
        return;

    var promoted = await db.Accounts
        .Where(a => ids.Contains(a.Id) && !a.IsModerator)
        .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsModerator, true));

    if (promoted > 0)
        logger.LogInformation("Promoted {Count} account(s) to moderator.", promoted);
}

// Named so integration tests can reference the entry point assembly.
public partial class Program;
