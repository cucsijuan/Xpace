using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// Joins a <see cref="Session"/> to the game. Place it in the game's shell,
/// beside the <see cref="SectorHost"/>, or under a sector's floating origin
/// (one sector, as tests do); nothing changes until <see cref="Start"/>
/// (alone, the game plays as before).
///
/// With a sector host, everything is in a sector (its number in the map,
/// the session's place). The host runs every sector a player is in: one a
/// guest goes to is loaded beside the host's own, with nothing drawn, and
/// let go when nobody is left in it; its floating origin follows a player
/// there, so their ships and what is near them keep their precision. A
/// guest runs only its own sector. Sectors loaded during a session lose
/// their scene ships as the first one did (<see cref="KeepSceneShips"/>).
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
///
/// A player on foot here (<see cref="Xpace.OnFoot.Walker.Local"/>) is sent to
/// the others, in the frame of the station they walk; the others' players
/// on foot walk here as <see cref="Xpace.OnFoot.RemoteWalker"/>s showing
/// <see cref="WalkerBody"/>, below the station of the same callsign, with
/// their ship's callsign over their heads.
/// </summary>
public partial class SessionNode : Node
{
    /// <summary>The host's first scene ship (the ships of peer 0 from 1 on).</summary>
    public const ushort FirstHostId = 1;

    [Export] public PackedScene RemoteShipScene;
    /// <summary>The body other players on foot show (none: the crew's).</summary>
    [Export] public PackedScene WalkerBody;
    [Export] public Vector3 PilotSpacing = new(40, 0, 0);

    /// <summary>
    /// Whether the scene's own ships (<see cref="NetOwner.Host"/>: drones,
    /// AI fighters) stay in a session. Off, they go on every machine when
    /// the session starts, leaving only the players.
    /// </summary>
    [Export] public bool KeepSceneShips = true;

    /// <summary>
    /// The host keeps the origin of each sector it runs for guests near a player
    /// there, so what flies near them keeps its precision. Off only to measure
    /// what that is worth.
    /// </summary>
    [Export] public bool FollowsPlayers = true;

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

    private FloatingOrigin _origin;                 // the sector this node is in, without a sector host
    private SectorHost _sectors;
    private readonly System.Collections.Generic.Dictionary<ushort, (ushort By, uint At)> _lastHit = new();
    private readonly System.Collections.Generic.HashSet<ushort> _down = new();
    private readonly System.Collections.Generic.Dictionary<ushort, (IRadio Radio, string Sign, bool On)> _radios = new();
    private readonly System.Collections.Generic.Dictionary<ushort, (string Sign, bool On)> _theirRadios = new();
    private readonly System.Collections.Generic.Dictionary<ushort, string> _fittings = new();         // sent, of the ships owned here
    private readonly System.Collections.Generic.Dictionary<ushort, string> _theirFittings = new();    // heard, of other machines' ships
    private readonly System.Collections.Generic.Dictionary<byte, Xpace.OnFoot.RemoteWalker> _walkers = new();

