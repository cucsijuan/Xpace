using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A ship someone else flies: no computer and no physics here, only its
/// owner's snapshots, interpolated (<see cref="RemoteTrack"/>). The model
/// and the hitboxes (box collision shapes) go below it.
///
/// The sensors here sense it like any ship: radars see its echo, seekers
/// its heat (the hull's, and its motor's as hard as the snapshots say it
/// pushes), warning receivers hear its radar (a <see cref="RadarEmitter"/>
/// it makes below itself, doing what the owner's radar does). Its
/// engines' plumes (<see cref="EnginePlume"/> nodes below it) glow as hard as
/// the snapshots say it pushes. A warhead's
/// fuse goes off on it; the damage is the host's to judge and its owner's
/// to take.
/// </summary>
public partial class RemoteShip : Node3D, INetEntity, IRadarTarget, IHeatSource, IDamageable
{
    [Export] public float RadarCrossSection { get; set; } = 4f;   // m²
    [Export] public float HullHeat = 8f;            // as a ship's signature
    [Export] public float MotorHeat = 80f;
    [Export] public float FrontHeat = 0.3f;
    [Export] public string RadarSignature = "F";    // how its radar's pulses look to a warning receiver
    [Export] public float RadarPowerAt1Km = 4e4f;

    public RemoteTrack Track { get; } = new();

    public ushort NetId { get; set; }
    public bool IsLocal => false;

    /// <summary>A session advances the track; alone, the ship does it itself.</summary>
    public bool Driven { get; set; }

    public Hitbox[] Hitboxes { get; private set; }

    /// <summary>Metres a second, world axes, as last shown (for sensors).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Its motor's push as last shown, 0 to 1.</summary>
    public float Thrust { get; private set; }

    public ushort RadarId { get; private set; }
    public bool Alive => Visible;

    private FloatingOrigin _origin;
    private RadarEmitter _radar;
    private EnginePlume[] _plumes;

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        Track.TickSeconds = 1f / Engine.PhysicsTicksPerSecond;
        Hitboxes = Hitbox.FromShapes(this);
        Visible = false;                        // until there is something to show
        RadarId = RadarIds.Next();
        AddToGroup("radar_targets");
        AddToGroup("heat_sources");
        _radar = new RadarEmitter { Name = "Radar", Signature = RadarSignature, PowerAt1Km = RadarPowerAt1Km };
        AddChild(_radar);
        _radar.Follow(RadarMode.Off, null, false);
        _plumes = FindChildren("*", "", true, false).OfType<EnginePlume>().ToArray();
    }

    public void Paint() { }

    public void Hit(float damage) { }           // the host judges it, the owner takes it

    public ShipSnapshot Snapshot(uint tick) => default;

    public void TakeHit(HitEvent hit) { }      // its owner takes it

    public float HeatAt(Vector3 from, out Vector3 direction)
    {
        float heat = Heat.Point(GlobalPosition, 1, from, out direction);
        if (!Visible)
            return 0;
        float behind = Mathf.Max(0, -direction.Dot(GlobalBasis.Z.Normalized()));
        return heat * (HullHeat + MotorHeat * Thrust * (FrontHeat + (1 - FrontHeat) * behind));
    }

    /// <summary>A packet from the owner's <see cref="ShipStateSender"/>.</summary>
    public void Receive(byte[] packet)
    {
        var reader = new BitReader(packet);
        ShipSnapshot s = ShipSnapshot.Read(reader);
        if (!reader.Overrun)
            Track.Receive(s, SectorClock.Instance?.Tick ?? 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Driven)
            Track.Advance(SectorClock.Instance?.Tick ?? 0);
        if (!Track.ShownTick.HasValue)
            return;
        RemoteTrack.Pose p = Track.Sample();
        GlobalTransform = new Transform3D(new Basis(p.Orientation), (_origin?.Origin ?? default).To(p.Position));
        Velocity = p.Velocity;
        Thrust = p.Destroyed ? 0 : p.Thrust;
        foreach (EnginePlume plume in _plumes)
            plume.Thrust = Thrust;
        if (p.Destroyed && Visible)
            Explosion.Spawn(GetParent(), GlobalPosition, 30f, 1.6, 16f);     // blown up on its owner's machine
        Visible = !p.Destroyed;
        _radar.Follow(p.Radar, p.Radar == RadarMode.Track ? SessionNode.Instance?.NodeOf(p.RadarTarget) : null, p.RadarGuiding);
    }
}
