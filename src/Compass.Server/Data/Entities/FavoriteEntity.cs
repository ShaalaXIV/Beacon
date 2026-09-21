namespace Compass.Server.Data.Entities;

/// <summary>An account starring a beacon. Composite key on (account, beacon).</summary>
public class FavoriteEntity
{
    public Guid AccountId { get; set; }

    public Guid BeaconId { get; set; }

    public BeaconEntity? Beacon { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
