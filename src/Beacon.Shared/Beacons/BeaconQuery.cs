using System.Text;

namespace Beacon.Shared.Beacons;

/// <summary>
/// Filters for an atlas query. Every field is optional; an empty query is "show me everything public,
/// most alive first", which is the right default for someone who just opened the plugin.
/// </summary>
public sealed record BeaconQuery
{
    /// <summary>Free text matched against name, description, zone and owner.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to one zone.</summary>
    public ushort? TerritoryId { get; init; }

    /// <summary>Restrict to one data centre, e.g. "Crystal".</summary>
    public string? DataCenter { get; init; }

    /// <summary>Restrict to one world, e.g. "Balmung".</summary>
    public string? World { get; init; }

    /// <summary>Restrict to one region, e.g. "North America".</summary>
    public string? Region { get; init; }

    /// <summary>Restrict to one kind of place.</summary>
    public BeaconKind? Kind { get; init; }

    /// <summary>Only beacons burning right now.</summary>
    public bool LitOnly { get; init; }

    /// <summary>Only beacons carrying this tag.</summary>
    public string? Tag { get; init; }

    /// <summary>Only beacons owned by this account. Combined with the caller's own id, this is "my beacons".</summary>
    public Guid? OwnerAccountId { get; init; }

    /// <summary>Only beacons the caller has starred.</summary>
    public bool FavoritesOnly { get; init; }

    public BeaconSort Sort { get; init; } = BeaconSort.Relevance;

    /// <summary>Zero-based page index.</summary>
    public int Page { get; init; }

    public int PageSize { get; init; } = BeaconLimits.DefaultPageSize;

    /// <summary>
    /// Renders the query as a URL query string, including the leading '?'. Kept next to the query type
    /// so the plugin cannot drift out of sync with the parameter names the server binds.
    /// </summary>
    public string ToQueryString()
    {
        var sb = new StringBuilder();

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            sb.Append(sb.Length == 0 ? '?' : '&');
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        Add("search", Search);
        Add("territoryId", TerritoryId?.ToString());
        Add("dataCenter", DataCenter);
        Add("world", World);
        Add("region", Region);
        Add("kind", Kind?.ToString());
        if (LitOnly)
            Add("litOnly", "true");
        Add("tag", Tag);
        Add("ownerAccountId", OwnerAccountId?.ToString());
        if (FavoritesOnly)
            Add("favoritesOnly", "true");
        if (Sort != BeaconSort.Relevance)
            Add("sort", Sort.ToString());
        if (Page > 0)
            Add("page", Page.ToString());
        if (PageSize != BeaconLimits.DefaultPageSize)
            Add("pageSize", PageSize.ToString());

        return sb.ToString();
    }
}

/// <summary>One page of results plus enough context to render a pager.</summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Total matching rows across all pages.</summary>
    public int Total { get; init; }

    /// <summary>True when another page exists after this one.</summary>
    public bool HasMore => (Page + 1) * PageSize < Total;

    public static PagedResult<T> Empty(int pageSize = BeaconLimits.DefaultPageSize) =>
        new() { Items = [], Page = 0, PageSize = pageSize, Total = 0 };
}
