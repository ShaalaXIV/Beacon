using System.Text;

namespace Compass.Shared.Profiles;

/// <summary>
/// Filters for a Chronicle search.
///
/// Tag filters combine with AND: selecting Protective and Curious means both, not either. That is what
/// "filter" means everywhere else in a UI, and a predictable narrowing beats a clever one -- somebody
/// who wants either can simply pick one.
/// </summary>
public sealed record ProfileQuery
{
    /// <summary>Free text across name, title, overview and hooks.</summary>
    public string? Search { get; init; }

    public string? Race { get; init; }

    public string? World { get; init; }

    public string? DataCenter { get; init; }

    /// <summary>All of these traits must be present.</summary>
    public IReadOnlyList<PersonalityTrait> Personality { get; init; } = [];

    public IReadOnlyList<RpTone> Tones { get; init; } = [];

    public IReadOnlyList<RpActivity> Activities { get; init; } = [];

    public RpLength? Length { get; init; }

    /// <summary>Only people whose beacon is burning right now and who are open to walk-ups.</summary>
    public bool AvailableOnly { get; init; }

    /// <summary>Only people seen at a beacon within this many days.</summary>
    public int? ActiveWithinDays { get; init; }

    /// <summary>Only profiles that welcome strangers walking up.</summary>
    public bool WalkupsOnly { get; init; }

    /// <summary>
    /// Whether to include profiles flagged as adult. Off by default: a directory that shows this
    /// by default to someone who did not ask for it is a directory people uninstall.
    /// </summary>
    public bool IncludeMature { get; init; }

    /// <summary>
    /// Narrow to people open to particular adult content. Ignored entirely unless
    /// <see cref="IncludeMature"/> is also set, so it can never widen a search past what was asked for.
    /// </summary>
    public IReadOnlyList<MatureTheme> MatureThemes { get; init; } = [];

    /// <summary>Restrict to one account's profiles. Combined with your own id, this is "my cards".</summary>
    public Guid? OwnerAccountId { get; init; }

    public ProfileSort Sort { get; init; } = ProfileSort.Relevance;

    public int Page { get; init; }

    public int PageSize { get; init; } = ProfileLimits.DefaultPageSize;

    /// <summary>
    /// Renders the query as a URL query string, including the leading '?'.
    /// Lives beside the query type so the plugin cannot drift from the names the server binds.
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

        // Repeated keys rather than a delimiter, so a value containing a comma could never split a tag.
        void AddAll<T>(string key, IReadOnlyList<T> values)
        {
            foreach (var value in values)
                Add(key, value?.ToString());
        }

        Add("search", Search);
        Add("race", Race);
        Add("world", World);
        Add("dataCenter", DataCenter);
        AddAll("personality", Personality);
        AddAll("tone", Tones);
        AddAll("activity", Activities);
        Add("length", Length?.ToString());

        if (AvailableOnly)
            Add("availableOnly", "true");

        if (ActiveWithinDays is { } days)
            Add("activeWithinDays", days.ToString());

        if (WalkupsOnly)
            Add("walkupsOnly", "true");

        if (IncludeMature)
        {
            Add("includeMature", "true");
            AddAll("mature", MatureThemes);
        }

        Add("ownerAccountId", OwnerAccountId?.ToString());

        if (Sort != ProfileSort.Relevance)
            Add("sort", Sort.ToString());

        if (Page > 0)
            Add("page", Page.ToString());

        if (PageSize != ProfileLimits.DefaultPageSize)
            Add("pageSize", PageSize.ToString());

        return sb.ToString();
    }
}
