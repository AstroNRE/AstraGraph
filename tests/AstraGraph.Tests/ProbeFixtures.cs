namespace AstraGraph.Tests.Probes;

public sealed class UserProbe
{
    public string User { get; set; } = string.Empty;
}

public sealed class HitListProbe
{
    public List<EntityUid> HitEntities { get; set; } = [];
}

public sealed class WeaponProbe
{
    public EntityUid? Weapon { get; set; }
}

public sealed class MeleeProbe
{
    public EntityUid User { get; set; }
    public List<EntityUid> HitEntities { get; set; } = [];
}

public sealed class ProjectileProbe
{
    public EntityUid? Weapon { get; set; }
    public EntityUid Target { get; set; }
}

public sealed class HitscanProbe
{
    public HitscanData Data { get; set; } = new();
}

public sealed class HitscanData
{
    public EntityUid Gun { get; set; }
    public EntityUid? HitEntity { get; set; }
}

public readonly struct EntityUid
{
    public EntityUid(int id) => Id = id;
    public int Id { get; }
}
