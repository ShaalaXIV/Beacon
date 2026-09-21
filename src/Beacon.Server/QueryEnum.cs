namespace Beacon.Server;

/// <summary>
/// Case-insensitive enum parsing for query-string parameters.
///
/// Minimal APIs bind an enum from the query string with a case-sensitive parse, and a mismatch throws
/// rather than failing to bind -- so <c>?kind=camp</c> returns a 500 while <c>?kind=Camp</c> works.
/// Worse, JSON bodies serialise enums as camelCase, so the obvious spelling is the one that breaks.
///
/// Every enum-valued query parameter is therefore bound as a string and parsed through here, which
/// accepts any casing and turns a genuinely unknown value into a 400 that says which value was wrong.
/// </summary>
public static class QueryEnum
{
    /// <summary>Parses one optional value. Returns false and sets <paramref name="error"/> when unrecognised.</summary>
    public static bool TryParse<T>(string? raw, string parameterName, out T? value, out string? error)
        where T : struct, Enum
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (Enum.TryParse<T>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        error = Describe<T>(parameterName, raw);
        return false;
    }

    /// <summary>Parses a repeated parameter. An empty or absent list is valid and yields no values.</summary>
    public static bool TryParseAll<T>(string[]? raw, string parameterName, out List<T> values, out string? error)
        where T : struct, Enum
    {
        values = [];
        error = null;

        if (raw is null || raw.Length == 0)
            return true;

        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (Enum.TryParse<T>(item, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                values.Add(parsed);
                continue;
            }

            // Reject rather than skip. A silently dropped filter returns everything, which reads as a
            // broken search rather than a typo.
            error = Describe<T>(parameterName, item);
            values = [];
            return false;
        }

        return true;
    }

    /// <summary>Parses a value that has a default, falling back when absent.</summary>
    public static bool TryParseOr<T>(string? raw, string parameterName, T fallback, out T value, out string? error)
        where T : struct, Enum
    {
        var ok = TryParse<T>(raw, parameterName, out var parsed, out error);
        value = parsed ?? fallback;
        return ok;
    }

    private static string Describe<T>(string parameterName, string raw)
        where T : struct, Enum =>
        $"\"{raw}\" is not a valid {parameterName}. Expected one of: {string.Join(", ", Enum.GetNames<T>())}.";
}
