using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.OnFoot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Players on foot in a session: this scene is the host, with the Synty station and a walker of its own in the
/// first hangar; two guests are plain code over a simulated network.
///
///   - a guest's player on foot shows on the host: their body in the station of the same callsign, where they are
///     in its frame, facing as they face, with their ship's callsign over the head;
///   - walking, the body follows them smoothly and its animations go as fast as they walk;
///   - the other guest sees them too (the host relays them);
///   - the host's own player on foot reaches the guests, in the station's frame;
///   - on foot at the station, the guest is there for its lockers (no ship needed); back in the seat, they are gone
///     everywhere and no longer there;
///   - a guest that goes silent is gone after a while.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_walker_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionWalkerTest : Node
{
    private const float Dt = 1f / 60;
    private const byte Ace = 1, Bea = 2;
    private int _failures;

    private FloatingOrigin _origin;
    private SimulatedNetwork _net;
    private readonly Dictionary<byte, Session> _guests = new();
    private readonly HashSet<byte> _silent = new();

    public override async void _Ready()
    {
        Universe.Current.Forget();
        _origin = new FloatingOrigin { Name = "World" };
        _origin.AddChild(new SectorClock());
        _origin.AddChild(new Projectiles());
        AddChild(_origin);
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        station.Position = new Vector3(300, -50, 1200);
        station.Rotation = new Vector3(0, 0.7f, 0);
        _origin.AddChild(station);
        var host = new SessionNode();
        _origin.AddChild(host);
        await Frames(1);

        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        host.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        foreach (byte peer in new[] { Ace, Bea })
            _guests[peer] = new Session(peer, _net.Join(peer)) { TickSeconds = Dt };
        var beaSees = new Dictionary<byte, WalkerState>();
        var beaGone = new List<byte>();
        _guests[Bea].WalkerMoved += (peer, s) => beaSees[peer] = s;
        _guests[Bea].WalkerGone += peer => beaGone.Add(peer);
        _guests[Ace].SetRadio(NetIds.PlayerShip(Ace), "ACE", true);
        await Frames(60);

        // Ace on foot in the station, standing
        var hangar = station.GetNode<Node3D>("HangarBig1");
        Vector3 at = station.ToLocal(hangar.ToGlobal(new Vector3(-3, 0, -40)));     // on the floor, before the kiosks
        Quaternion facing = new Basis(Vector3.Up, 1.2f).GetRotationQuaternion();
        Walk(Ace, at, facing, Vector3.Zero);
        RemoteWalker ace = null;
        bool shown = await Until(() => (ace = host.WalkerOf(Ace)) != null, 2);
        await Frames(30);
        Check("a guest on foot shows on the host, in the station of the same callsign",
              shown && ace.GetParent() == station);
        Check($"where they are in its frame ({(ace?.Position ?? default).DistanceTo(at):0.00} m off), facing as they face",
              ace != null && ace.Position.DistanceTo(at) < 0.05f && ace.Basis.GetRotationQuaternion().AngleTo(facing) < 0.05f);
        Check($"with their ship's callsign over the head ({ace?.Pilot})", ace?.Pilot == "ACE");

        // Walking at a jog along the station's X
        var velocity = new Vector3(3.6f, 0, 0);
        float worst = 0;
        Vector3 last = ace.Position;
        float biggestStep = 0;
        for (int i = 0; i < 120; i++)
        {
            Walk(Ace, at + velocity * (i * Dt), facing, velocity);
            await Frames(1);
            if (i > 30)
                worst = Mathf.Max(worst, ace.Position.DistanceTo(at + velocity * (i * Dt)));
            biggestStep = Mathf.Max(biggestStep, ace.Position.DistanceTo(last));
            last = ace.Position;
        }
        Check($"walking, the body follows within {worst:0.00} m (latency and smoothing), never jumping ({biggestStep * 100:0.0} cm a frame at most)",
              worst < 0.6f && biggestStep < 0.12f);
        Check($"and its animations go as fast as they walk ({ace.Body?.Speed:0.0} m/s)", ace.Body != null && Mathf.Abs(ace.Body.Speed - 3.6f) < 0.01f);
        float behind = beaSees.TryGetValue(Ace, out WalkerState seen) ? seen.Position.DistanceTo(at + velocity * (119 * Dt)) : float.MaxValue;
        Check($"the other guest sees them too, through the host ({behind:0.00} m behind: two trips)",
              seen.Station == "HAVEN" && behind < 1f);

        // The host's own walker, in the first hangar
        var aceSees = new Dictionary<byte, WalkerState>();
        _guests[Ace].WalkerMoved += (peer, s) => aceSees[peer] = s;
        var mine = GD.Load<PackedScene>("res://scenes/characters/walker.tscn").Instantiate<Walker>();
        hangar.AddChild(mine);
        mine.Position = new Vector3(-2, 1, 5);
        mine.SetPhysicsProcess(false);
        Vector3 inStation = station.ToLocal(mine.GlobalPosition);
        Check("the host's own player on foot reaches the guests, in the station's frame",
              await Until(() => aceSees.TryGetValue(Session.HostPeer, out WalkerState s) && s.Station == "HAVEN" && s.Position.DistanceTo(inStation) < 0.01f, 2));

        // Lockers: on foot there is being there
        var documents = new Dictionary<string, string>();
        _guests[Ace].DocumentArrived += (topic, text) => documents[topic] = text;
        _guests[Ace].Introduce("ace-pilot", "Ace");
        await Until(() => Universe.Current.PilotOfPeer(Ace) == "ace-pilot", 2);
        Check("on foot at the station, the guest is there for its lockers (no ship needed)", await Lockers(documents) is { Present: true });

        // Back in the seat
        _guests[Ace].Walk(null);
        Check("back in the seat: gone from the host and from the other guest",
              await Until(() => host.WalkerOf(Ace) == null && beaGone.Contains(Ace), 2));
        Check("and no longer there for the lockers", await Lockers(documents) is { Present: false });

        // Silent
        Walk(Ace, at, facing, Vector3.Zero);
        await Until(() => host.WalkerOf(Ace) != null, 2);
        _silent.Add(Ace);
        await Frames((int)(1.5f / Dt));
        bool waited = host.WalkerOf(Ace) != null;
        Check("a guest that goes silent is gone after a while", waited && await Until(() => host.WalkerOf(Ace) == null, 3));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Walk(byte peer, Vector3 at, Quaternion facing, Vector3 velocity) =>
        _guests[peer].Walk(new WalkerState { Station = "HAVEN", Position = at, Facing = facing, Velocity = velocity, Grounded = true });

    private async Task<LockerView> Lockers(Dictionary<string, string> documents)
    {
        documents.Remove(StationLockers.Topic);
        _guests[Ace].Ask(StationLockers.Topic, System.Text.Json.JsonSerializer.Serialize(new LockerRequest { Station = "HAVEN" }));
        if (!await Until(() => documents.ContainsKey(StationLockers.Topic), 2))
            return null;
        return System.Text.Json.JsonSerializer.Deserialize<LockerView>(documents[StationLockers.Topic]);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_net == null)
                continue;
            uint tick = SectorClock.Instance.Tick;
            _net.Now = tick * Dt;
            foreach ((byte peer, Session guest) in _guests)
                if (!_silent.Contains(peer))
                    guest.Tick(tick);
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
