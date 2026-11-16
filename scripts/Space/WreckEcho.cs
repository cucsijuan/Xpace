using Godot;

namespace Xpace.Space;

/// <summary>
/// What a dead hull is to a radar: an echo as big as the hull's (m²), and nothing else: no motor, no heat, no wake.
/// It lies still where it was left (<see cref="StorySites"/>).
/// </summary>
public partial class WreckEcho : Node3D, IRadarTarget
{
    /// <summary>The hull's echo, m² (the same as the ship's when it flew).</summary>
    [Export] public float RadarCrossSection { get; set; } = 20f;

    public ushort RadarId { get; private set; }
    public Vector3 Velocity => Vector3.Zero;
    public bool Alive => true;
    public void Paint() { }

    public override void _Ready()
    {
        RadarId = RadarIds.Next();
        AddToGroup("radar_targets");
    }
}
