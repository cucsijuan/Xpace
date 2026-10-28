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
/// - the ones made while playing (<see cref="NetOwner.Maker"/>: missiles;
///   <see cref="NetBody.Spawned"/>: ships the host brings in) join as they
///   come, and the other machines make their <see cref="Spawnables"/> scene
///   as a puppet; a guest's own spawned host ships go when it joins.
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
///
/// Every machine's ships fight under the same eyes (<see cref="Combat"/>):
/// a round or a missile another machine fires goes off here too, the host's
/// hits name the ship that fired, and a ship that blew up on its owner's
/// machine was destroyed by the last ship to hit it (within
/// <see cref="BlameSeconds"/>). So a station's security, on the host, judges
/// a guest as it judges anyone, and what its radio says to a guest's ship
/// (a <see cref="RemoteRadio"/> hears it) reaches that guest's radio.
///
/// The host's stations are the stations: a guest's request for a dock goes
/// to the host's station of the same callsign, its clearance comes back to
/// the guest's radio, and the docks it opens and shuts open and shut on
/// every machine. Every ship's radio is as its owner has it: callsign (the
/// pilot's name, for players) and power.
/// </summary>
public partial class SessionNode : Node
{
    /// <summary>The host's first scene ship (the ships of peer 0 from 1 on).</summary>
    public const ushort FirstHostId = 1;

    [Export] public PackedScene RemoteShipScene;
    [Export] public Vector3 PilotSpacing = new(40, 0, 0);

    /// <summary>
    /// Whether the scene's own ships (<see cref="NetOwner.Host"/>: drones,
    /// AI fighters) stay in a session. Off, they go on every machine when
    /// the session starts, leaving only the players.
    /// </summary>
    [Export] public bool KeepSceneShips = true;

    /// <summary>A ship that blows up this soon after a hit was destroyed by whoever hit it.</summary>
    [Export] public float BlameSeconds = 10f;

    /// <summary>What a session can make on every machine: missiles, chaff, flares (the same list everywhere).</summary>
    [Export] public Godot.Collections.Array<PackedScene> Spawnables = new();

    /// <summary>
    /// The jammer models a ship may carry, the same list on every machine: a
    /// snapshot says which one a ship's jammer is by its place here, so a
    /// remote player's ship jams as hard as the model it has.
    /// </summary>
    [Export] public Godot.Collections.Array<Xpace.Ship.JammerSpec> Jammers = new();

    /// <summary>A jammer model's place in <see cref="Jammers"/> (by resource path; <see cref="ShipSnapshot.NoModel"/> if not there).</summary>
    public byte JammerIndex(Xpace.Ship.JammerSpec spec)
    {
        for (int i = 0; spec != null && i < Jammers.Count && i < ShipSnapshot.NoModel; i++)
            if (Jammers[i] == spec || (!string.IsNullOrEmpty(spec.ResourcePath) && Jammers[i]?.ResourcePath == spec.ResourcePath))
                return (byte)i;
        return ShipSnapshot.NoModel;
    }

    /// <summary>The model at a place in <see cref="Jammers"/>, if there is one.</summary>
    public Xpace.Ship.JammerSpec JammerModel(byte index) => index < Jammers.Count ? Jammers[index] : null;

    public static SessionNode Instance { get; private set; }

    public Session Session { get; private set; }
    public bool Active => Session != null;

    private FloatingOrigin _origin;
    private readonly System.Collections.Generic.Dictionary<ushort, (ushort By, uint At)> _lastHit = new();
    private readonly System.Collections.Generic.HashSet<ushort> _down = new();
    private readonly System.Collections.Generic.Dictionary<ushort, (IRadio Radio, string Sign, bool On)> _radios = new();
    private readonly System.Collections.Generic.Dictionary<ushort, (string Sign, bool On)> _theirRadios = new();

