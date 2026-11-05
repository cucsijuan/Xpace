using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Under fire a cargo ship alone leaves its line: its pilot flies it away from who attacked it, on its generator, fights
/// nobody, and once clear goes back on its record to where it was going, from where it is. A fight goes on after the
/// player leaves its sector, with nothing drawn, its results reach the records, and the sector goes when it is quiet.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/run_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RunTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        ShipBody me = main.Ship;
        CargoView view = main.GetNode<CargoView>("CargoView");
        view.StandDownSeconds = 2;
        view.ClearMeters = 1500;
        main.FightQuietSeconds = 3;
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        StationBook.Station from = book.Find("ALPHA"), to = book.Find("QUARTZ");
        var way = book.Departure(from, null, to, freighter, 0, out Vector3 contact);
        ship.Phase = CargoShipRecord.Flying;
        ship.State = CargoShipRecord.Fetching;
        ship.From = from.Id;
        ship.Going = to.Id;
        ship.LeftDock = ship.LeftPad = ship.Dock = ship.Pad = 0;
        ship.ContactPoint = contact;
        ship.Left = state.Clock - way.Sum(l => l.Seconds) * 0.5;
        ship.Arrives = ship.Left + way.Sum(l => l.Seconds + l.Transit);
        ship.WorkedTo = state.Clock;
        await Seconds(3);
        if (!view.Shown.TryGetValue(ship.Id, out ShipBody body))
        {
            Check("the ship is shown", false);
            End();
            return;
        }

        // Attacked with nobody to guard it
        me.State = new ShipState { Position = body.State.Position + new Vector3(0, 300, 0), Orientation = Quaternion.Identity };
        Combat.Struck(body, me);
        await Seconds(1.5);
        Check($"attacked alone, it leaves its way ({ship.Phase})", ship.Phase == CargoShipRecord.Loose && body.Holder == null && !body.Dormant);
        FighterAi pilot = body.Find<FighterAi>();
        Check("it fights nobody", !pilot.Engagement.Hostiles.Any());
        float was = body.GlobalPosition.DistanceTo(me.GlobalPosition);
        await Seconds(25);
        float now = body.GlobalPosition.DistanceTo(me.GlobalPosition);
        Check($"it runs from the attacker ({was:0} m -> {now:0} m)", now > was + 200);

        // Clear of them, it goes on
        await Until(() => ship.Phase == CargoShipRecord.Flying, 120);
        Check($"clear of them, it is back on its record ({ship.Phase})", ship.Phase == CargoShipRecord.Flying && body.Holder is CargoRun);
        Check("still going where it was going, to fetch what it went for", ship.Going == to.Id && ship.State == CargoShipRecord.Fetching);

        // A fight goes on when the player leaves
        string here = main.Here.Id, other2 = main.Map.Sectors.First(s => s.Id != here).Id;
        ShipBody victim = view.Shown[ship.Id];
        Combat.Struck(victim, me);
        await Seconds(1);
        main.Jump(other2, new Transform3D(Basis.Identity, new Vector3(0, 0, 20000)));
        await Seconds(1);
        Check("the sector left in a fight goes on running", main.Root(here) != null && !SectorHost.Seen(victim));
        victim.Find<ShipHull>().Burn(1e7f);
        await Seconds(2);
        Check("the ship destroyed there is gone from the records", !state.CargoShips.ContainsKey(ship.Id));
        await Until(() => main.Root(here) == null, 20);
        Check("quiet, the sector goes", main.Root(here) == null);
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

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60 && !done(); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