    /// <summary>Another machine's player on foot as it walks here, or null.</summary>
    public Xpace.OnFoot.RemoteWalker WalkerOf(byte peer) =>
        _walkers.TryGetValue(peer, out var w) && IsInstanceValid(w) ? w : null;

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
        if (_sectors != null)
        {
            _sectors.Loaded -= Prepare;
            _sectors.Jumped -= Jumped;
            _sectors.Keep = null;
        }
        foreach (Xpace.OnFoot.RemoteWalker walker in _walkers.Values)
            if (IsInstanceValid(walker))
                walker.QueueFree();
        _walkers.Clear();
        if (Session != null)
            Xpace.World.Universe.Current?.Detach();
    }

    public override void _Ready()
    {
        for (Node n = GetParent(); n != null && _origin == null; n = n.GetParent())
            _origin = n as FloatingOrigin;
        if (_origin == null)
            _sectors = SectorHost.Current;
        if (SectorClock.Instance == null)
            GetParent().CallDeferred(Node.MethodName.AddChild, new SectorClock { Name = "SectorClock" });
    }

    /// <summary>The sector a node is in (its number in the map; 0 with one sector).</summary>
    public byte PlaceOf(Node node) => _sectors?.PlaceOf(node) ?? 0;

    /// <summary>The root of a sector running here, or null.</summary>
    public FloatingOrigin OriginAt(byte place) => _sectors != null ? _sectors.RootAt(place) : _origin;

    // Where puppets of that sector go
    private Node WorldAt(byte place) => _sectors != null ? _sectors.RootAt(place) : (Node)_origin ?? GetParent();

    private uint Now => SectorClock.Instance?.Tick ?? 0;

    /// <summary>Joins the session as <paramref name="peer"/> (0 is the host) over <paramref name="transport"/>.</summary>
    public void Start(byte peer, INetTransport transport)
    {
        Session = new Session(peer, transport) { TickSeconds = 1f / Engine.PhysicsTicksPerSecond };
        Xpace.World.Universe.Current?.Attach(Session);
        if (Session.Judge != null)
            Session.Judge.Solid = (place, a, b) =>
                OriginAt(place) is FloatingOrigin o && Obstacles.Blocked(o.GetWorld3D(), Scene(a, place), Scene(b, place));
        if (_sectors != null)
        {
            Session.Move(PlaceOf(_sectors.Sector));
            _sectors.Keep = id => Active && Session.IsHost && Session.Occupied.Contains(_sectors.PlaceOf(id));
            _sectors.Loaded += Prepare;
            _sectors.Jumped += Jumped;
            Session.Moved += Moved;
        }
        Session.SpawnRemote = Spawn;
        Session.SpawnDynamic = SpawnPuppet;
        Session.RoundFired += Show;
        Session.RoundFired += e => Combat.Fire(NodeOf(e.Ship), Scene(e.Origin, Session.PlaceOf(e.Ship)));
        Session.RoundFired += Counted;
        Session.HitConfirmed += Confirmed;
        Session.RadioHeard += Heard;
        Session.RadioChanged += RadioChanged;
        Session.FittingChanged += FittingChanged;
        Session.Requested += Requested;
        Session.DockCleared += DockCleared;
        Session.Thickened += (ship, density, band) => (NodeOf(ship.NetId) as Xpace.Ship.ShipBody)?.Device<Xpace.Ship.CruiseDevice>()?.Thicken(density, band);
        Session.DockChanged += (station, number, open) =>
            StationRadio.Called(station)?.Docks.FirstOrDefault(d => d.Number == number)?.Show(open);
        Session.StationNamed += (station, callsign, title) => Xpace.World.Universe.Current?.ShowName(station, callsign, title);
        if (Session.IsHost)
        {
            HangarDock.Changed += DockChanged;
            if (Xpace.World.Universe.Current is Xpace.World.Universe universe)
            {
                universe.Renamed += Session.NameStation;
                foreach (Xpace.World.StationRecord record in universe.State.Stations.Values.Where(r => r.Title != "" || r.Callsign != ""))
                    Session.NameStation(record.Id, record.Callsign, record.Title);
            }
            foreach (StationRadio station in StationRadio.All)
                foreach (HangarDock dock in station.Docks.Where(d => d.IsOpen))
                    DockChanged(dock);
        }
        Session.Despawned += (e, tick, exploded) => (e as NetBody)?.EndAt(tick, exploded);
        Session.Dropped += Dropped;
        Session.WalkerMoved += Walked;
        Session.WalkerGone += WalkerGone;
        Session.Forgotten += Unshow;
        Session.Left += Unshow;
        Session.AdjustClock = ticks =>
        {
            if (SectorClock.Instance is SectorClock clock)
                clock.Tick = (uint)(clock.Tick + ticks);
        };

        Node root = _sectors?.Sector ?? WorldAt(0);
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

    // What showed something here goes (its owner left, or it is in another sector now)
    private static void Unshow(INetEntity e)
    {
        if (e is RemoteShip ship && IsInstanceValid(ship))
            ship.QueueFree();
        else if (e is NetBody body && IsInstanceValid(body.Ship))
            body.Ship.QueueFree();
    }

    // A sector loaded during the session: its scene ships go as the first sector's did, what it brought in joins
    private void Prepare(FloatingOrigin root)
    {
        if (!Active)
            return;
        var all = root.FindChildren("*", nameof(Node), true, false).OfType<NetBody>().ToList();
        foreach (NetBody body in all.Where(b => b.OwnedBy == NetOwner.Host && !b.Spawned))
            if (!KeepSceneShips || !Session.IsHost)
                body.Ship.QueueFree();
        foreach (NetBody body in all.Where(b => b.Spawned && !b.Puppet))
            Made(body);
    }

    // This machine's player went to another sector
    private void Jumped(Sector from, Sector to)
    {
        if (Active)
            Session.Move(PlaceOf(_sectors.Sector));
    }

    // The host: a player is in another sector now: it runs that one, and lets go of those nobody is in
    private void Moved(ushort id, byte place)
    {
        if (!Session.IsHost || _sectors == null)
            return;
        var occupied = Session.Occupied.ToHashSet();
        foreach (byte p in occupied)
            if (_sectors.RootAt(p) == null)
                _sectors.Hold(p);
        foreach (byte p in _sectors.LoadedPlaces.ToList())
            if (!occupied.Contains(p))
                _sectors.Release(p);
    }

    // The host: the origin of each sector it runs for guests follows a player there
    private void FollowPlayers()
    {
        if (_sectors == null || !Session.IsHost || !FollowsPlayers)
            return;
        foreach (byte p in _sectors.LoadedPlaces)
        {
            FloatingOrigin root = _sectors.RootAt(p);
            if (root == _sectors.Sector || root.Ship is Node3D followed && IsInstanceValid(followed) && root.IsAncestorOf(followed))
                continue;
            root.Ship = Session.Entities.Where(e => NetIds.IsPlayerShip(e.NetId) && Session.PlaceOf(e.NetId) == p)
                .Select(e => NodeOf(e.NetId)).FirstOrDefault(n => n != null && root.IsAncestorOf(n));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active || SectorClock.Instance is not SectorClock clock)
            return;
        FollowPlayers();
        Session.Walk(Xpace.OnFoot.Walker.Local is { } walker && IsInstanceValid(walker) ? walker.State() : null);
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

        // What the ships owned here have on their hardpoints and their rails, when it changes
        foreach (INetEntity e in Session.Entities)
            if (e.IsLocal && e is NetBody { Joined: true, Ship: Xpace.Ship.ShipBody ship } && ship.Hardpoints.Count > 0)
            {
                string fitted = $"{ship.Fitted}|{ship.Device<Xpace.Ship.LauncherDevice>()?.Loads}";
                if (_fittings.TryGetValue(e.NetId, out string sent) && sent == fitted)
                    continue;
                _fittings[e.NetId] = fitted;
                Session.SetFitting(e.NetId, fitted);
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

    // Another machine's player on foot: shown in the station of the same callsign here (moved there if they walk
    // another one now)
    private void Walked(byte peer, WalkerState state)
    {
        if (StationRadio.Called(state.Station)?.GetParent() is not Node3D station)
        {
            WalkerGone(peer);
            return;
        }
        Xpace.OnFoot.RemoteWalker walker = WalkerOf(peer);
        if (walker == null)
        {
            WalkerBody ??= GD.Load<PackedScene>("res://scenes/characters/crew_body.tscn");
            _walkers[peer] = walker = Xpace.OnFoot.RemoteWalker.Make(WalkerBody, "");
            walker.Name = $"Walker{peer}";
            station.AddChild(walker);
        }
        else if (walker.GetParent() != station)
            walker.Reparent(station, false);
        walker.Pilot = _theirRadios.TryGetValue(NetIds.PlayerShip(peer), out var radio) ? radio.Sign : "";
        walker.Hear(state);
    }

    private void WalkerGone(byte peer)
    {
        if (WalkerOf(peer) is Xpace.OnFoot.RemoteWalker walker)
            walker.QueueFree();
        _walkers.Remove(peer);
    }

    /// <summary>A round leaves a muzzle of <paramref name="shooter"/> here (scene position and velocity).</summary>
    public void Fire(Vector3 origin, Vector3 velocity, float damage, Node shooter = null, byte kind = 0, Node where = null)
    {
        ushort ship = IdOf(shooter) ?? NetIds.PlayerShip(Session.LocalPeer);
        Show(Session.Fire(Now, ship, Sector(origin, PlaceOf(shooter ?? where ?? NodeOf(ship))), velocity, damage, kind));
    }

    /// <summary>The warhead of a missile flown here went off (scene position, and when in the tick).</summary>
    public void Blast(Node missile, Vector3 at, float fraction, float damage, float lethalRadius, float blastRadius)
    {
        ushort id = IdOf(missile) ?? NetIds.PlayerShip(Session.LocalPeer);
        ushort ship = IdOf((missile as Missile)?.Shooter) ?? NetIds.PlayerShip(Session.LocalPeer);
        Session.Blast(Now, fraction, id, Sector(at, PlaceOf(missile)), damage, lethalRadius, blastRadius, ship);
    }

    /// <summary>Something thrown out here (a <see cref="Spawnables"/> scene) is made on the other machines too.</summary>
    public void Drop(Node world, PackedScene scene, Vector3 at, Vector3 velocity)
    {
        int index = Index(scene);
        byte place = PlaceOf(world);
        if (index >= 0)
            Session.Drop(Now, (byte)index, Sector(at, place), velocity, place);
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
        Session.Spawn(body, (byte)index, PlaceOf(body.Ship));
        body.Ship.TreeExiting += () =>
        {
            if (!Sectors.Moving)
                Instance?.Gone(body, body.Exploded);
        };
    }

    /// <summary>A ship made here leaves (its NetBody calls this).</summary>
    public void Gone(NetBody body, bool exploded)
    {
        if (Active && body.Joined && body.IsLocal && Session.Entity(body.NetId) == body)
            Session.Despawn(body.NetId, Now, exploded);
    }

    /// <summary>A scene point of a sector as a point of the sector (its origin added).</summary>
    public SectorPoint Sector(Vector3 scene, byte place) => (OriginAt(place)?.Origin ?? default) + scene;

    /// <summary>A point of a sector in that sector's scene here.</summary>
    public Vector3 Scene(SectorPoint p, byte place) => (OriginAt(place)?.Origin ?? default).To(p);

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
        byte place = Session.PlaceOf(id);
        Node world = WorldAt(place) ?? (Session.IsHost ? _sectors?.Hold(place) : null);
        if (world == null)
            return null;                        // in a sector not run here
        RemoteShip ship = RemoteShipScene?.Instantiate<RemoteShip>() ?? PlainShip();
        ship.Name = $"Player{NetIds.Owner(id)}";
        ship.NetId = id;
        ship.Driven = true;
        if (_theirFittings.TryGetValue(id, out string fitted))
            ship.Fit(fitted);
        world.AddChild(ship);
        if (_theirRadios.TryGetValue(id, out var radio))
            ship.GetNodeOrNull<RemoteRadio>("Radio")?.Follow(radio.Sign, radio.On);
        return ship;
    }

    /// <summary>Another machine made something: its scene here, as a puppet.</summary>
    private INetEntity SpawnPuppet(ushort id, byte scene)
    {
        Node world = WorldAt(Session.PlaceOf(id));
        if (world == null || scene >= Spawnables.Count || Spawnables[scene]?.Instantiate() is not Node3D ship)
            return null;
        if (ship.GetChildren().OfType<NetBody>().FirstOrDefault() is not NetBody body)
        {
            ship.Free();
            return null;
        }
        body.Puppet = true;
        ship.Visible = false;                   // until its first snapshot
        world.AddChild(ship);
        body.Join(id, false, Session.TickSeconds);
        if (_theirRadios.TryGetValue(id, out var radio))
            foreach (Xpace.Ship.RadioDevice device in ship.FindChildren("*", "", true, false).OfType<Xpace.Ship.RadioDevice>())
                device.Follow(radio.Sign, radio.On);
        // A guest's missile was launched by its player's ship, the only ship a guest flies
        if (ship is Missile missile && NetIds.Owner(id) != Session.HostPeer && NodeOf(NetIds.PlayerShip(NetIds.Owner(id))) is Node3D shooter)
        {
            missile.Shooter = shooter;
            Combat.Fire(shooter, shooter.GlobalPosition);
            Xpace.World.Universe.Current?.Fired(shooter, missile.Spec);
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
        byte place = Session.PlaceOf(e.Ship);
        Rounds(place)?.Show(Scene(e.At(now - e.Tick, Session.TickSeconds), place), e.Velocity, age, e.Shooter, e.Sequence, e.Kind);
    }

    private void Confirmed(HitEvent hit)
    {
        Rounds(Session.PlaceOf(hit.Ship))?.Remove(hit.Shooter, hit.Sequence);
        if (Session.Entity(hit.Target) is INetEntity { IsLocal: false } victim)
        {
            _lastHit[victim.NetId] = (hit.Ship, Now);
            Combat.Struck(NodeOf(victim.NetId), NodeOf(hit.Ship));     // the owner takes it (and Combat hears of it there)
        }
    }

    // The rounds of a sector running here
    private Projectiles Rounds(byte place) => _sectors == null ? Projectiles.Instance : OriginAt(place) is Node root ? Projectiles.In(root) : null;

    /// <summary>A guest's ship asks a station: the host's station of that callsign answers.</summary>
    public void Request(IRadioReceiver from, StationRadio station, RadioRequest what)
    {
        if (IdOf(from as Node) is ushort ship)
            Session.Request(ship, station.Id, (byte)what);
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
            Session.Dock(dock.Station.Id, dock.Number, dock.IsOpen);
    }

    /// <summary>Another machine's ship's fitting, as its owner has it.</summary>
    private void FittingChanged(ushort ship, string fitted)
    {
        _theirFittings[ship] = fitted;              // also for when what shows it is made
        if (NodeOf(ship) is RemoteShip remote)
            remote.Fit(fitted);
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
        IRadio speaker = Radio.All(node).FirstOrDefault(r => r.Callsign == from) ?? new Speaker(from, node.GlobalPosition);
        radio.Hear(speaker, text, speech);
    }

    /// <summary>The host's radio, when this machine has none of that callsign.</summary>
    private sealed record Speaker(string Callsign, Vector3 RadioPosition) : IRadio
    {
        public char Kind => 'S';
        public float RadioRange => float.MaxValue;
        public bool RadioOn => true;
    }

    /// <summary>The host: a guest's cannon round comes off its pilot's magazine (laser pulses cost no rounds).</summary>
    private void Counted(FireEvent e)
    {
        byte owner = NetIds.Owner(e.Ship);
        if (Session.IsHost && e.Kind == 0 && owner != Session.HostPeer && e.Ship == NetIds.PlayerShip(owner) && NodeOf(e.Ship) is Node3D ship)
            Xpace.World.Universe.Current?.FiredRound(ship);
    }

    /// <summary>Someone else dropped something: it is made here, already as old as it is by now.</summary>
    private void Dropped(DropEvent d)
    {
        if (d.Scene >= Spawnables.Count || Spawnables[d.Scene] is not PackedScene scene || WorldAt(d.Place) is not Node world)
            return;
        float age = System.Math.Max(0, (int)(Now - d.Tick)) * Session.TickSeconds;
        Node3D made = Countermeasures.Make(world, scene, Scene(d.Position + d.Velocity * age, d.Place), d.Velocity, age);
        // A guest's chaff or flare was thrown by its player's ship: the host counts it off the pilot's record
        if (Session.IsHost && d.Dropper != Session.HostPeer && made is IDropped dropped && NodeOf(NetIds.PlayerShip(d.Dropper)) is Node3D thrower)
            Xpace.World.Universe.Current?.Fired(thrower, dropped.Item);
    }
}
