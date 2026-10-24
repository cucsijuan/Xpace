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

/// <summary>
/// A noise jammer: it raises the noise a radar hears, the more the closer it
/// is and the nearer it sits to where the radar looks. Anything in the
/// "jammers" group with this interface counts.
/// </summary>
public interface IJammer
{
    Vector3 GlobalPosition { get; }
    /// <summary>
    /// Jamming-to-noise ratio it causes in a reference radar at 1 km, looking
    /// straight at it; it falls with the square of the distance.
    /// </summary>
    float JammingAt1Km { get; }
}

/// <summary>A jammer pod: put it on (under) a ship to make that ship hard to track.</summary>
public partial class JammerPod : Node3D, IJammer
{
    [Export] public float JammingAt1Km = 400f;
    float IJammer.JammingAt1Km => JammingAt1Km;

    public override void _Ready() => AddToGroup("jammers");
}