    public override void _EnterTree()
    {
        Instance = this;
        ProcessPhysicsPriority = -900;          // after the sector clock, before what shows the ships
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        HangarDock.Changed -= DockChanged;
        if (Session != null)
            Xpace.World.Universe.Current?.Detach();
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
        Xpace.World.Universe.Current?.Attach(Session);
        if (Session.Judge != null)
            Session.Judge.Solid = (a, b) => Obstacles.Blocked(Scene(a), Scene(b));
        Session.SpawnRemote = Spawn;
        Session.SpawnDynamic = SpawnPuppet;
        Session.RoundFired += Show;
        Session.RoundFired += e => Combat.Fire(NodeOf(e.Ship), Scene(e.Origin));
        Session.HitConfirmed += Confirmed;
        Session.RadioHeard += Heard;
        Session.RadioChanged += RadioChanged;
        Session.Requested += Requested;
        Session.DockCleared += DockCleared;
        Session.DockChanged += (station, number, open) =>
            StationRadio.Called(station)?.Docks.FirstOrDefault(d => d.Number == number)?.Show(open);
        if (Session.IsHost)
        {
            HangarDock.Changed += DockChanged;
            foreach (StationRadio station in StationRadio.All)
                foreach (HangarDock dock in station.Docks.Where(d => d.IsOpen))
                    DockChanged(dock);
        }
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
        var all = root.FindChildren("*", nameof(Node), true, false).OfType<NetBody>().ToList();
        if (!KeepSceneShips)
        {
            foreach (NetBody body in all.Where(b => b.OwnedBy == NetOwner.Host && !b.Spawned))
                body.Ship.QueueFree();
            all.RemoveAll(b => b.OwnedBy == NetOwner.Host && !b.Spawned);
        }
        var bodies = all.Where(b => b.OwnedBy != NetOwner.Maker && !b.Spawned)
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
        foreach (NetBody body in all.Where(b => b.Spawned && !b.Puppet))
            Made(body);                     // brought in before the session: the host's join now, a guest's go
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

        // The radios of the ships owned here, as they are
        foreach (INetEntity e in Session.Entities)
        {
            if (!e.IsLocal || e is not NetBody { Joined: true } body)
                continue;
            if (!_radios.TryGetValue(e.NetId, out var radio))
                radio = (body.Ship.FindChildren("*", "", true, false).OfType<IRadio>().FirstOrDefault(), null, false);
            if (radio.Radio is IRadio r && (r.Callsign != radio.Sign || r.RadioOn != radio.On))
            {
                radio = (r, r.Callsign, r.RadioOn);
                Session.SetRadio(e.NetId, radio.Sign, radio.On);
            }
            _radios[e.NetId] = radio;
        }

        // Ships of other machines that blew up there
        foreach (INetEntity e in Session.Entities)
        {
            if (e.IsLocal || !e.Track.ShownTick.HasValue)
                continue;
            bool down = e.Track.Sample().Destroyed;
            if (down && _down.Add(e.NetId))
            {
                Node3D by = _lastHit.TryGetValue(e.NetId, out var hit) && (Now - hit.At) * Session.TickSeconds < BlameSeconds
                    ? NodeOf(hit.By) : null;
                if (NodeOf(e.NetId) is Node3D ship)
                    Combat.Lost(ship, by, ship.GlobalPosition);
            }
            else if (!down)
                _down.Remove(e.NetId);
        }
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
        ushort ship = IdOf((missile as Missile)?.Shooter) ?? NetIds.PlayerShip(Session.LocalPeer);
        Session.Blast(Now, fraction, id, Sector(at), damage, lethalRadius, blastRadius, ship);
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
        if (body.Joined || !IsInstanceValid(body.Ship))
            return;
        if (body.OwnedBy == NetOwner.Host && !Session.IsHost)
        {
            body.Ship.QueueFree();          // the host's world has the game's ships
            return;
        }
        int index = Index(body.Ship.SceneFilePath);
        if (index < 0)
        {
            GD.PushWarning($"session: {body.Ship.SceneFilePath} is not in the spawnables; the others will not see it");
            return;
        }
        body.Join(Session.NewDynamicId(), true, Session.TickSeconds);
        Session.Spawn(body, (byte)index);
        body.Ship.TreeExiting += () => Instance?.Gone(body, body.Exploded);
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
        if (_theirRadios.TryGetValue(id, out var radio))
            ship.GetNodeOrNull<RemoteRadio>("Radio")?.Follow(radio.Sign, radio.On);
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
        if (_theirRadios.TryGetValue(id, out var radio))
            foreach (Xpace.Ship.RadioDevice device in ship.FindChildren("*", "", true, false).OfType<Xpace.Ship.RadioDevice>())
                device.Follow(radio.Sign, radio.On);
        // A guest's missile was launched by its player's ship, the only ship a guest flies
        if (ship is Missile missile && NetIds.Owner(id) != Session.HostPeer && NodeOf(NetIds.PlayerShip(NetIds.Owner(id))) is Node3D shooter)
        {
            missile.Shooter = shooter;
            Combat.Fire(shooter, shooter.GlobalPosition);
        }
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

    private void Confirmed(HitEvent hit)
    {
        Projectiles.Instance?.Remove(hit.Shooter, hit.Sequence);
        if (Session.Entity(hit.Target) is INetEntity { IsLocal: false } victim)
        {
            _lastHit[victim.NetId] = (hit.Ship, Now);
            Combat.Struck(NodeOf(victim.NetId), NodeOf(hit.Ship));     // the owner takes it (and Combat hears of it there)
        }
    }

    /// <summary>A guest's ship asks a station: the host's station of that callsign answers.</summary>
    public void Request(IRadioReceiver from, StationRadio station, RadioRequest what)
    {
        if (IdOf(from as Node) is ushort ship)
            Session.Request(ship, station.Sign, (byte)what);
    }

    /// <summary>The host: another machine's ship asks one of the stations here.</summary>
    private void Requested(ushort ship, string station, byte what)
    {
        if (StationRadio.Called(station) is StationRadio s && NodeOf(ship) is Node3D node &&
            node.FindChildren("*", "", true, false).OfType<RemoteRadio>().FirstOrDefault() is RemoteRadio radio)
            s.Request(radio, (RadioRequest)what);
    }

    /// <summary>The host's station cleared a ship of this machine to a dock: its radio has the clearance.</summary>
    private void DockCleared(INetEntity ship, string station, int dock, int pad)
    {
        if (StationRadio.Called(station) is StationRadio s && NodeOf(ship.NetId) is Node3D node &&
            node.FindChildren("*", "", true, false).OfType<IRadioReceiver>().FirstOrDefault() is IRadioReceiver radio)
            radio.Cleared(s, dock, pad);
    }

    /// <summary>The host: a dock here opened or shut; every machine shows it.</summary>
    private void DockChanged(HangarDock dock)
    {
        if (Active && Session.IsHost && dock.Station != null)
            Session.Dock(dock.Station.Sign, dock.Number, dock.IsOpen);
    }

    /// <summary>Another machine's ship's radio, as its owner has it.</summary>
    private void RadioChanged(ushort ship, string callsign, bool on)
    {
        _theirRadios[ship] = (callsign, on);       // also for when what shows it is made
        switch (NodeOf(ship))
        {
            case RemoteShip remote:
                remote.GetNodeOrNull<RemoteRadio>("Radio")?.Follow(callsign, on);
                break;
            case Node3D puppet:
                foreach (Xpace.Ship.RadioDevice radio in puppet.FindChildren("*", "", true, false).OfType<Xpace.Ship.RadioDevice>())
                    radio.Follow(callsign, on);
                break;
        }
    }

    /// <summary>The host's radio said something to a ship of this machine: its radio hears it.</summary>
    private void Heard(INetEntity ship, string from, string text, string speech)
    {
        if (NodeOf(ship.NetId) is not Node3D node ||
            node.FindChildren("*", "", true, false).OfType<IRadioReceiver>().FirstOrDefault() is not IRadioReceiver radio)
            return;
        IRadio speaker = Radio.All(GetTree()).FirstOrDefault(r => r.Callsign == from) ?? new Speaker(from, node.GlobalPosition);
        radio.Hear(speaker, text, speech);
    }

    /// <summary>The host's radio, when this machine has none of that callsign.</summary>
    private sealed record Speaker(string Callsign, Vector3 RadioPosition) : IRadio
    {
        public char Kind => 'S';
        public float RadioRange => float.MaxValue;
        public bool RadioOn => true;
    }

    /// <summary>Someone else dropped something: it is made here, already as old as it is by now.</summary>
    private void Dropped(DropEvent d)
    {
        if (d.Scene >= Spawnables.Count || Spawnables[d.Scene] is not PackedScene scene)
            return;
        float age = System.Math.Max(0, (int)(Now - d.Tick)) * Session.TickSeconds;
        Countermeasures.Make(World, scene, Scene(d.Position + d.Velocity * age), d.Velocity, age);
    }
}
