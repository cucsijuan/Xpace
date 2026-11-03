using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Landing pads in a session (the Synty station's first hangar, with its
/// pads, on every machine; the other side is plain code over a simulated
/// network).
///
/// This scene as the host, a guest's ship docking:
///   - the guest asks for a dock: the pad's hangar is cleared for it, and
///     the pad here glows for it; over it and lined up, green;
///   - the guest's ship (docked on its own machine) sits in its place on the
///     pad: the station here shuts the door, everywhere, and tells the guest;
///   - docked, the guest's pilot trades at the station's market through the
///     host: the host's universe takes the credits and loads the hold, and
///     sends the guest the market's answer and the pilot's record; it refits
///     there through the host too (the turret off the spine, into its locker);
///   - what the guest's ship has on its hardpoints, as the guest sends it,
///     shows on its ship here;
///   - the guest asks to launch: the door opens everywhere, the guest is
///     told; once its ship is out of the hangar the door shuts and its
///     clearance is over.
///
/// This scene as a guest, the host's station deciding:
///   - cleared by the host, the pad here takes the ship and docks it (only
///     this machine moves its own ship);
///   - asking to launch goes to the host; the clamps hold the ship until
///     the host opens the door, then lift it and let it go;
///   - what the ship has on its hardpoints goes to the host, and again when
///     it is refitted.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_pad_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionPadTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer);
    private int _failures;

    private sealed class Thing : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes { get; init; } = { new(Vector3.Zero, new Vector3(5, 3, 10)) };
        public SectorPoint Position;
        public Quaternion Orientation = Quaternion.Identity;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Position = Position, Orientation = Orientation };

        public void TakeHit(HitEvent hit) { }
    }

    private FloatingOrigin _origin;
    private SimulatedNetwork _net;
    private Session _other;
    private readonly List<(string Station, int Dock, bool Open)> _doors = new();
    private readonly List<int> _cleared = new(), _pads = new();
    private readonly List<string> _heard = new();

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
        Universe.Current.Forget();
        HangarDock dock = World();
        var hostNode = new SessionNode { RemoteShipScene = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn") };
        _origin.AddChild(hostNode);
        await Frames(1);

        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        hostNode.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _other = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        var ship = new Thing { NetId = GuestShipId, IsLocal = true };
        Put(ship, hostNode, dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 400)));
        _other.Add(ship);
        _other.RadioHeard += (e, from, text, speech) => { if (e == ship) _heard.Add(text); };
        _other.DockCleared += (e, station, d, p) => { if (e == ship && station == "HAVEN") { _cleared.Add(d); _pads.Add(p); } };
        _other.DockChanged += (station, d, open) => _doors.Add((station, d, open));
        _other.SetRadio(GuestShipId, "ACE", true);
        await Frames(90);
        var player = _origin.GetNodeOrNull<RemoteShip>("Player1");
        Check("the guest's ship shows on the host", player != null && player.Visible);

        _other.Request(GuestShipId, "HAVEN", (byte)RadioRequest.Docking);
        Check("asking for a dock: the hangar and a pad in it are cleared", await Until(() => _cleared.Count > 0 && _cleared[^1] == dock.Number && _pads[^1] != 0, 2));
        LandingPad pad = dock.PadNumbered(_pads[^1]);
        Check("the door opens, everywhere", await Until(() => dock.IsOpen && _doors.Contains(("HAVEN", dock.Number, true)), 1));
        Check($"the pad here glows for the guest's ship ({pad.State}), only it", await Until(() => pad.State == LandingPad.PadState.Waiting && pad.Expected == player, 1)
              && dock.Pads.Where(p => p != pad).All(p => p.State == LandingPad.PadState.Idle));

        Transform3D rest = pad.GlobalTransform * pad.Rest(player);
        Put(ship, hostNode, rest.TranslatedLocal(new Vector3(0, 10, 0)));
        Check($"over it and lined up: green here ({pad.State})", await Until(() => pad.State == LandingPad.PadState.Aligned, 2));
        Put(ship, hostNode, rest);
        Check("in its place (docked on its own machine): the door shuts, everywhere",
              await Until(() => !dock.IsOpen && _doors[^1] == ("HAVEN", dock.Number, false), 3));
        Check($"the guest is told ({Last})", await Until(() => Last.Contains("Docked"), 1));
        Check($"the pad here shows it docked ({pad.State})", pad.State == LandingPad.PadState.Docked);

        var documents = new Dictionary<string, string>();
        _other.DocumentArrived += (topic, text) => documents[topic] = text;
        _other.Introduce("ace-pilot", "Ace");
        Check("the guest says who flies its ship: the host sends the pilot's record",
              await Until(() => documents.ContainsKey(Universe.PilotTopic), 2) && Universe.Current.PilotOfPeer(GuestPeer) == "ace-pilot");
        _other.Ask(StationMarket.Topic, System.Text.Json.JsonSerializer.Serialize(
            new MarketRequest { Station = "HAVEN", Do = MarketRequest.Buy, Good = "food", Tonnes = 2 }));
        Check("the guest buys at the market through the host", await Until(() => documents.ContainsKey(StationMarket.Topic), 2));
        MarketView view = System.Text.Json.JsonSerializer.Deserialize<MarketView>(documents.GetValueOrDefault(StationMarket.Topic) ?? "{}");
        PilotRecord ace = Universe.Current.State.Pilot("ace-pilot");
        Check($"docked there, the host's universe takes the credits and loads the hold ({view.Message})",
              view.Docked && ace.Ship.Holds("food") == 2 && ace.Credits < PilotRecord.StartingCredits && view.Credits == ace.Credits);
        Check("and the guest gets the pilot's record with it",
              await Until(() => documents[Universe.PilotTopic].Contains("\"food\":2"), 1));

        ace.Ship.Magazine["ir_1"] = 0;
        ace.Credits = 100_000;
        _other.Ask(StationMarket.ArmsTopic, System.Text.Json.JsonSerializer.Serialize(
            new MarketRequest { Station = "HAVEN", Do = MarketRequest.Buy, Good = "ir_1", Tonnes = 1 }));
        Check("the guest buys a missile at the arms dealer through the host", await Until(() => documents.ContainsKey(StationMarket.ArmsTopic), 2));
        ArmsView arms = System.Text.Json.JsonSerializer.Deserialize<ArmsView>(documents.GetValueOrDefault(StationMarket.ArmsTopic) ?? "{}");
        Check($"the host loads it on the pilot's record ({arms.Message})", ace.Ship.Magazine["ir_1"] == 1 && arms.Credits == ace.Credits && ace.Credits < 100_000);
        Check("and the guest's copy of the record has it",
              await Until(() => documents[Universe.PilotTopic].Contains("\"ir_1\":1"), 1));

        _other.Ask(FittingDesk.Topic, System.Text.Json.JsonSerializer.Serialize(
            new FittingRequest { Station = "HAVEN", Do = FittingRequest.Remove, Place = "Spine" }));
        Check("the guest refits through the host", await Until(() => documents.ContainsKey(FittingDesk.Topic), 2));
        FittingView fit = System.Text.Json.JsonSerializer.Deserialize<FittingView>(documents.GetValueOrDefault(FittingDesk.Topic) ?? "{}");
        Check($"the host takes the turret off on the pilot's record, into the locker here ({fit.Message})",
              ace.Ship.Fittings.GetValueOrDefault("Spine") == "" && Universe.Current.State.Station("HAVEN").LockerOf("ace-pilot").Any(i => i.Name == "GUN-1")
              && fit.Places.First(p => p.Name == "Spine").Mounted == "");
        Hardpoint spine = player.FindChildren("*", "", true, false).OfType<Hardpoint>().First(h => h.Name == "Spine");
        Check("the guest's ship shows the factory's fit here until the guest says", spine.Mounted is TurretSpec);
        _other.SetFitting(GuestShipId, "Spine=res://assets/weapons/lasers/lsr_1.tres");
        Check("then what the guest says it has: a laser on the spine, nothing on the nose",
              await Until(() => spine.Mounted is LaserSpec, 2) && player.FindChildren("*", "", true, false).OfType<Hardpoint>().Count(h => h.Mounted != null) == 1);

        _other.Request(GuestShipId, "HAVEN", (byte)RadioRequest.Launch);
        Check("asking to launch: the door opens, everywhere", await Until(() => dock.IsOpen && _doors[^1] == ("HAVEN", dock.Number, true), 2));
        Check($"the guest is told ({Last})", await Until(() => Last.Contains("launch"), 1));
        Put(ship, hostNode, rest.TranslatedLocal(new Vector3(0, 6, 0)));
        await Frames(60);
        Check("lifted, still in the hangar: the door stays open", dock.IsOpen && _cleared[^1] == dock.Number);
        Put(ship, hostNode, dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 200)));
        Check("out of the hangar: the door shuts everywhere, the clearance is over",
              await Until(() => !dock.IsOpen && _doors[^1] == ("HAVEN", dock.Number, false) && _cleared[^1] == 0, 3));
        Check($"the guest is told ({Last})", await Until(() => Last.Contains("Fly safe"), 1));

        _origin.QueueFree();
        _other = null;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task AsGuest()
    {
        HangarDock dock = World();
        StationRadio traffic = dock.Station;
        LandingPad pad = dock.Pads[^1];
        var ship = new ShipBody { Name = "Bravo", Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres") };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_hitboxes.tscn").Instantiate());
        ship.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_exterior.tscn").Instantiate());
        var radio = new RadioDevice { Name = "Radio", Callsign = "BRAVO" };
        ship.AddChild(radio);
        ship.AddChild(new NetBody { OwnedBy = NetOwner.Pilot });
        _origin.AddChild(ship);
        ship.GlobalTransform = dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 400));
        var guestNode = new SessionNode();
        _origin.AddChild(guestNode);
        await Frames(1);

        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        var host = new Session(Session.HostPeer, _net.Join(Session.HostPeer)) { TickSeconds = Dt };
        host.Add(new Thing { NetId = GuestShipId, Track = new RemoteTrack { TickSeconds = Dt } });
        var asked = new List<(ushort Ship, string Station, byte What)>();
        host.Requested += (id, at, what) => asked.Add((id, at, what));
        var fittings = new List<string>();
        host.FittingChanged += (id, fitted) => { if (id == GuestShipId) fittings.Add(fitted); };
        guestNode.Start(GuestPeer, _net.Join(GuestPeer));
        _other = host;
        await Frames(60);

        radio.Ask(RadioRequest.Docking, traffic);
        Check("asking for a dock goes to the host", await Until(() => asked.Contains((GuestShipId, "HAVEN", (byte)RadioRequest.Docking)), 2));
        host.Cleared(GuestShipId, "HAVEN", dock.Number, pad.Number);
        host.Dock("HAVEN", dock.Number, true);
        Check("the host clears it: the radio has it, the door opens here", await Until(() => radio.Dock == dock.Number && radio.Pad == pad.Number && dock.IsOpen, 2));
        Check($"the pad here waits for it ({pad.State}), only it", await Until(() => pad.Expected == ship, 1) && dock.Pads.Where(p => p != pad).All(p => p.State == LandingPad.PadState.Idle));

        Transform3D rest = pad.GlobalTransform * pad.Rest(ship);
        ship.State = new ShipState { Position = rest.TranslatedLocal(new Vector3(1, 3, 0)).Origin, Orientation = rest.Basis.GetRotationQuaternion() };
        Check("low, lined up and still: the clamps here take it", await Until(() => ship.Held, 1));
        Check("and dock it", await Until(() => pad.State == LandingPad.PadState.Docked, (float)pad.SettleSeconds + 1));
        host.Dock("HAVEN", dock.Number, false);
        Check("the host shuts the door: shut here", await Until(() => !dock.IsOpen, 1));

        radio.AskLaunch();
        Check("asking to launch goes to the host", await Until(() => asked.Contains((GuestShipId, "HAVEN", (byte)RadioRequest.Launch)), 2));
        await Frames(60);
        Check("the door still shut: the clamps hold it", ship.Held && pad.State == LandingPad.PadState.Docked);
        host.Dock("HAVEN", dock.Number, true);
        Check("the host opens the door: the clamps lift it", await Until(() => pad.State == LandingPad.PadState.Launching, 1));
        Check("and let it go", await Until(() => !ship.Held, (float)pad.LaunchSeconds + 1));

        Check($"what the ship has on its hardpoints went to the host ({(fittings.Count > 0 ? fittings[^1] : "nothing")})",
              fittings.Count > 0 && fittings[^1] == ship.Fitted && ship.Fitted.Contains("Spine=res://assets/weapons/turrets/gun_1.tres"));
        var laser = GD.Load<Resource>("res://assets/weapons/lasers/lsr_1.tres");
        ship.Fit(new Dictionary<string, Resource> { ["NoseLeft"] = laser, ["NoseRight"] = laser, ["Spine"] = laser });
        Check("refitted, it goes again", await Until(() => fittings.Count > 0 && fittings[^1] == ship.Fitted && ship.Fitted.Contains("Spine=res://assets/weapons/lasers"), 2));

        _origin.QueueFree();
        _other = null;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    // The station; the first hangar's dock
    private HangarDock World()
    {
        _origin = new FloatingOrigin { Name = "World" };
        _origin.AddChild(new SectorClock());
        _origin.AddChild(new Projectiles());
        AddChild(_origin);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        _origin.AddChild(station);
        return station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
    }

    private static void Put(Thing ship, SessionNode node, Transform3D at)
    {
        ship.Position = node.Sector(at.Origin, 0);
        ship.Orientation = at.Basis.Orthonormalized().GetRotationQuaternion();
    }

    private string Last => _heard.Count > 0 ? _heard[^1] : "";

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_other == null)
                continue;
            uint tick = SectorClock.Instance.Tick;
            _net.Now = tick * Dt;
            _other.Tick(tick);
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
