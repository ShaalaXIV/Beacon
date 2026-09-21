namespace Beacon.Server.Data.Entities;

/// <summary>A character claimed by an account. Unique per (name, world) across the whole server.</summary>
public class CharacterEntity
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountEntity? Account { get; set; }

    public string Name { get; set; } = string.Empty;

    public uint WorldId { get; set; }

    public string WorldName { get; set; } = string.Empty;

    public string DataCenter { get; set; } = string.Empty;

    public DateTimeOffset LinkedAt { get; set; }
}
