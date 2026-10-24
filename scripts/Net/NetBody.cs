using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>Which machine simulates a ship in a session.</summary>
public enum NetOwner
{
    /// <summary>The machine of the player flying it: on each machine, its own player's ship.</summary>
    Pilot,
    /// <summary>The host: ships flown by the game (AI fighters, capital ships, test drones).</summary>
    Host,
}

/// <summary>
/// Makes the ship it is placed under a ship of the network session,
/// whatever kind of ship it is: the player's, an AI fighter, a capital
/// ship. Nothing here knows the kind; any ship can shoot any other.
///
/// On the machine that owns it, it reads the ship's motion (from how it
/// moved each tick) into snapshots. On the others the ship is a puppet:
/// its own physics process is switched off and it follows the owner's
/// snapshots. Rounds hit its box collision shapes; damage goes to the
/// first <see cref="IDamageable"/> on the ship or below it. While its
/// <see cref="IRadarTarget"/> (if it has one) is not alive the snapshots
/// say it is destroyed: puppets hide and rounds pass through.
/// </summary>
public partial class NetBody : Node, INetEntity
{
    [Export] public NetOwner OwnedBy = NetOwner.Host;

    public byte NetId { get; set; }
    public bool IsLocal { get; private set; } = true;
    public RemoteTrack Track { get; private set; }
    public Hitbox[] Hitboxes { get; private set; }

    /// <summary>The ship this makes networked.</summary>
    public Node3D Ship { get; private set; }

    private IDamageable _hull;
    private IRadarTarget _signature;
    private FloatingOrigin _origin;
    private SectorPoint _lastPosition;
    private Quaternion _lastOrientation;
    private bool _moved;

    public override void _Ready()
    {
        Ship = GetParent<Node3D>();
        for (Node n = Ship; n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
    }

    /// <summary>The session decides: owned here, or a puppet of another machine's ship.</summary>
    public void Join(byte id, bool local, float tickSeconds)
    {
        // The ship is built by now (its own _Ready runs after this node's)
        var parts = new Node[] { Ship }.Concat(Ship.FindChildren("*", "", true, false));
        _hull = parts.OfType<IDamageable>().FirstOrDefault();
        _signature = parts.OfType<IRadarTarget>().FirstOrDefault();
        Hitboxes = Hitbox.FromShapes(Ship);
        NetId = id;
        IsLocal = local;
        Track = local ? null : new RemoteTrack { TickSeconds = tickSeconds };
        Ship.SetPhysicsProcess(local);
        _moved = false;
    }

    public ShipSnapshot Snapshot(uint tick)
    {
        // The motion from the last tick to this one: the same for any kind of ship
        SectorPoint position = (_origin?.Origin ?? default) + Ship.GlobalPosition;
        Quaternion orientation = Ship.GlobalBasis.GetRotationQuaternion();
        float dt = Track?.TickSeconds ?? 1f / Engine.PhysicsTicksPerSecond;
        Vector3 velocity = Vector3.Zero, spin = Vector3.Zero;
        if (_moved)
        {
            velocity = _lastPosition.To(position) / dt;
            Quaternion turn = orientation * _lastOrientation.Inverse();
            if (turn.W < 0)
                turn = -turn;
            float angle = turn.GetAngle();
            if (angle > 1e-6f)
                spin = turn.GetAxis() * (angle / dt);
        }
        _lastPosition = position;
        _lastOrientation = orientation;
        _moved = true;
        return new ShipSnapshot
        {
            Tick = tick, Position = position, Orientation = orientation, Velocity = velocity, AngularVelocity = spin,
            Destroyed = !(_signature?.Alive ?? true),
        };
    }

    public void TakeHit(HitEvent hit) => _hull?.Hit(hit.Damage);

    public override void _PhysicsProcess(double delta)
    {
        if (IsLocal || Track?.ShownTick is not double shown)
            return;
        RemoteTrack.Pose p = Track.Sample(shown);
        Ship.GlobalTransform = new Transform3D(new Basis(p.Orientation), (_origin?.Origin ?? default).To(p.Position));
        Ship.Visible = !p.Destroyed;
    }
}
