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
    /// <summary>The machine that made it while playing (a missile: the one that fired it).</summary>
    Maker,
}

/// <summary>
/// The script of something in a session that has more to it than where it
/// is: what its owner's machine reads of it, and what a puppet is told.
/// </summary>
public interface INetShip
{
    /// <summary>Owned here: what its motor pushes now, 0 to 1 of the most.</summary>
    float Thrust { get; }

    /// <summary>Owned here, as it leaves: it blew up.</summary>
    bool Exploded { get; }

    /// <summary>A puppet, each tick: the owner's state (the node is already where it shows).</summary>
    void Follow(RemoteTrack.Pose pose);

    /// <summary>A puppet whose original is gone: show how it ended (it is removed after).</summary>
    void End(bool exploded);
}

/// <summary>A radar others can tell is on, and what it tracks.</summary>
public interface INetRadar
{
    RadarMode Mode { get; }

    /// <summary>What it tracks (null if nothing, or not a ship).</summary>
    Node Tracked { get; }

    /// <summary>A puppet's radar: do what the owner's does.</summary>
    void Follow(RadarMode mode, Node3D tracked);
}

/// <summary>
/// Makes the ship it is placed under a ship of the network session,
/// whatever kind of ship it is: the player's, an AI fighter, a capital
/// ship, a missile. Nothing here knows the kind; any ship can shoot any
/// other.
///
/// On the machine that owns it, it reads the ship's motion (from how it
/// moved each tick) into snapshots, with its motor's push and its radar's
/// state (<see cref="INetShip"/>, <see cref="INetRadar"/>, if the ship has
/// them). On the others the ship is a puppet: its own physics process is
/// switched off and it follows the owner's snapshots. Rounds and blasts
/// hit its box collision shapes; damage goes to the first
/// <see cref="IDamageable"/> on the ship or below it. While its
/// <see cref="IRadarTarget"/> (if it has one) is not alive the snapshots
/// say it is destroyed: puppets hide and rounds pass through.
///
/// A ship made while playing (<see cref="NetOwner.Maker"/>: a missile)
/// joins the session by itself when it enters the scene, and leaves it
/// when it goes; its puppets show its end when they reach the tick it
/// ended at.
/// </summary>
public partial class NetBody : Node, INetEntity
{
    [Export] public NetOwner OwnedBy = NetOwner.Host;

    public ushort NetId { get; set; }
    public bool IsLocal { get; private set; } = true;
    public RemoteTrack Track { get; private set; }
    public Hitbox[] Hitboxes { get; private set; } = System.Array.Empty<Hitbox>();
    public bool Joined { get; private set; }

    /// <summary>Made by the session to show another machine's ship: it must not join as its own.</summary>
    public bool Puppet { get; set; }

    /// <summary>The ship this makes networked.</summary>
    public Node3D Ship { get; private set; }

    private IDamageable _hull;
    private IRadarTarget _signature;
    private INetShip _script;
    private INetRadar _radar;
    private FloatingOrigin _origin;
    private SectorPoint _lastPosition;
    private Quaternion _lastOrientation;
    private bool _moved;
    private uint? _endTick;
    private bool _endExploded;
    private int _endWait;

    public override void _Ready()
    {
        Ship = GetParent<Node3D>();
        for (Node n = Ship; n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        if (OwnedBy == NetOwner.Maker && !Puppet)
            CallDeferred(MethodName.Made);              // the ship's own _Ready (after this one) has run by then
    }

    private void Made()
    {
        if (SessionNode.Instance?.Active == true && IsInstanceValid(Ship) && Ship.IsInsideTree())
        {
            SessionNode.Instance.Made(this);
            Ship.TreeExiting += () => SessionNode.Instance?.Gone(this, _script?.Exploded ?? false);
        }
    }

    /// <summary>The session decides: owned here, or a puppet of another machine's ship.</summary>
    public void Join(ushort id, bool local, float tickSeconds)
    {
        // The ship is built by now (its own _Ready runs after this node's)
        var parts = new Node[] { Ship }.Concat(Ship.FindChildren("*", "", true, false)).ToArray();
        _hull = parts.OfType<IDamageable>().FirstOrDefault();
        _signature = parts.OfType<IRadarTarget>().FirstOrDefault();
        _script = parts.OfType<INetShip>().FirstOrDefault();
        _radar = parts.OfType<INetRadar>().FirstOrDefault();
        Hitboxes = Hitbox.FromShapes(Ship);
        NetId = id;
        IsLocal = local;
        Track = local ? null : new RemoteTrack { TickSeconds = tickSeconds };
        Ship.SetPhysicsProcess(local);
        if (!local)
            _radar?.Follow(RadarMode.Off, null);       // quiet until the owner's first word
        _moved = false;
        Joined = true;
    }

    /// <summary>A radar has this ship locked, somewhere in the session.</summary>
    public void Paint() => _signature?.Paint();

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
        RadarMode radar = _radar?.Mode ?? RadarMode.Off;
        ushort tracked = ShipSnapshot.NoTarget;
        if (radar == RadarMode.Track && SessionNode.Instance?.IdOf(_radar.Tracked) is ushort id)
            tracked = id;
        return new ShipSnapshot
        {
            Tick = tick, Position = position, Orientation = orientation, Velocity = velocity, AngularVelocity = spin,
            Destroyed = !(_signature?.Alive ?? true), Thrust = _script?.Thrust ?? 0,
            Radar = radar, RadarTarget = tracked,
        };
    }

    public void TakeHit(HitEvent hit) => _hull?.Hit(hit.Damage);

    /// <summary>A puppet: its original ended at <paramref name="tick"/>; it ends when it is shown there.</summary>
    public void EndAt(uint tick, bool exploded)
    {
        _endTick = tick;
        _endExploded = exploded;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsLocal || Track?.ShownTick is not double shown)
        {
            if (!IsLocal && _endTick.HasValue)
                End();                                  // never shown: nothing to wait for
            return;
        }
        RemoteTrack.Pose p = Track.Sample(shown);
        Ship.GlobalTransform = new Transform3D(new Basis(p.Orientation), (_origin?.Origin ?? default).To(p.Position));
        Ship.Visible = !p.Destroyed;
        _script?.Follow(p);
        _radar?.Follow(p.Radar, p.Radar == RadarMode.Track ? SessionNode.Instance?.NodeOf(p.RadarTarget) : null);
        if (_endTick is uint end && (shown >= end || ++_endWait > 60))
            End();
    }

    private void End()
    {
        _script?.End(_endExploded);
        SessionNode.Instance?.Session?.Remove(NetId);
        Ship.QueueFree();
        _endTick = null;
    }

    /// <summary>Whether a node is (on) a ship another machine owns, shown here as a puppet.</summary>
    public static bool IsPuppet(Node node) => Of(node) is NetBody { IsLocal: false, Joined: true };

    /// <summary>The NetBody of the ship a node is on, if any.</summary>
    public static NetBody Of(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
        {
            if (n is NetBody self)
                return self;
            foreach (Node child in n.GetChildren())
                if (child is NetBody body)
                    return body;
        }
        return null;
    }
}
