using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// Joins a <see cref="Session"/> to the game. Place it under the floating
/// origin; nothing changes until <see cref="Start"/> (alone, the game
/// plays as before).
///
/// Every ship with a <see cref="NetBody"/> under the origin takes part,
/// whatever kind of ship it is:
/// - the one flown by this machine's player (<see cref="NetOwner.Pilot"/>)
///   is named by the local peer;
/// - the ones the game flies (<see cref="NetOwner.Host"/>) are the host's,
///   numbered from <see cref="FirstHostId"/> in the order of their paths in
///   the scene, the same on every machine; on a guest they are puppets.
/// Other players' ships appear as <see cref="RemoteShipScene"/>s (a plain
/// box when not set).
///
/// Rounds go through the session (<see cref="Projectiles.Fire"/> calls
/// <see cref="Fire"/>): here they only fly for show, and the host's hit
/// events do the damage, to whichever ship they hit.
/// </summary>
public partial class SessionNode : Node
{
    /// <summary>Ids below this are players' ships (the peer flying them), from it the host's ships.</summary>
    public const byte FirstHostId = 16;

    [Export] public PackedScene RemoteShipScene;

    public static SessionNode Instance { get; private set; }

    public Session Session { get; private set; }
    public bool Active => Session != null;

    private FloatingOrigin _origin;

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

        Node root = (Node)_origin ?? GetParent();
        var bodies = root.FindChildren("*", nameof(Node), true, false).OfType<NetBody>()
            .OrderBy(b => root.GetPathTo(b).ToString(), System.StringComparer.Ordinal);
        byte next = FirstHostId;
        foreach (NetBody body in bodies)
        {
            if (body.OwnedBy == NetOwner.Pilot)
                body.Join(peer, true, Session.TickSeconds);
            else
                body.Join(next++, Session.IsHost, Session.TickSeconds);
            Session.Add(body);
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

    private INetEntity Spawn(byte id)
    {
        if (id >= FirstHostId)
            return null;                            // the host's ships are in every machine's scene already
        RemoteShip ship = RemoteShipScene?.Instantiate<RemoteShip>() ?? PlainShip();
        ship.Name = $"Player{id}";
        ship.NetId = id;
        ship.Driven = true;
        (_origin as Node ?? GetParent()).AddChild(ship);
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

    private void Confirmed(HitEvent hit) => Projectiles.Instance?.Remove(hit.Shooter, hit.Sequence);
}
