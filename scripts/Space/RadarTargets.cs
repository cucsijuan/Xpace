using Godot;

namespace Xpace.Space;

/// <summary>Something a radar can see.</summary>
public interface IRadarTarget
{
    ushort RadarId { get; }
    Vector3 GlobalPosition { get; }
    Vector3 Velocity { get; }
    bool Alive { get; }
    /// <summary>How big it looks to a radar, m².</summary>
    float RadarCrossSection { get; }
    /// <summary>A radar has it locked, this step.</summary>
    void Paint();
}

/// <summary>Radar ids: every echo gets its own, as a radar's tracker numbers them.</summary>
public static class RadarIds
{
    private static ushort _next = 1;

    public static ushort Next()
    {
        if (_next == 0)
            _next = 1;
        return _next++;
    }
}

/// <summary>Where rounds look for what they hit.</summary>
public static class Hitboxes
{
    /// <summary>The physics layer of the areas rounds hit, on any kind of ship; the damage goes to the nearest <see cref="IDamageable"/> above the area.</summary>
    public const uint RoundLayer = 2;
}

/// <summary>Something weapons can hurt: rounds, a warhead's blast.</summary>
public interface IDamageable
{
    void Hit(float damage);
}
