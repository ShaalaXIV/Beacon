using Beacon.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Beacon.Server.Data;

public class BeaconDbContext(DbContextOptions<BeaconDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();

    public DbSet<BeaconEntity> Beacons => Set<BeaconEntity>();

    public DbSet<BeaconImageEntity> Images => Set<BeaconImageEntity>();

    public DbSet<BeaconLightEntity> Lights => Set<BeaconLightEntity>();

    public DbSet<FavoriteEntity> Favorites => Set<FavoriteEntity>();

    public DbSet<ReportEntity> Reports => Set<ReportEntity>();

    public DbSet<ProfileEntity> Profiles => Set<ProfileEntity>();

    public DbSet<ProfileTagEntity> ProfileTags => Set<ProfileTagEntity>();

    public DbSet<ProfileHookEntity> ProfileHooks => Set<ProfileHookEntity>();

    public DbSet<ProfileImageEntity> ProfileImages => Set<ProfileImageEntity>();

    public DbSet<ProfileLinkEntity> ProfileLinks => Set<ProfileLinkEntity>();

    /// <summary>
    /// Stores every timestamp as Unix milliseconds.
    ///
    /// SQLite has no date type, and EF's default is an ISO string with an offset suffix. That sorts and
    /// compares correctly only while every row shares one offset -- one row written in a non-UTC offset
    /// silently corrupts every range query and ORDER BY. An integer has no such trap.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> TimestampConverter = new(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableTimestampConverter = new(
        v => v == null ? null : v.Value.ToUnixTimeMilliseconds(),
        v => v == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(v.Value));

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(TimestampConverter);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(NullableTimestampConverter);
            }
        }

        b.Entity<AccountEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();

            // The hot path on every authenticated request: look an account up by its key hash.
            e.HasIndex(x => x.KeyHash).IsUnique();

            e.HasMany(x => x.Characters)
                .WithOne(x => x.Account!)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CharacterEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(48).IsRequired();
            e.Property(x => x.WorldName).HasMaxLength(48);
            e.Property(x => x.DataCenter).HasMaxLength(48);

            // One character belongs to exactly one account, server-wide.
            e.HasIndex(x => new { x.Name, x.WorldId }).IsUnique();
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<BeaconEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.ZoneName).HasMaxLength(128);
            e.Property(x => x.SubAreaName).HasMaxLength(128);
            e.Property(x => x.NearestAetheryteName).HasMaxLength(128);
            e.Property(x => x.AethernetShard).HasMaxLength(128);
            e.Property(x => x.WorldName).HasMaxLength(48);
            e.Property(x => x.DataCenter).HasMaxLength(48);
            e.Property(x => x.Region).HasMaxLength(48);
            e.Property(x => x.LitByName).HasMaxLength(48);
            e.Property(x => x.LitNote).HasMaxLength(256);
            e.Property(x => x.TagsCsv).HasMaxLength(256);
            e.Property(x => x.ShareCode).HasMaxLength(16).IsRequired();

            e.HasIndex(x => x.ShareCode).IsUnique();
            e.HasIndex(x => x.OwnerAccountId);
            e.HasIndex(x => x.TerritoryId);
            e.HasIndex(x => x.DataCenter);
            e.HasIndex(x => x.WorldId);

            // Serves the default atlas query: live, visible beacons ordered by activity.
            e.HasIndex(x => new { x.IsDeleted, x.Visibility, x.IsLit });

            // Serves the flame sweeper, which runs on a timer and must stay cheap.
            e.HasIndex(x => new { x.IsLit, x.LitUntil });

            e.HasOne(x => x.Owner)
                .WithMany(x => x.Beacons)
                .HasForeignKey(x => x.OwnerAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BeaconImageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ContentType).HasMaxLength(64);
            e.HasIndex(x => x.BeaconId);
        });

        b.Entity<BeaconLightEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CharacterName).HasMaxLength(48);
            e.Property(x => x.Note).HasMaxLength(256);
            e.HasIndex(x => x.BeaconId);
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<FavoriteEntity>(e =>
        {
            e.HasKey(x => new { x.AccountId, x.BeaconId });
            e.HasIndex(x => x.BeaconId);

            e.HasOne(x => x.Beacon)
                .WithMany(x => x.Favorites)
                .HasForeignKey(x => x.BeaconId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReportEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.Resolved, x.CreatedAt });
            e.HasIndex(x => x.BeaconId);
            e.HasIndex(x => x.ProfileId);
        });

        b.Entity<ProfileEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CharacterName).HasMaxLength(48).IsRequired();
            e.Property(x => x.WorldName).HasMaxLength(48);
            e.Property(x => x.DataCenter).HasMaxLength(48);
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(80);
            e.Property(x => x.Race).HasMaxLength(32);
            e.Property(x => x.Clan).HasMaxLength(32);
            e.Property(x => x.Gender).HasMaxLength(32);
            e.Property(x => x.Pronouns).HasMaxLength(32);
            e.Property(x => x.ArchetypeCsv).HasMaxLength(96);
            e.Property(x => x.Quote).HasMaxLength(200);
            e.Property(x => x.Boundaries).HasMaxLength(256);
            e.Property(x => x.PlayerTimezone).HasMaxLength(48);
            e.Property(x => x.PlayerAvailability).HasMaxLength(200);
            e.Property(x => x.PlayerContact).HasMaxLength(96);
            e.Property(x => x.Overview).HasMaxLength(600);
            e.Property(x => x.History).HasMaxLength(4400);
            e.Property(x => x.Goals).HasMaxLength(1000);
            e.Property(x => x.ShareCode).HasMaxLength(16).IsRequired();

            // One card per character, server-wide, mirroring how characters themselves are claimed.
            e.HasIndex(x => new { x.CharacterName, x.WorldId }).IsUnique();
            e.HasIndex(x => x.ShareCode).IsUnique();
            e.HasIndex(x => x.OwnerAccountId);
            e.HasIndex(x => x.Race);
            e.HasIndex(x => x.DataCenter);

            // Serves the default Chronicle view: visible cards, most recently seen first.
            e.HasIndex(x => new { x.IsDeleted, x.Visibility, x.LastActiveAt });

            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Tags).WithOne(x => x.Profile!)
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hooks).WithOne(x => x.Profile!)
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Images).WithOne(x => x.Profile!)
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProfileTagEntity>(e =>
        {
            e.HasKey(x => x.Id);

            // The index the whole search depends on: find every profile carrying this tag.
            e.HasIndex(x => new { x.Kind, x.Value });
            e.HasIndex(x => x.ProfileId);

            // A profile cannot hold the same tag twice.
            e.HasIndex(x => new { x.ProfileId, x.Kind, x.Value }).IsUnique();
        });

        b.Entity<ProfileHookEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.ProfileId);
        });

        b.Entity<ProfileImageEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Caption).HasMaxLength(120);
            e.HasIndex(x => x.ProfileId);
            e.HasIndex(x => x.ImageId);
        });

        b.Entity<ProfileLinkEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(160);
            e.HasIndex(x => x.ProfileId);
            e.HasIndex(x => x.OtherProfileId);

            // One claim per pair; changing the kind edits the existing row.
            e.HasIndex(x => new { x.ProfileId, x.OtherProfileId }).IsUnique();

            e.HasOne(x => x.Profile).WithMany()
                .HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(b);
    }
}
