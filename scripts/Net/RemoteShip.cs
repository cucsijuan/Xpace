using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A ship someone else flies: no computer and no physics here, only its
/// owner's snapshots, interpolated (<see cref="RemoteTrack"/>). The model
/// and the hitboxes (box collision shapes) go below it.
/// </summary>
public partial class RemoteShip : Node3D, INetEntity
{
    public RemoteTrack Track { get; } = new();

    public byte NetId { get; set; }
    public bool IsLocal => false;

    /// <summary>A session advances the track; alone, the ship does it itself.</summary>
    public bool Driven { get; set; }

    public Hitbox[] Hitboxes { get; private set; }
    public bool Hittable => true;

    /// <summary>Metres a second, world axes, as last shown (for sensors).</summary>
    public Vector3 Velocity { get; private set; }

    private FloatingOrigin _origin;

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        Track.TickSeconds = 1f / Engine.PhysicsTicksPerSecond;
        Hitboxes = Hitbox.FromShapes(this);
        Visible = false;                        // until there is something to show
    }

    public ShipSnapshot Snapshot(uint tick) => default;

    public void TakeHit(HitEvent hit) { }      // its owner takes it

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
        Visible = true;
    }
}
