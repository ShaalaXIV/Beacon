using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beacon.Shared;
using Beacon.Shared.Accounts;
using Beacon.Shared.Beacons;
using Beacon.Shared.Profiles;

namespace Beacon.Services;

/// <summary>The outcome of a call, carrying the server's own message when something went wrong.</summary>
public readonly record struct ApiResult<T>
{
    private ApiResult(T? value, string? error, HttpStatusCode status)
    {
        Value = value;
        Error = error;
        Status = status;
    }

    public T? Value { get; }

    /// <summary>
    /// The server's message, verbatim, when it gave one. Showing the server's wording rather than a
    /// generic failure is what turns "something went wrong" into "you are 480 yalms away".
    /// </summary>
    public string? Error { get; }

    public HttpStatusCode Status { get; }

    public bool Ok => Error is null;

    public static ApiResult<T> Success(T value) => new(value, null, HttpStatusCode.OK);

    public static ApiResult<T> Failure(string error, HttpStatusCode status = HttpStatusCode.InternalServerError) =>
        new(default, error, status);
}

/// <summary>
/// Talks to the Beacon server. Every method returns rather than throws, because every one of these
/// calls happens behind a button that needs to show a reason when it does not work.
/// </summary>
public sealed class BeaconApi(Configuration config) : IDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler
    {
        // Screenshots are the slow part; everything else is small JSON.
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>True when the last call reached the server, whatever it answered.</summary>
    public bool Reachable { get; private set; } = true;

    // --- Accounts -------------------------------------------------------

    public Task<ApiResult<RegisterAccountResponse>> RegisterAsync(string displayName, CancellationToken ct) =>
        SendAsync<RegisterAccountResponse>(
            HttpMethod.Post,
            ApiRoutes.Accounts.Register,
            new RegisterAccountRequest { DisplayName = displayName },
            authenticated: false,
            ct);

    public Task<ApiResult<AccountDto>> GetAccountAsync(CancellationToken ct) =>
        SendAsync<AccountDto>(HttpMethod.Get, ApiRoutes.Accounts.Me, null, true, ct);

    public Task<ApiResult<AccountDto>> UpdateAccountAsync(string displayName, CancellationToken ct) =>
        SendAsync<AccountDto>(
            HttpMethod.Patch,
            ApiRoutes.Accounts.Me,
            new UpdateAccountRequest { DisplayName = displayName },
            true,
            ct);

    public Task<ApiResult<AccountDto>> LinkCharacterAsync(LinkCharacterRequest request, CancellationToken ct) =>
        SendAsync<AccountDto>(HttpMethod.Post, ApiRoutes.Accounts.Characters, request, true, ct);

    /// <summary>Records, or withdraws, the account holder's confirmation that they are an adult.</summary>
    public Task<ApiResult<AccountDto>> ConfirmAdultAsync(bool confirmed, CancellationToken ct) =>
        SendAsync<AccountDto>(
            HttpMethod.Put,
            ApiRoutes.Accounts.ConfirmAdult,
            new ConfirmAdultRequest { Confirmed = confirmed },
            true,
            ct);

    // --- Beacons --------------------------------------------------------

    public Task<ApiResult<PagedResult<BeaconDto>>> BrowseAsync(BeaconQuery query, CancellationToken ct) =>
        SendAsync<PagedResult<BeaconDto>>(
            HttpMethod.Get,
            ApiRoutes.Beacons.Root + query.ToQueryString(),
            null,
            true,
            ct);

    public Task<ApiResult<BeaconDto>> GetBeaconAsync(Guid id, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Get, ApiRoutes.Beacons.ById(id), null, true, ct);

    public Task<ApiResult<BeaconDto>> GetByShareCodeAsync(string code, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Get, ApiRoutes.Beacons.ByShareCode(code), null, true, ct);

    public Task<ApiResult<BeaconDto>> CreateBeaconAsync(CreateBeaconRequest request, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Post, ApiRoutes.Beacons.Root, request, true, ct);

    public Task<ApiResult<BeaconDto>> UpdateBeaconAsync(Guid id, UpdateBeaconRequest request, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Patch, ApiRoutes.Beacons.ById(id), request, true, ct);

    public Task<ApiResult<bool>> DeleteBeaconAsync(Guid id, CancellationToken ct) =>
        SendAsync<bool>(HttpMethod.Delete, ApiRoutes.Beacons.ById(id), null, true, ct);

    public Task<ApiResult<BeaconDto>> LightAsync(Guid id, LightBeaconRequest request, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Post, ApiRoutes.Beacons.Light(id), request, true, ct);

    public Task<ApiResult<BeaconDto>> StokeAsync(Guid id, StokeBeaconRequest request, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Post, ApiRoutes.Beacons.Stoke(id), request, true, ct);

    public Task<ApiResult<BeaconDto>> ExtinguishAsync(Guid id, CancellationToken ct) =>
        SendAsync<BeaconDto>(HttpMethod.Post, ApiRoutes.Beacons.Extinguish(id), null, true, ct);

    public Task<ApiResult<BeaconDto>> SetFavoriteAsync(Guid id, bool favorite, CancellationToken ct) =>
        SendAsync<BeaconDto>(
            favorite ? HttpMethod.Put : HttpMethod.Delete,
            ApiRoutes.Beacons.Favorite(id),
            null,
            true,
            ct);

    public Task<ApiResult<bool>> ReportAsync(Guid id, string reason, CancellationToken ct) =>
        SendAsync<bool>(
            HttpMethod.Post,
            ApiRoutes.Beacons.Report(id),
            new ReportBeaconRequest { Reason = reason },
            true,
            ct);

    // --- Profiles -------------------------------------------------------

    public Task<ApiResult<PagedResult<ProfileDto>>> SearchProfilesAsync(ProfileQuery query, CancellationToken ct) =>
        SendAsync<PagedResult<ProfileDto>>(
            HttpMethod.Get,
            ApiRoutes.Profiles.Root + query.ToQueryString(),
            null,
            true,
            ct);

    public Task<ApiResult<PagedResult<ProfileDto>>> MyProfilesAsync(CancellationToken ct) =>
        SendAsync<PagedResult<ProfileDto>>(HttpMethod.Get, ApiRoutes.Profiles.Mine, null, true, ct);

    public Task<ApiResult<ProfileDto>> GetProfileAsync(Guid id, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Get, ApiRoutes.Profiles.ById(id), null, true, ct);

    /// <summary>The lookup behind "who is the person standing in front of me".</summary>
    public Task<ApiResult<ProfileDto>> GetProfileByCharacterAsync(string name, uint worldId, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Get, ApiRoutes.Profiles.ByCharacter(name, worldId), null, true, ct);

    public Task<ApiResult<ProfileDto>> GetProfileByShareCodeAsync(string code, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Get, ApiRoutes.Profiles.ByShareCode(code), null, true, ct);

    public Task<ApiResult<ProfileDto>> SaveProfileAsync(SaveProfileRequest request, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Put, ApiRoutes.Profiles.Root, request, true, ct);

    public Task<ApiResult<bool>> DeleteProfileAsync(Guid id, CancellationToken ct) =>
        SendAsync<bool>(HttpMethod.Delete, ApiRoutes.Profiles.ById(id), null, true, ct);

    public Task<ApiResult<ProfileDto>> SetAvailabilityAsync(Guid id, AvailabilityOverride availability, CancellationToken ct) =>
        SendAsync<ProfileDto>(
            HttpMethod.Put,
            ApiRoutes.Profiles.Availability(id),
            new SetAvailabilityRequest { Availability = availability },
            true,
            ct);

    public Task<ApiResult<ProfileDto>> UploadPortraitAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct) =>
        UploadAsync<ProfileDto>(ApiRoutes.Profiles.Images(id), bytes, fileName, ct);

    public Task<ApiResult<ProfileDto>> RemoveGalleryImageAsync(Guid id, Guid imageId, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Delete, ApiRoutes.Profiles.Image(id, imageId), null, true, ct);

    public Task<ApiResult<ProfileDto>> UpdateGalleryAsync(Guid id, UpdateGalleryRequest request, CancellationToken ct) =>
        SendAsync<ProfileDto>(HttpMethod.Put, ApiRoutes.Profiles.Gallery(id), request, true, ct);

    /// <summary>
    /// Reports that a character is standing at a lit beacon.
    ///
    /// Deliberately quiet: the server throttles and verifies this, and a refusal means nothing was
    /// recorded, which is not something worth interrupting a player about.
    /// </summary>
    public async Task<bool> HeartbeatAsync(ActivityHeartbeatRequest request, CancellationToken ct)
    {
        var result = await SendAsync<HeartbeatResponse>(
            HttpMethod.Post,
            ApiRoutes.Profiles.Activity,
            request,
            true,
            ct);

        return result.Ok && result.Value?.Recorded == true;
    }

    private sealed record HeartbeatResponse
    {
        public bool Recorded { get; init; }
    }

    // --- Images ---------------------------------------------------------

    /// <summary>Uploads a screenshot as multipart form data and returns the updated beacon.</summary>
    public Task<ApiResult<BeaconDto>> UploadImageAsync(
        Guid beaconId,
        byte[] bytes,
        string fileName,
        CancellationToken ct) =>
        UploadAsync<BeaconDto>(ApiRoutes.Beacons.Image(beaconId), bytes, fileName, ct);

    /// <summary>Posts image bytes as multipart form data. Shared by beacon screenshots and profile galleries.</summary>
    private async Task<ApiResult<T>> UploadAsync<T>(string route, byte[] bytes, string fileName, CancellationToken ct)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(fileName));
            content.Add(file, "image", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, Url(route)) { Content = content };
            ApplyHeaders(request, authenticated: true);

            using var response = await http.SendAsync(request, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (Exception ex)
        {
            return Unreachable<T>(ex);
        }
    }

    public Task<ApiResult<bool>> DeleteImageAsync(Guid beaconId, CancellationToken ct) =>
        SendAsync<bool>(HttpMethod.Delete, ApiRoutes.Beacons.Image(beaconId), null, true, ct);

    /// <summary>
    /// Downloads image bytes for the texture cache. Null on any failure; the caller draws a placeholder.
    /// Screenshots are stored as WebP, so <paramref name="preferPng"/> asks the server to transcode for
    /// machines whose imaging stack cannot decode it.
    /// </summary>
    public async Task<byte[]?> DownloadImageAsync(Guid imageId, bool thumb, bool preferPng, CancellationToken ct)
    {
        try
        {
            var route = thumb ? ApiRoutes.Images.Thumb(imageId) : ApiRoutes.Images.Full(imageId);
            if (preferPng)
                route += "?format=png";

            using var request = new HttpRequestMessage(HttpMethod.Get, Url(route));
            ApplyHeaders(request, authenticated: true);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "Could not download image {ImageId}.", imageId);
            return null;
        }
    }

    // --- Health ---------------------------------------------------------

    /// <summary>Checks the server is up and speaks a protocol we understand.</summary>
    public async Task<ApiResult<ServerHealth>> HealthAsync(CancellationToken ct)
    {
        var result = await SendAsync<ServerHealth>(HttpMethod.Get, ApiRoutes.Health, null, false, ct);
        if (!result.Ok)
            return result;

        var health = result.Value!;
        if (health.Protocol != ApiRoutes.ProtocolVersion)
        {
            return ApiResult<ServerHealth>.Failure(
                health.Protocol > ApiRoutes.ProtocolVersion
                    ? "This server is newer than your plugin. Update Beacon to connect."
                    : "This server is older than your plugin and cannot be used until it is updated.");
        }

        return result;
    }

    public sealed record ServerHealth
    {
        public string Status { get; init; } = string.Empty;

        public int Protocol { get; init; }

        public int Listeners { get; init; }

        public bool RegistrationOpen { get; init; }
    }

    // --- Plumbing -------------------------------------------------------

    private string Url(string route) => config.NormalisedServerUrl + route;

    private void ApplyHeaders(HttpRequestMessage request, bool authenticated)
    {
        request.Headers.TryAddWithoutValidation(ApiRoutes.ProtocolHeader, ApiRoutes.ProtocolVersion.ToString());

        if (authenticated && !string.IsNullOrWhiteSpace(config.SecretKey))
            request.Headers.TryAddWithoutValidation(ApiRoutes.ApiKeyHeader, config.SecretKey);
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string route,
        object? body,
        bool authenticated,
        CancellationToken ct)
    {
        if (authenticated && !config.HasAccount)
            return ApiResult<T>.Failure("No Beacon account yet. Open the settings to create one.", HttpStatusCode.Unauthorized);

        try
        {
            using var request = new HttpRequestMessage(method, Url(route));
            ApplyHeaders(request, authenticated);

            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, body.GetType(), BeaconJson.Options),
                    Encoding.UTF8,
                    "application/json");
            }

            using var response = await http.SendAsync(request, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<T>.Failure("Cancelled.");
        }
        catch (Exception ex)
        {
            return Unreachable<T>(ex);
        }
    }

    private async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        Reachable = true;

        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return ApiResult<T>.Failure(ExtractError(payload, response.StatusCode), response.StatusCode);

        // Endpoints that answer 204, and the bool-returning calls, have nothing to deserialise.
        if (typeof(T) == typeof(bool))
            return ApiResult<T>.Success((T)(object)true);

        if (string.IsNullOrWhiteSpace(payload))
            return ApiResult<T>.Failure("The server sent an empty response.", response.StatusCode);

        try
        {
            var value = JsonSerializer.Deserialize<T>(payload, BeaconJson.Options);
            return value is null
                ? ApiResult<T>.Failure("The server sent something Beacon could not read.", response.StatusCode)
                : ApiResult<T>.Success(value);
        }
        catch (JsonException ex)
        {
            Svc.Log.Warning(ex, "Could not deserialise a {Type} response.", typeof(T).Name);
            return ApiResult<T>.Failure("The server sent something Beacon could not read.", response.StatusCode);
        }
    }

    /// <summary>Pulls the server's own error message out of the body, falling back to the status.</summary>
    private static string ExtractError(string payload, HttpStatusCode status)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in (string[])["error", "detail", "title", "message"])
                    {
                        if (document.RootElement.TryGetProperty(property, out var value)
                            && value.ValueKind == JsonValueKind.String)
                        {
                            return value.GetString()!;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON. Fall through to the status-based message.
            }
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "Your Beacon key was not accepted.",
            HttpStatusCode.Forbidden => "You are not allowed to do that.",
            HttpStatusCode.NotFound => "That is no longer there.",
            HttpStatusCode.TooManyRequests => "Slow down a moment, then try again.",
            HttpStatusCode.UpgradeRequired => "The Beacon server requires a newer plugin. Update Beacon and try again.",
            _ => $"The server answered {(int)status}.",
        };
    }

    private ApiResult<T> Unreachable<T>(Exception ex)
    {
        Reachable = false;
        Svc.Log.Debug(ex, "Beacon server unreachable.");
        return ApiResult<T>.Failure("Could not reach the Beacon server.", HttpStatusCode.ServiceUnavailable);
    }

    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };

    public void Dispose() => http.Dispose();
}
