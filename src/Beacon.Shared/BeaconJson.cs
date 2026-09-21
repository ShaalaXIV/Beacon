using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.Shared;

/// <summary>
/// The serialiser settings both ends must use. Enums travel as strings so that adding a
/// <see cref="Beacons.BeaconKind"/> never silently renumbers existing rows, and nulls are dropped on
/// write so that PATCH bodies mean "change only what I sent".
/// </summary>
public static class BeaconJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
