using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Gates in a session, from both sides. First the game is the host and a
/// guest runs beside it as plain code: the guest asks the first sector's
/// gate for transit and its pilot pays the toll on the host; its ship goes
/// through and on to the second sector, where the host runs it; it comes
/// back through the gate there without clearance and the host fines its
/// pilot. Then the game is a guest of a host in plain code: its request for
/// transit goes to the host, and through the gate its ship jumps on its own
/// machine and the host hears it is in the second sector.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/session_gate_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SessionGateTest : Node
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

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity,
        };

        public void TakeHit(HitEvent hit) { }
    }

    private SimulatedNetwork _net;
    private Session _code;
    private CodeShip _ship;
    private string _heard = "";

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
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        var node = main.GetNode<SessionNode>("SessionNode");
        await Frames(2);
        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        node.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _code = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        _ship = new CodeShip { NetId = GuestShipId, IsLocal = true };
        _code.RadioHeard += (e, from, text, speech) => { if (e == _ship) _heard = text; };
        JumpGate gate2 = main.Sector.GetNode<JumpGate>("Gate2");
        Put(node, gate2, 0, 600, 0);
        _code.Move(0);
        _code.Add(_ship);
        _code.SetRadio(GuestShipId, "ACE", true);
        _code.Introduce("ace-pilot", "Ace");
        await Run(90);
        PilotRecord ace = Universe.Current.State.Pilot("ace-pilot");
        RemoteShip puppet = main.Sector.GetNodeOrNull<RemoteShip>($"Player{GuestPeer}");
        Check("the guest's ship is by the first sector's gate here", puppet != null && ace != null);

        long before = ace?.Credits ?? 0;
        _code.Request(GuestShipId, gate2.Radio.Sign, (byte)RadioRequest.Transit);
        Check($"asking for transit: the host's gate clears it ({_heard})", await Run(90, () => gate2.Cleared(puppet)));
        Check($"the toll comes out of the guest's pilot's credits ({before - ace?.Credits})", before - ace?.Credits == gate2.Radio.Laws.Toll);
        Check("the guest hears it", await Run(60, () => _heard.StartsWith("Cleared for transit")));

        await Through(node, gate2, 0, -100);
        Check($"through, cleared: no fine ({ace?.Credits})", Universe.Current.State.Station(gate2.Radio.Sign).CrimesOf("ace-pilot").Fines == 0);

        // On the other side: the host runs the second sector for it, and it comes back without clearance
        _code.Move(1);
        await Run(30, () => main.Root("sector_02") != null);
        JumpGate gate1 = main.Root("sector_02")?.GetNode<JumpGate>("Gate1");
        Check("the host runs the second sector for the guest", gate1 != null);
        Put(node, gate1, 1, -200, 0);
        await Run(120);
        Check("the guest's ship is there, by its gate", main.Root("sector_02")?.GetNodeOrNull<RemoteShip>($"Player{GuestPeer}") != null);
        await Through(node, gate1, 1, 100);
        Check($"back through without clearance: the host fines its pilot ({_heard})",
              await Run(60, () => Universe.Current.State.Station(gate1.Radio.Sign).CrimesOf("ace-pilot").Fines > 0));
        _code.Move(0);
        await Run(30);
        main.QueueFree();
        await Frames(5);
    }

    private async Task AsGuest()
    {
        _ship = null;
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        var node = main.GetNode<SessionNode>("SessionNode");
        await Frames(2);
        _net = new SimulatedNetwork { Latency = 0.05, Jitter = 0.01, Loss = 0.02 };
        _code = new Session(Session.HostPeer, _net.Join(Session.HostPeer)) { TickSeconds = Dt };
        (ushort Ship, string Station, byte What) asked = default;
        _code.Requested += (ship, station, what) => asked = (ship, station, what);
        node.Start(GuestPeer, _net.Join(GuestPeer));
        ShipBody ship = main.Ship;
        Check("the computer boots", await Run(90 * 60, () => ship.Computer.ScreenText().Contains("Radio resident")));

        JumpGate gate2 = main.Sector.GetNode<JumpGate>("Gate2");
        Basis b = gate2.GlobalBasis.Orthonormalized();
        ship.State = new ShipState { Position = gate2.ToGlobal(new Vector3(0, 0, 600)), Orientation = b.GetRotationQuaternion() };
        await Run(60);
        ship.Find<RadioDevice>().AskTransit();
        Check($"asking for transit goes to the host ({asked.Station})",
              await Run(120, () => asked.Ship == GuestShipId && asked.Station == gate2.Radio.Sign && asked.What == (byte)RadioRequest.Transit));

        ship.State = new ShipState { Position = gate2.ToGlobal(new Vector3(0, 0, 30)), Orientation = b.GetRotationQuaternion(), Velocity = b * new Vector3(0, 0, -100) };
        Check("through the gate, its ship jumps on its own machine", await Run(120, () => main.Here.Id == "sector_02"));
        Check("and the host hears it is in the second sector", await Run(60, () => _code.PlaceOf(GuestShipId) == 1));
        main.QueueFree();
        await Frames(5);
    }

    // The guest's code ship at a point on a gate's axis (its frame), in a sector, moving along the axis
    private void Put(SessionNode node, JumpGate gate, byte place, float along, float speed)
    {
        Basis b = gate.GlobalBasis.Orthonormalized();
        _ship.Position = node.Sector(gate.ToGlobal(new Vector3(0, 0, along)), place);
        _ship.Velocity = b * new Vector3(0, 0, speed);
    }

    // The code ship flies through a gate from 40 m out, at 100 m/s
    private async Task Through(SessionNode node, JumpGate gate, byte place, float speed)
    {
        Put(node, gate, place, -Mathf.Sign(speed) * 40, speed);
        await Run(60);
        _ship.Velocity = Vector3.Zero;
        await Run(30);
    }

    // Both machines run so many ticks (until done, if given); the code ship flies on
    private async Task<bool> Run(int ticks, Func<bool> done = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (done?.Invoke() == true)
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance?.Tick ?? 0;
            _net.Now = tick * Dt;
            if (_ship != null)
                _ship.Position += _ship.Velocity * Dt;
            _code.Tick(tick);
        }
        return done?.Invoke() ?? true;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
