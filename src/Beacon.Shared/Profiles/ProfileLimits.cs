namespace Beacon.Shared.Profiles;

/// <summary>
/// Field limits shared by the editor and the server's validator, for the same reason as
/// <see cref="Beacons.BeaconLimits"/>: a client that allows more than the server accepts produces a
/// rejection the user cannot act on.
/// </summary>
public static class ProfileLimits
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 48;

    /// <summary>The epithet under the name. "The Wolf Without a Den".</summary>
    public const int TitleMaxLength = 60;

    public const int AgeMin = 18;
    public const int AgeMax = 9999;

    /// <summary>The one line in quotes on the card.</summary>
    public const int QuoteMaxLength = 160;

    /// <summary>
    /// The three words under the name: "Wild - Wanderer - Independent".
    /// Three, because it reads as a rhythm; four reads as a list.
    /// </summary>
    public const int MaxArchetypeWords = 3;
    public const int ArchetypeWordMaxLength = 20;

    /// <summary>
    /// The only biography that appears on the card itself. Short on purpose: the card exists to make
    /// someone decide whether to walk over, and nobody reads three screens of history to decide that.
    /// </summary>
    public const int OverviewMaxLength = 500;

    /// <summary>Everything below the fold, for people who do want the novel.</summary>
    public const int HistoryMaxLength = 4000;
    public const int GoalsMaxLength = 800;

    /// <summary>A single hook. One sentence that gives a stranger a reason to speak to you.</summary>
    public const int HookMaxLength = 140;
    public const int MaxHooks = 5;

    /// <summary>
    /// The live line: what this character is doing right now.
    ///
    /// Short on purpose. It is meant to be rewritten in ten seconds from a chat command between
    /// scenes, not composed. A field long enough for a paragraph becomes a second biography that
    /// nobody updates, which is worse than having none.
    /// </summary>
    public const int CurrentlyMaxLength = 200;

    /// <summary>The out-of-character note: the player speaking as themselves, not the character.</summary>
    public const int OutOfCharacterMaxLength = 400;

    /// <summary>
    /// What a stranger notices before a word is exchanged: height, bearing, the scar, the smell of
    /// woodsmoke. Five slots, because this is the impression, not the description.
    /// </summary>
    public const int MaxGlanceNotes = 5;
    public const int GlanceLabelMaxLength = 24;
    public const int GlanceTextMaxLength = 140;

    /// <summary>
    /// How long a "currently" line stays believable before the card should say how old it is.
    ///
    /// A live line that is three weeks old is not live, and silently presenting it as though it were
    /// is the failure mode this whole field has in every other tool that offers it.
    /// </summary>
    public static readonly TimeSpan CurrentlyFreshFor = TimeSpan.FromDays(3);

    public const int MaxPersonalityTraits = 6;
    public const int MaxTones = 5;
    public const int MaxActivities = 6;

    /// <summary>All four mature themes may be set; the cap exists only to bound a malformed request.</summary>
    public const int MaxMatureThemes = 4;

    /// <summary>
    /// The shape of a portrait, in one place.
    ///
    /// The crop preview, the stored image and the card all have to agree on this. When they did not,
    /// a portrait cropped to 5:6 was drawn into a 4:5 frame and every face on every card was quietly
    /// stretched by four per cent -- which is exactly the kind of wrongness nobody can name but
    /// everybody can see.
    /// </summary>
    public const int PortraitWidth = 600;
    public const int PortraitHeight = 720;

    /// <summary>Width over height, so a frame is <c>width / PortraitAspect</c> tall.</summary>
    public const float PortraitAspect = PortraitWidth / (float)PortraitHeight;

    /// <summary>Gallery size. Enough for a proper reference sheet, bounded enough to host.</summary>
    public const int MaxGalleryImages = 8;
    public const int CaptionMaxLength = 80;

    public const int MaxRelationships = 20;
    public const int RelationshipNoteMaxLength = 120;

    /// <summary>Free-text boundaries shown on the card face, e.g. "no romance, ask before injury".</summary>
    public const int BoundariesMaxLength = 200;

    public const int TimezoneMaxLength = 40;
    public const int ContactMaxLength = 80;

    /// <summary>
    /// How often a client may report that it is standing at a lit beacon.
    ///
    /// The signal only needs to be accurate to the hour to be useful ("active this evening"), so a
    /// heartbeat any more frequent than this is pure load. The server enforces it as well, because a
    /// client that ignores it would otherwise be free to hammer the endpoint.
    /// </summary>
    public static readonly TimeSpan ActivityHeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long after their last sighting someone still counts as active in search.
    ///
    /// Two weeks is roughly the point where a profile stops being a person you might run into and
    /// starts being an archive entry.
    /// </summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(14);

    public const int MaxPageSize = 60;
    public const int DefaultPageSize = 20;
}
