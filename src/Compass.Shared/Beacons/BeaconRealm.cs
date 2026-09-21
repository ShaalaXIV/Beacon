namespace Compass.Shared.Beacons;

/// <summary>
/// Which world a beacon physically sits on. Travel feasibility is decided entirely from this:
/// same world is a plain teleport, same data centre is a world visit, and a different data centre
/// needs a DC travel -- each of which Lifestream handles, but which we must tell the player about up front.
/// </summary>
public sealed record BeaconRealm
{
    /// <summary>World row id. The authoritative key; names are not unique across regions forever.</summary>
    public uint WorldId { get; init; }

    /// <summary>World display name, e.g. "Balmung".</summary>
    public string WorldName { get; init; } = string.Empty;

    /// <summary>Data centre name, e.g. "Crystal".</summary>
    public string DataCenter { get; init; } = string.Empty;

    /// <summary>Physical region, e.g. "North America". DC travel is only possible within a region.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>"Balmung (Crystal)" -- the form players actually use when telling each other where to go.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(DataCenter) ? WorldName : $"{WorldName} ({DataCenter})";
}
