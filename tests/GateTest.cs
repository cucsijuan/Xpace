using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The gates of the game's two sectors. Asked from beyond the ship's
/// radio's reach, the gate does not answer, and the radio says so. The
/// player asks the first sector's gate for transit: cleared, the toll taken. Through it the ship is in the
/// second sector, held in the tunnel past the gate there, and goes on with
/// the velocity it went in with. Back through the second gate without
/// clearance: it jumps all the same, and the gate fines the pilot; again
/// while the fine is owed, a bounty. With too few credits the toll is
/// refused.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/gate_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class GateTest : Node
{
    private int _failures;
    private SectorHost _main;
    private ShipBody _ship;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(_main);
        _ship = _main.Ship;
        var radio = _ship.Find<RadioDevice>();
        PilotRecord me = universe.Me;
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));

        // From 30 km the gate is heard, but the ship's radio does not reach it: no answer, and it says so
        JumpGate gate2 = Gate("Gate2");
        Place(gate2, 30_000, 0);
        await Seconds(1.5);
        radio.AskTransit();
        await Seconds(4);
        Check($"asked from too far, the radio says no one answered ({Heard(radio)})",
              Heard(radio) == $"No answer from {gate2.Radio.Sign}." && !gate2.Cleared(_ship));

        // Cleared through the first sector's gate, the toll paid
        Place(gate2, 600, 0);
        await Seconds(1.5);
        long before = me.Credits;
        radio.AskTransit();
        bool cleared = await Until(() => gate2.Cleared(_ship), 3);
        Check($"the gate clears the ship ({Heard(radio)})", cleared);
        Check($"and takes the toll ({before - me.Credits} credits)", before - me.Credits == gate2.Radio.Laws.Toll && gate2.Radio.Laws.Toll > 0);

        // Through it: the other sector, in the tunnel, then on as it went in
        Place(gate2, 30, 100);
        Check("through the ring, the ship is in the second sector", await Until(() => _main.Here.Id == "sector_02", 2));
        JumpGate gate1 = Gate("Gate1");
        Check("held in transit past its gate, the tunnel round it",
              _ship.Holder is GateTransit && _ship.GetChildren().OfType<WarpTunnel>().Any() &&
              gate1.ToLocal(_ship.GlobalPosition).Z < -gate1.ExitMargin * 0.9f);
        Check("the first sector is let go", _main.Root("sector_01") == null);
        Vector3 kept = ((GateTransit)_ship.Holder).Velocity;
        Check("let go after the tunnel", await Until(() => _ship.Holder == null, (float)gate1.TransitSeconds + 1));
        Vector3 v = gate1.GlobalBasis.Inverse() * _ship.LinearVelocity;
        Check($"going on as it went in, through the gate ({v.Z:0} m/s; went in at {kept.Length():0})",
              v.Z < -50 && Mathf.Abs(v.X) < 3 && Mathf.Abs(v.Y) < 3 && Mathf.Abs(v.Length() - kept.Length()) < 3);
        Check("the tunnel is gone", await Until(() => !_ship.GetChildren().OfType<WarpTunnel>().Any(), 1));

        // Back without clearance: a fine with the second sector's gate
        Place(gate1, -30, 100);
        Check("back through without clearance, it jumps all the same", await Until(() => _main.Here.Id == "sector_01", 2));
        long fine = universe.State.Station("GATE1").CrimesOf(me.Id).Fines;
        Check($"and is fined by the gate it went through ({fine} credits)", fine > 0);
        await Until(() => _ship.Holder == null, 6);

        // Through the first gate, then the second again without clearance while owing the fine: a bounty
        gate2 = Gate("Gate2");
        radio.AskTransit();
        await Until(() => gate2.Cleared(_ship), 3);
        Place(gate2, 30, 100);
        await Until(() => _main.Here.Id == "sector_02", 2);
        await Until(() => _ship.Holder == null, 6);
        gate1 = Gate("Gate1");
        Place(gate1, -30, 100);
        await Until(() => _main.Here.Id == "sector_01", 2);
        CrimeRecord crimes = universe.State.Station("GATE1").CrimesOf(me.Id);
        Check($"again while owing it: a bounty ({crimes.Bounty} credits)", crimes.Bounty > 0);
        await Until(() => _ship.Holder == null, 6);

        // Too poor for the toll
        gate2 = Gate("Gate2");
        Place(gate2, 600, 0);
        await Seconds(1.5);
        me.Credits = 10;
        radio.AskTransit();
        await Seconds(2);
        Check($"too poor, the toll is refused ({Heard(radio)})", !gate2.Cleared(_ship) && me.Credits == 10 && Heard(radio).StartsWith("Transit denied"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private JumpGate Gate(string name) => _main.Sector.GetNode<JumpGate>(name);

    // The ship on the gate's axis, so far out on its +Z side (or -Z), coming in at that speed
    private void Place(JumpGate gate, float along, float speed)
    {
        Basis b = gate.GlobalBasis.Orthonormalized();
        Vector3 at = gate.ToGlobal(new Vector3(0, 0, along));
        Basis facing = along >= 0 ? b : b * new Basis(Vector3.Up, Mathf.Pi);
        _ship.State = new ShipState
        {
            Position = at, Orientation = facing.GetRotationQuaternion(),
            Velocity = b * new Vector3(0, 0, along >= 0 ? -speed : speed),
        };
    }

    private static string Heard(RadioDevice radio) => radio.Heard.Count > 0 ? radio.Heard[^1].Text : "nothing";

    private async Task Seconds(double s) => await Until(() => false, s);

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
