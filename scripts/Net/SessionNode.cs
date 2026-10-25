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
///   numbered from 1 in the order of their paths in the scene, the same on
///   every machine; on a guest they are puppets;
/// - the ones made while playing (<see cref="NetOwner.Maker"/>: missiles)
///   join as they come, and the other machines make their
///   <see cref="Spawnables"/> scene as a puppet.
/// Other players' ships appear as <see cref="RemoteShipScene"/>s (a plain
/// box when not set), and go when they leave. A guest's own ship starts
/// <see cref="PilotSpacing"/> times its peer from where the scene puts it,
/// so the players do not all start in the same place. A guest's sector
/// clock follows the host's.
///
/// Rounds go through the session (<see cref="Projectiles.Fire"/> calls
/// <see cref="Fire"/>): here they only fly for show, and the host's hit
/// events do the damage, to whichever ship they hit. Warheads that go off
/// here are judged by the host the same way (<see cref="Blast"/>). Chaff
/// and flares dropped here (<see cref="Countermeasures.Drop"/>) are made on
/// every machine. A ship a radar somewhere has locked is painted here, if
/// it is this machine's.
/// </summary>
public partial class SessionNode : Node
{
    /// <summary>The host's first scene ship (the ships of peer 0 from 1 on).</summary>
    public const ushort FirstHostId = 1;

    [Export] public PackedScene RemoteShipScene;
    [Export] public Vector3 PilotSpacing = new(40, 0, 0);

    /// <summary>What a session can make on every machine: missiles, chaff, flares (the same list everywhere).</summary>
    [Export] public Godot.Collections.Array<PackedScene> Spawnables = new();

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

    private Node World => (Node)_origin ?? GetParent();

    private uint Now => SectorClock.Instance?.Tick ?? 0;

    /// <summary>Joins the session as <paramref name="peer"/> (0 is the host) over <paramref name="transport"/>.</summary>
    public void Start(byte peer, INetTransport transport)
    {
        Session = new Session(peer, transport) { TickSeconds = 1f / Engine.PhysicsTicksPerSecond };
        Session.SpawnRemote = Spawn;
        Session.SpawnDynamic = SpawnPuppet;
        Session.RoundFired += Show;
        Session.HitConfirmed += Confirmed;
        Session.Despawned += (e, tick, exploded) => (e as NetBody)?.EndAt(tick, exploded);
        Session.Dropped += Dropped;
        Session.Forgotten += e =>
        {
            if (e is RemoteShip ship)
                ship.QueueFree();
            else if (e is NetBody body && IsInstanceValid(body.Ship))
                body.Ship.QueueFree();
        };
        Session.AdjustClock = ticks =>
        {
            if (SectorClock.Instance is SectorClock clock)
                clock.Tick = (uint)(clock.Tick + ticks);
        };

        Node root = World;
        var bodies = root.FindChildren("*", nameof(Node), true, false).OfType<NetBody>()
            .Where(b => b.OwnedBy != NetOwner.Maker)
            .OrderBy(b => root.GetPathTo(b).ToString(), System.StringComparer.Ordinal);
        ushort next = FirstHostId;
        foreach (NetBody body in bodies)
        {
            if (body.OwnedBy == NetOwner.Pilot)
            {
                body.Join(NetIds.PlayerShip(peer), true, Session.TickSeconds);
                body.Ship.GlobalPosition += PilotSpacing * peer;
                foreach (Node n in body.Ship.GetChildren())
                    if (n is Xpace.Ship.ShipHull hull)
                        hull.Spawn = body.Ship.GlobalTransform;     // a new ship comes back here
            }
            else
                body.Join(next++, Session.IsHost, Session.TickSeconds);
            Session.Add(body);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active || SectorClock.Instance is not SectorClock clock)
            return;
        Session.Tick(clock.Tick);

        // Radars elsewhere that track a ship of this machine paint it here
        foreach (INetEntity e in Session.Entities)
            if (!e.IsLocal && e.Track.ShownTick.HasValue && e.Track.Sample() is { Radar: RadarMode.Track } p &&
                Session.Entity(p.RadarTarget) is NetBody { IsLocal: true } target)
                target.Paint();
    }

    /// <summary>A round leaves a muzzle of <paramref name="shooter"/> here (scene position and velocity).</summary>
    public void Fire(Vector3 origin, Vector3 velocity, float damage, Node shooter = null, byte kind = 0)
    {
        ushort ship = IdOf(shooter) ?? NetIds.PlayerShip(Session.LocalPeer);
        Show(Session.Fire(Now, ship, Sector(origin), velocity, damage, kind));
    }

