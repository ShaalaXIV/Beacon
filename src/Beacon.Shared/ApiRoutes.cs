namespace Beacon.Shared;

/// <summary>
/// Every route and header the plugin and server agree on, in one place, so a rename is a compile error
/// rather than a 404 discovered by a user.
/// </summary>
public static class ApiRoutes
{
    /// <summary>Header carrying the account secret key on authenticated calls.</summary>
    public const string ApiKeyHeader = "X-Beacon-Key";

    /// <summary>
    /// Header carrying the client's protocol version. The server uses it to reject clients it can no
    /// longer talk to with a clear "please update" rather than a confusing deserialisation failure.
    /// </summary>
    public const string ProtocolHeader = "X-Beacon-Protocol";

    /// <summary>Bump when a change to the shared contracts is not backwards compatible.</summary>
    public const int ProtocolVersion = 3;

    /// <summary>Liveness probe, unauthenticated. Also reports the server's protocol range.</summary>
    public const string Health = "/health";

    /// <summary>WebSocket endpoint pushing <see cref="Beacons.BeaconEvent"/> frames.</summary>
    public const string Hub = "/hub/beacons";

    public static class Accounts
    {
        public const string Register = "/api/accounts/register";
        public const string Me = "/api/accounts/me";
        public const string Characters = "/api/accounts/me/characters";

        /// <summary>One-time adult confirmation, required before any mature content is set or seen.</summary>
        public const string ConfirmAdult = "/api/accounts/me/adult";

        public static string Character(string name, uint worldId) =>
            $"{Characters}/{Uri.EscapeDataString(name)}/{worldId}";
    }

    public static class Beacons
    {
        public const string Root = "/api/beacons";

        public static string ById(Guid id) => $"{Root}/{id}";

        public static string ByShareCode(string code) => $"{Root}/code/{Uri.EscapeDataString(code)}";

        public static string Light(Guid id) => $"{Root}/{id}/light";

        public static string Stoke(Guid id) => $"{Root}/{id}/stoke";

        public static string Extinguish(Guid id) => $"{Root}/{id}/extinguish";

        public static string Favorite(Guid id) => $"{Root}/{id}/favorite";

        public static string Report(Guid id) => $"{Root}/{id}/report";

        public static string Image(Guid id) => $"{Root}/{id}/image";
    }

    public static class Profiles
    {
        public const string Root = "/api/profiles";

        /// <summary>The calling account's own cards.</summary>
        public const string Mine = "/api/profiles/me";

        /// <summary>Reporting that a character is standing at a lit beacon.</summary>
        public const string Activity = "/api/profiles/activity";

        public static string ById(Guid id) => $"{Root}/{id}";

        public static string ByShareCode(string code) => $"{Root}/code/{Uri.EscapeDataString(code)}";

        /// <summary>
        /// Looks a profile up by the character it belongs to.
        /// This is the route behind "who is the person standing in front of me".
        /// </summary>
        public static string ByCharacter(string name, uint worldId) =>
            $"{Root}/character/{Uri.EscapeDataString(name)}/{worldId}";

        public static string Availability(Guid id) => $"{Root}/{id}/availability";

        public static string Images(Guid id) => $"{Root}/{id}/images";

        public static string Image(Guid id, Guid imageId) => $"{Root}/{id}/images/{imageId}";

        public static string Gallery(Guid id) => $"{Root}/{id}/gallery";

        public static string Links(Guid id) => $"{Root}/{id}/links";

        public static string Link(Guid id, Guid otherId) => $"{Root}/{id}/links/{otherId}";

        public static string ConfirmLink(Guid id, Guid otherId) => $"{Root}/{id}/links/{otherId}/confirm";

        public static string Report(Guid id) => $"{Root}/{id}/report";
    }

    public static class Images
    {
        public const string Root = "/images";

        /// <summary>Full-size screenshot. Immutable once written, so it is safe to cache forever.</summary>
        public static string Full(Guid imageId) => $"{Root}/{imageId}";

        /// <summary>Atlas thumbnail.</summary>
        public static string Thumb(Guid imageId) => $"{Root}/{imageId}/thumb";
    }
}
