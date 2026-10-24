using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// Joins a <see cref="Session"/> to the game. Place it under the floating
/// origin; nothing changes until <see cref="Start"/> (alone, the game
/// plays as before).
///
/// - The player's ship (<see cref="ShipPath"/>) is this machine's entity,
///   named by the local peer.
/// - The drones under the origin are the host's, numbered from
///   <see cref="FirstDroneId"/> in the order of their names, the same on
///   every machine; on a guest they are puppets following the host.
/// - Other players' ships appear as <see cref="RemoteShipScene"/>s (a plain
///   box when not set).
/// - Rounds go through the session (<see cref="Projectiles.Fire"/> calls
///   <see cref="Fire"/>): here they only fly for show, and the host's hit
///   events do the damage.
/// </summary>
public partial class SessionNode : Node
{
    public const byte FirstDroneId = 16;

    [Export] public NodePath ShipPath = "../Ship";
    [Export] public PackedScene RemoteShipScene;

    public static SessionNode Instance { get; private set; }

    public Session Session { get; private set; }
    public bool Active => Session != null;

    private FloatingOrigin _origin;
    private ShipBody _ship;
    private readonly Dictionary<byte, Node3D> _shown = new();

    public override void _EnterTree()
    {
        Instance = this;
        ProcessPhysicsPriority = -900;          // after the sector clock, before what shows the ships
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        _ship = GetNodeOrNull<ShipBody>(ShipPath);
        if (SectorClock.Instance == null)
            GetParent().CallDeferred(Node.MethodName.AddChild, new SectorClock { Name = "SectorClock" });
    }

    /// <summary>Joins the session as <paramref name="peer"/> (0 is the host) over <paramref name="transport"/>.</summary>
    public void Start(byte peer, INetTransport transport)
    {
        Session = new Session(peer, transport) { TickSeconds = 1f / Engine.PhysicsTicksPerSecond };
        Session.SpawnRemote = Spawn;
        Session.RoundFired += Show;
        Session.HitConfirmed += Confirmed;
        if (_ship != null)
            Session.Add(new ShipEntity(peer, _ship, this));
        byte id = FirstDroneId;
        foreach (Drone d in Drones())
        {
            var entity = new DroneEntity(id++, d, Session.IsHost, this);
            if (!Session.IsHost)
                d.Puppet = entity.Track;
            _shown[entity.NetId] = d;
            Session.Add(entity);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active && SectorClock.Instance is SectorClock clock)
            Session.Tick(clock.Tick);
    }

    /// <summary>A round leaves a muzzle here (scene position and velocity).</summary>
    public void Fire(Vector3 origin, Vector3 velocity, float damage)
    {
        FireEvent sent = Session.Fire(SectorClock.Instance?.Tick ?? 0, Sector(origin), velocity, damage);
        Show(sent);
    }

    public SectorPoint Sector(Vector3 scene) => (_origin?.Origin ?? default) + scene;

    public Vector3 Scene(SectorPoint p) => (_origin?.Origin ?? default).To(p);

    private IEnumerable<Drone> Drones() =>
        (_origin ?? GetParent()).FindChildren("*", "", true, false).OfType<Drone>().OrderBy(d => d.Name.ToString());

    private INetEntity Spawn(byte id)
    {
        if (id >= FirstDroneId)
            return null;                            // drones are in every machine's scene already
        RemoteShip ship = RemoteShipScene?.Instantiate<RemoteShip>() ?? PlainShip();
        ship.Name = $"Player{id}";
        ship.NetId = id;
        ship.Driven = true;
        (_origin as Node ?? GetParent()).AddChild(ship);
        _shown[id] = ship;
        return ship;
    }

    private static RemoteShip PlainShip()
    {
        var ship = new RemoteShip();
        var size = new Vector3(5, 3, 10);
        ship.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size } });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return ship;
    }

    /// <summary>Someone else's round: it flies here for show, from where it is by now.</summary>
    private void Show(FireEvent e)
    {
        uint now = SectorClock.Instance?.Tick ?? e.Tick;
        float age = (now - e.Tick) * Session.TickSeconds;
        Projectiles.Instance?.Show(Scene(e.At(now - e.Tick, Session.TickSeconds)), e.Velocity, age, e.Shooter, e.Sequence);
    }

    private void Confirmed(HitEvent hit)
    {
        Projectiles.Instance?.Remove(hit.Shooter, hit.Sequence);
        if (_shown.GetValueOrDefault(hit.Target) is Drone d && !Session.IsHost)
            d.Hit(hit.Damage);                      // the puppet flashes and blows up with the host's
    }

    /// <summary>The player's ship, flown here.</summary>
    private sealed class ShipEntity : INetEntity
    {
        private readonly ShipBody _ship;
        private readonly SessionNode _node;
        private readonly IDamageable _hull;

        public ShipEntity(byte id, ShipBody ship, SessionNode node)
        {
            NetId = id;
            _ship = ship;
            _node = node;
            _hull = ship.FindChildren("*", "", true, false).OfType<IDamageable>().FirstOrDefault();
            Hitboxes = Hitbox.FromShapes(ship);
        }

        public byte NetId { get; }
        public bool IsLocal => true;
        public RemoteTrack Track => null;
        public Hitbox[] Hitboxes { get; }

        public bool Hittable => true;

        public ShipSnapshot Snapshot(uint tick)
        {
            ShipState s = _ship.State;
            return new ShipSnapshot
            {
                Tick = tick, Position = _node.Sector(s.Position), Orientation = s.Orientation,
                Velocity = s.Velocity, AngularVelocity = s.AngularVelocity,
            };
        }

        public void TakeHit(HitEvent hit) => _hull?.Hit(hit.Damage);
    }

    /// <summary>A drone: the host flies it, guests show it.</summary>
    private sealed class DroneEntity : INetEntity
    {
        private readonly Drone _drone;
        private readonly SessionNode _node;

        public DroneEntity(byte id, Drone drone, bool local, SessionNode node)
        {
            NetId = id;
            _drone = drone;
            _node = node;
            IsLocal = local;
            Track = local ? null : new RemoteTrack { TickSeconds = node.Session.TickSeconds };
            Hitboxes = Hitbox.FromShapes(drone);
        }

        public byte NetId { get; }
        public bool IsLocal { get; }
        public RemoteTrack Track { get; }
        public Hitbox[] Hitboxes { get; }

        public bool Hittable => _drone.Alive;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = _node.Sector(_drone.GlobalPosition),
            Orientation = _drone.GlobalBasis.GetRotationQuaternion(), Velocity = _drone.Velocity,
        };

        public void TakeHit(HitEvent hit) => _drone.Hit(hit.Damage);
    }
}