    /// <summary>The warhead of a missile flown here went off (scene position, and when in the tick).</summary>
    public void Blast(Node missile, Vector3 at, float fraction, float damage, float lethalRadius, float blastRadius)
    {
        ushort id = IdOf(missile) ?? NetIds.PlayerShip(Session.LocalPeer);
        Session.Blast(Now, fraction, id, Sector(at), damage, lethalRadius, blastRadius);
    }

    /// <summary>Something thrown out here (a <see cref="Spawnables"/> scene) is made on the other machines too.</summary>
    public void Drop(PackedScene scene, Vector3 at, Vector3 velocity)
    {
        int index = Index(scene);
        if (index >= 0)
            Session.Drop(Now, (byte)index, Sector(at), velocity);
        else
            GD.PushWarning($"session: {scene?.ResourcePath} is not in the spawnables; the others will not see it");
    }

    /// <summary>A ship made here while playing (its NetBody calls this): it joins, and the others make its puppet.</summary>
    public void Made(NetBody body)
    {
        int index = Index(body.Ship.SceneFilePath);
        if (index < 0)
        {
            GD.PushWarning($"session: {body.Ship.SceneFilePath} is not in the spawnables; the others will not see it");
            return;
        }
        body.Join(Session.NewDynamicId(), true, Session.TickSeconds);
        Session.Spawn(body, (byte)index);
    }

    /// <summary>A ship made here leaves (its NetBody calls this).</summary>
    public void Gone(NetBody body, bool exploded)
    {
        if (Active && body.Joined && body.IsLocal && Session.Entity(body.NetId) == body)
            Session.Despawn(body.NetId, Now, exploded);
    }

    public SectorPoint Sector(Vector3 scene) => (_origin?.Origin ?? default) + scene;

    public Vector3 Scene(SectorPoint p) => (_origin?.Origin ?? default).To(p);

    /// <summary>The session id of the ship a node is on (its NetBody's, or a remote player's), if any.</summary>
    public ushort? IdOf(Node node)
    {
        if (node == null || !IsInstanceValid(node))
            return null;                // a radar may still hold a ship freed this frame
        if (NetBody.Of(node) is NetBody { Joined: true } body)
            return body.NetId;
        for (Node n = node; n != null; n = n.GetParent())
            if (n is INetEntity e)
                return e.NetId;
        return null;
    }

    /// <summary>What shows a session id here.</summary>
    public Node3D NodeOf(ushort id) => Session?.Entity(id) switch
    {
        NetBody body => body.Ship,
        Node3D shown => shown,
        _ => null,
    };

    private int Index(PackedScene scene) => scene == null ? -1 : Index(scene.ResourcePath);

    private int Index(string path)
    {
        for (int i = 0; i < Spawnables.Count; i++)
            if (Spawnables[i]?.ResourcePath == path)
                return i;
        return -1;
    }

    private INetEntity Spawn(ushort id)
    {
        RemoteShip ship = RemoteShipScene?.Instantiate<RemoteShip>() ?? PlainShip();
        ship.Name = $"Player{NetIds.Owner(id)}";
        ship.NetId = id;
        ship.Driven = true;
        World.AddChild(ship);
        return ship;
    }

    /// <summary>Another machine made something: its scene here, as a puppet.</summary>
    private INetEntity SpawnPuppet(ushort id, byte scene)
    {
        if (scene >= Spawnables.Count || Spawnables[scene]?.Instantiate() is not Node3D ship)
            return null;
        if (ship.GetChildren().OfType<NetBody>().FirstOrDefault() is not NetBody body)
        {
            ship.Free();
            return null;
        }
        body.Puppet = true;
        ship.Visible = false;                   // until its first snapshot
        World.AddChild(ship);
        body.Join(id, false, Session.TickSeconds);
        return body;
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
        Projectiles.Instance?.Show(Scene(e.At(now - e.Tick, Session.TickSeconds)), e.Velocity, age, e.Shooter, e.Sequence, e.Kind);
    }

    private void Confirmed(HitEvent hit) => Projectiles.Instance?.Remove(hit.Shooter, hit.Sequence);

    /// <summary>Someone else dropped something: it is made here, already as old as it is by now.</summary>
    private void Dropped(DropEvent d)
    {
        if (d.Scene >= Spawnables.Count || Spawnables[d.Scene] is not PackedScene scene)
            return;
        float age = System.Math.Max(0, (int)(Now - d.Tick)) * Session.TickSeconds;
        Countermeasures.Make(World, scene, Scene(d.Position + d.Velocity * age), d.Velocity, age);
    }
}
