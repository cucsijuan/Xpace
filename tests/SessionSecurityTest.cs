using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A station's security, on the host, judging a guest's ship as it judges
/// any ship (the guest is plain code over a simulated network; the host's
/// two ships are hulls with a radio, their computers off):
///
///   - the guest's radio is known on the host as the guest has it: its
///     callsign (the pilot's name), on or off;
///   - too fast near the station: the guest is fined, and its radio hears
///     it; the station's welcome is not sent twice (the guest's own station
///     sends it there);
///   - the guest asks the host's station for a dock: cleared (its radio has
///     the clearance, its words), the dock opens on the host and on the
///     guest's machine, and on a guest that joins later; the clearance runs
///     out the same way;
///   - a host ship's rounds destroy the guest's ship (it blows up on the
///     guest's machine): the host ship is hostile, told it destroyed the
///     guest's ship by name;
///   - the guest's missile leaving its rail in the zone: a warning, heard by
///     the guest; its round within the grace destroys a clean host ship:
///     the guest is hostile at once, and told why; asking for a dock now, it
///     is denied;
///   - the police the station launches are seen on the guest's machine.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_security_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionSecurityTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private const int PoliceScene = 4;
    private int _failures;

    /// <summary>The guest's own ship, and its copies of the host's ships.</summary>
    private sealed class Thing : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes { get; init; } = { Box };
        public SectorPoint Position;
        public Vector3 Velocity;
        public bool Destroyed;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity, Destroyed = Destroyed,
        };

        public void TakeHit(HitEvent hit) => Hits++;
    }

    private FloatingOrigin _origin;
    private SimulatedNetwork _net;
    private Session _guest, _late;
    private Thing _ship, _lateShip;
    private readonly List<(string Station, int Dock, bool Open)> _doors = new(), _lateDoors = new();
    private readonly List<int> _cleared = new();
    private readonly Dictionary<ushort, (string Sign, bool On)> _lateRadios = new();
    private readonly List<(string From, string Text, string Speech)> _heard = new();
    private readonly List<byte> _spawned = new();

    public override async void _Ready()
    {
        await AsHost();
        await AsGuest();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task AsHost()
    {
        _origin = new FloatingOrigin { Name = "World" };
        _origin.AddChild(new SectorClock());
        _origin.AddChild(new Projectiles());
        AddChild(_origin);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        _origin.AddChild(station);
        var security = station.GetNode<StationSecurity>("Security");
        security.PoliceCount = 1;
        var traffic = station.GetNode<StationRadio>("Radio");
        traffic.ClearanceSeconds = 8;
        ShipBody victor = Ship("Victor", new Vector3(560, 0, 1000), 15);
        ShipBody whiskey = Ship("Whiskey", new Vector3(360, 0, 1300));
        var hostNode = new SessionNode
        {
            Spawnables = new()
            {
                Scene("weapons/missile"), Scene("weapons/ir_missile"), Scene("weapons/chaff"), Scene("weapons/flare"),
                Scene("ships/police_fighter"),
            },
        };
        _origin.AddChild(hostNode);
        await Frames(1);

        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        hostNode.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _guest = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        _ship = new Thing { NetId = GuestShipId, IsLocal = true, Position = new SectorPoint(0, 0, 1000) };
        _guest.Add(_ship);
        foreach (ushort id in new ushort[] { SessionNode.FirstHostId, SessionNode.FirstHostId + 1 })
            _guest.Add(new Thing { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } });
        _guest.RadioHeard += (e, from, text, speech) => { if (e == _ship) _heard.Add((from, text, speech)); };
        _guest.DockCleared += (e, station, dock) => { if (e == _ship && station == "HAVEN") _cleared.Add(dock); };
        _guest.DockChanged += (station, dock, open) => _doors.Add((station, dock, open));
        _guest.SetRadio(GuestShipId, "ACE", true);
        _guest.SpawnDynamic = (id, scene) =>
        {
            _spawned.Add(scene);
            return new Thing { NetId = id, Track = new RemoteTrack { TickSeconds = Dt }, Hitboxes = Array.Empty<Hitbox>() };
        };
        await Frames(90);
        var player = _origin.GetNodeOrNull<RemoteShip>("Player1");
        Check("the guest's ship shows on the host", player != null && player.Visible);
        Check($"the guest hears no welcome from the host's station ({_heard.Count} messages)", _heard.Count == 0);
        RemoteRadio radio = player?.GetNodeOrNull<RemoteRadio>("Radio");
        Check($"its radio on the host has the guest's callsign ({radio?.Callsign}), on", radio?.Callsign == "ACE" && radio.RadioOn);
        _guest.SetRadio(GuestShipId, "ACE", false);
        Check("turned off there, off on the host", await Until(() => !radio.RadioOn, 1));
        _guest.SetRadio(GuestShipId, "ACE", true);
        Check("and on again", await Until(() => radio.RadioOn, 1));

        // Too fast near the station
        _ship.Velocity = new Vector3(80, 0, 0);
        await Frames(270);
        _ship.Velocity = Vector3.Zero;
        await Frames(30);
        Check($"too fast near the station: fined ({LastHeard})",
              security.Fines.TryGetValue(player, out int fines) && fines == 1 && LastHeard.Contains("fined") && _heard[^1].From == "HAVEN");
        Check($"spoken by the station's operator ({LastSpeech})", traffic.Operator != null && LastSpeech == $"{traffic.Operator.Name}:fined");
        Check("fined, but not wanted", security.StandingOf(player) == Standing.Clean);

        // A dock from the host's station
        _guest.Request(GuestShipId, "HAVEN", (byte)RadioRequest.Docking);
        Check("asking for a dock: cleared, its radio has the clearance", await Until(() => _cleared.Count > 0 && _cleared[^1] != 0, 2));
        int number = _cleared.Count > 0 ? _cleared[^1] : 0;
        HangarDock dock = traffic.Docks.FirstOrDefault(d => d.Number == number);
        await Frames(10);
        Check($"told in words ({LastHeard})", LastHeard.Contains($"hangar {number}"));
        Check($"and in the operator's voice, the hangar's number its own clip ({LastSpeech})", LastSpeech.EndsWith($":cleared hangar_{number} follow_lights"));
        Check($"hangar {number} opens on the host, for the guest's ship", dock != null && dock.IsOpen && dock.ClearedFor == player);
        Check("and on the guest's machine", _doors.Contains(("HAVEN", number, true)));
        _late = new Session(2, _net.Join(2)) { TickSeconds = Dt };
        _lateShip = new Thing { NetId = NetIds.PlayerShip(2), IsLocal = true, Position = new SectorPoint(0, 0, 8000) };
        _late.Add(_lateShip);
        _late.DockChanged += (station, d, open) => _lateDoors.Add((station, d, open));
        _late.RadioChanged += (id, sign, on) => _lateRadios[id] = (sign, on);
        Check("a guest joining later sees that dock open", await Until(() => _lateDoors.Contains(("HAVEN", number, true)), 3));
        Check("and knows the first guest's radio", _lateRadios.TryGetValue(GuestShipId, out var r) && r.Sign == "ACE" && r.On);
        bool expired = await Until(() => _cleared[^1] == 0, 10) &&
                       await Until(() => _doors[^1] == ("HAVEN", number, false) && _lateDoors[^1] == ("HAVEN", number, false), 1);
        Check("the clearance runs out: the guest is told, the dock shuts everywhere", expired && !dock.IsOpen);
        Check($"in words ({LastHeard})", LastHeard.Contains("expired"));

        // A host ship destroys the guest's ship
        for (int i = 0; i < 3; i++)
        {
            Vector3 from = whiskey.GlobalPosition + new Vector3(0, 0, -10);
            Projectiles.Instance.Fire(from, (player.GlobalPosition - from).Normalized() * 1200, 20, whiskey);
            await Frames(10);
        }
        Check("its rounds hit the guest's ship, on the guest's machine", await Until(() => _ship.Hits > 0, 2));
        _ship.Destroyed = true;
        await Frames(30);
        string told = Heard(whiskey);
        Check($"it blew up there: the host ship that did it is hostile, told so ({told})",
              security.StandingOf(whiskey) == Standing.Hostile && told.Contains("You destroyed ACE"));
        Check("the destroyed guest starts clean", security.StandingOf(player) == Standing.Clean);
        _ship.Destroyed = false;                    // insured: a new ship
        await Frames(60);

        // The guest's missile leaves its rail in the zone
        var missile = new Thing { NetId = _guest.NewDynamicId(), IsLocal = true, Hitboxes = Array.Empty<Hitbox>(), Position = _ship.Position + new Vector3(0, 0, -10) };
        int before = _heard.Count;
        _guest.Spawn(missile, 0);
        bool warned = await Until(() => security.StandingOf(player) == Standing.Warned, 1) &&
                      await Until(() => _heard.Count > before && LastHeard.Contains("Cease fire"), 1);
        Check($"the guest's missile launch in the zone: warned, and told ({LastHeard})", warned);
        _guest.Despawn(missile.NetId, SectorClock.Instance.Tick, false);

        // Within the grace, its round destroys a clean ship
        Vector3 muzzle = player.GlobalPosition + new Vector3(10, 0, 0);
        _guest.Fire(SectorClock.Instance.Tick, GuestShipId, hostNode.Sector(muzzle), (victor.GlobalPosition - muzzle).Normalized() * 1200, 20);
        Check("the guest's round destroys the host's ship", await Until(() => victor.Wrecked, 2));
        await Frames(20);
        Check($"the guest is hostile at once, told why ({LastHeard})",
              security.StandingOf(player) == Standing.Hostile && LastHeard.Contains("You destroyed VICTOR"));
        Check("the police go after it", security.Hostiles.Contains(player));
        int heard = _heard.Count;
        _guest.Request(GuestShipId, "HAVEN", (byte)RadioRequest.Docking);
        bool denied = await Until(() => _heard.Count > heard && LastHeard.Contains("denied"), 2);
        Check($"asking for a dock now: denied ({LastHeard})", denied);
        Check($"the police launched are seen on the guest's machine (scenes {string.Join(",", _spawned)})",
              await Until(() => _spawned.Contains(PoliceScene), 10));

        _origin.QueueFree();
        _guest = _late = null;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>
    /// This scene as a guest (the host is plain code): the ship's radio sends
    /// its callsign, its request for a dock goes to the host's station (the
    /// station here clears nothing itself), and the host's clearance, words
    /// and dock come to this machine.
    /// </summary>
    private async Task AsGuest()
    {
        _origin = new FloatingOrigin { Name = "World" };
        _origin.AddChild(new SectorClock());
        _origin.AddChild(new Projectiles());
        AddChild(_origin);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        _origin.AddChild(station);
        var traffic = station.GetNode<StationRadio>("Radio");
        ShipBody ship = Ship("Bravo", new Vector3(0, 0, 1500), pilot: true);
        var radio = ship.GetNode<RadioDevice>("Radio");
        radio.Callsign = "BRAVO";
        var voice = new Xpace.Cockpit.RadioVoice { Name = "Voice" };
        radio.AddChild(voice);
        var guestNode = new SessionNode();
        _origin.AddChild(guestNode);
        await Frames(1);

        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        var host = new Session(Session.HostPeer, _net.Join(Session.HostPeer)) { TickSeconds = Dt };
        host.Add(new Thing { NetId = GuestShipId, Track = new RemoteTrack { TickSeconds = Dt } });
        string sign = null;
        var asked = new List<(ushort Ship, string Station, byte What)>();
        host.RadioChanged += (id, callsign, on) => { if (id == GuestShipId) sign = callsign; };
        host.Requested += (id, at, what) => asked.Add((id, at, what));
        guestNode.Start(GuestPeer, _net.Join(GuestPeer));
        _guest = host;                              // stepped with the frames
        _ship = new Thing { NetId = SessionNode.FirstHostId, IsLocal = true, Position = new SectorPoint(0, 0, 9000) };
        host.Add(_ship);
        Check("the ship's callsign reaches the host", await Until(() => sign == "BRAVO", 3));

        radio.Ask(RadioRequest.Docking, traffic);
        Check("asking for a dock: the request goes to the host's station",
              await Until(() => asked.Contains((GuestShipId, "HAVEN", (byte)RadioRequest.Docking)), 2));
        await Frames(60);
        Check("the station here clears nothing itself", radio.Dock == 0 && traffic.Docks.All(d => !d.IsOpen));
        host.Cleared(GuestShipId, "HAVEN", 2);
        host.Dock("HAVEN", 2, true);
        int clips = voice.Clips;
        host.Say(GuestShipId, "HAVEN", "Cleared to dock, hangar 2.", "operator_f1:cleared hangar_2 follow_lights");
        Check("the host's clearance: the radio has it", await Until(() => radio.Dock == 2 && radio.DockFrom == traffic, 2));
        Check("the dock opens here", await Until(() => traffic.Docks.First(d => d.Number == 2).IsOpen, 1));
        bool words = await Until(() => Heard(ship).Contains("hangar 2"), 1);
        Check($"the words reach the radio ({Heard(ship)})", words);
        Check($"spoken here: every clip of the speech found ({voice.Clips - clips} clips, {voice.Missing} missing)", voice.Clips - clips == 3 && voice.Missing == 0 && voice.Talking);
        host.Dock("HAVEN", 2, false);
        Check("the host shuts it: shut here", await Until(() => !traffic.Docks.First(d => d.Number == 2).IsOpen, 1));

        _origin.QueueFree();
        _guest = null;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static PackedScene Scene(string name) => GD.Load<PackedScene>($"res://scenes/{name}.tscn");

    private string LastHeard => _heard.Count > 0 ? _heard[^1].Text : "";
    private string LastSpeech => _heard.Count > 0 ? _heard[^1].Speech : "";

    private static string Heard(ShipBody ship) => ship.GetNode<RadioDevice>("Radio").Heard is { Count: > 0 } h ? h[^1].Text : "";

    private ShipBody Ship(string name, Vector3 at, float integrity = 300, bool pilot = false)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio" });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false, Integrity = integrity });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        ship.AddChild(new NetBody { OwnedBy = pilot ? NetOwner.Pilot : NetOwner.Host });
        _origin.AddChild(ship);
        return ship;
    }

    /// <summary>Frames, with the guest's side of the session stepped in each.</summary>
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_guest == null)
                continue;
            uint tick = SectorClock.Instance.Tick;
            _net.Now = tick * Dt;
            _ship.Position += _ship.Velocity * Dt;
            _guest.Tick(tick);
            _late?.Tick(tick);
        }
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await Frames(1);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
