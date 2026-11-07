using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Free people and cargo in a session: the game is the host and a guest runs beside it as plain code. The guest sees
/// the free people's ships and the pods that are spilt; its pilot's jettison goes to the host, which throws out what
/// its record holds; flying past a pod its pilot gets the cargo on the host; and a bounty is paid to the guest's pilot.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_people_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionPeopleTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class CodeShip : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;
        public Vector3 Velocity;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity };

        public void TakeHit(HitEvent hit) { }
    }

    private SimulatedNetwork _net;
    private Session _code;
    private CodeShip _ship;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        var node = main.GetNode<SessionNode>("SessionNode");
        await Frames(2);
        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        node.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _code = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        int podScene = node.Spawnables.ToList().FindIndex(s => s.ResourcePath.EndsWith("cargo_pod.tscn"));
        Check("pods are among what can appear on other machines", podScene >= 0);
        int pods = 0;
        _code.SpawnDynamic = (id, scene) =>
        {
            if (scene == podScene)
                pods++;
            return new CodeShip { NetId = id, IsLocal = false, Track = new RemoteTrack() };
        };
        _ship = new CodeShip { NetId = GuestShipId, IsLocal = true, Position = new SectorPoint(3000, 5000, 3000) };
        _code.Move(0);
        _code.Add(_ship);
        _code.SetRadio(GuestShipId, "ACE", true);
        _code.Introduce("ace-pilot", "Ace");
        await Run(90);
        PilotRecord ace = Universe.Current.State.Pilot("ace-pilot");
        RemoteShip puppet = main.Sector.GetNodeOrNull<RemoteShip>($"Player{GuestPeer}");
        Check("the guest's ship is here", puppet != null);
        if (puppet == null)
        {
            End();
            return;
        }

        ace.Ship.Cargo["iron"] = 4;
        Universe.Current.Touch(ace);

        // A guest's scanner asks the host what a hold has
        string reading = null;
        _code.DocumentArrived += (topic, text) => reading = topic == Universe.ScanTopic ? text : reading;
        _code.Ask(Universe.ScanTopic, $"{GuestShipId} 7");
        Check("the host reads a hold for a guest's scanner", await Run(120, () => reading != null));
        var read = reading == null ? null : System.Text.Json.JsonSerializer.Deserialize<ScanReading>(reading);
        Check($"and answers what it asked ({reading})", read?.Asked == 7 && read.Tonnes == 4 && read.Cargo.GetValueOrDefault("iron") == 4);

        _code.Ask(Universe.JettisonTopic, "");
        Check("the guest's jettison empties its pilot's hold on the host", await Run(120, () => ace.Ship.Tonnes == 0));
        Check($"the guest sees the pod ({pods})", await Run(120, () => pods == 1));

        CargoPod pod = Sectors.InGroup(puppet, CargoPod.Group).OfType<CargoPod>().FirstOrDefault();
        Check("the pod drifts on the host", pod != null);
        if (pod != null)
        {
            await Run(60 * 6);
            _ship.Position = FloatingOrigin.Of(puppet).Origin + pod.Position;
            _ship.Velocity = pod.Velocity;
            Check($"flying past it, the guest's pilot gets the cargo on the host ({ace.Ship.Tonnes} t)", await Run(240, () => { _ship.Position = FloatingOrigin.Of(puppet).Origin + pod.Position; return ace.Ship.Tonnes == 4; }));
        }

        // A price on a free person's head, paid to the guest's pilot
        var state = Universe.Current.State;
        state.People["person-9"] = new PersonRecord { Id = "person-9", First = "Test", Last = "Pirate" };
        var payer = (OrganisationRecord)Universe.Current.Account("gate_authority");
        payer.Credits = 10_000;
        payer.Bounties["person-9"] = 3000;
        var prey = GD.Load<PackedScene>("res://scenes/ships/escort_fighter.tscn").Instantiate<ShipBody>();
        prey.Actor = "person-9";
        foreach (Xpace.Computer.ShipComputer c in prey.FindChildren("*", "", true, false).OfType<Xpace.Computer.ShipComputer>())
            c.PowerOnAtStart = false;
        main.Sector.AddChild(prey);
        long before = ace.Credits;
        Combat.Lost(prey, puppet, prey.GlobalPosition);
        Check($"the bounty goes to the guest's pilot ({ace.Credits - before})", ace.Credits - before == 3000);
        End();
    }

    private void End()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Run(int steps, Func<bool> done = null)
    {
        for (int i = 0; i < steps; i++)
        {
            if (done?.Invoke() == true)
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance?.Tick ?? 0;
            _net.Now = tick * Dt;
            _code.Tick(tick);
        }
        return done?.Invoke() ?? true;
    }
}
