using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Cargo ships seen, in the real game:
///
///   - a ship stands on a pad of its own, as the pad itself reckons it;
///   - its computer rests until someone deals with it;
///   - it leaves from that pad, the door opened for it, and the pad rests a while before others get it;
///   - near a station it asks for a pad and is given the nearest free one, which it goes down onto;
///   - destroyed, it is gone from the universe and its owner orders another;
///   - with no pad free it holds off and asks again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/cargo_view_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CargoViewTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Seconds(0.5);
        CargoView view = main.GetNode<CargoView>("CargoView");
        StationBook book = universe.Book;
        UniverseState state = universe.State;
        Check($"the universe has its ships ({state.CargoShips.Count})", state.CargoShips.Count == 8);
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;                              // only the one we set off

        // Parked: every ship has a pad of its own, and the one here stands on it
        CargoShipRecord record = state.CargoShips["ALPHA-1-1"], second = state.CargoShips["ALPHA-1-2"];
        Check($"each ship has a pad of its own ({record.Dock}:{record.Pad}, {second.Dock}:{second.Pad})", record.Pad != 0 && second.Pad != 0 && (record.Dock, record.Pad) != (second.Dock, second.Pad));
        await Seconds(2.5);
        StationBook.Station first = book.Find("ALPHA"), quartz = book.Find("QUARTZ");
        ShipModel model = GD.Load<ShipModel>(record.Model);
        StationRadio station = StationRadio.Called(first.Id);
        Check("a ship on its pad is shown there", view.Shown.ContainsKey(record.Id));
        if (!view.Shown.TryGetValue(record.Id, out ShipBody ship))
        {
            Finish();
            return;
        }
        StationBook.Pad pad = StationBook.PadOf(first, record.Dock, record.Pad);
        Vector3 rest = book.Rest(pad, model);
        Check($"at rest on it ({Off(ship, rest, main, first.Sector):0.00} m off)", Off(ship, rest, main, first.Sector) < 0.1f);
        LandingPad real = station.Docks.First(d => d.Number == record.Dock).PadNumbered(record.Pad);
        Check("as the pad itself reckons it", real.Docks(ship));

        // Detail: nobody deals with it, so its computer rests; hit, it wakes; left alone, it rests again
        Check("left alone it is at detail level 1", ship.Dormant);
        view.RestAfterSeconds = 2;
        Combat.Struck(ship, main.Ship);
        await Seconds(1.5);
        Check("hit, it wakes", !ship.Dormant);
        await Seconds(4);
        Check("left alone again, it rests", ship.Dormant);

        // Cleared to leave: from its own pad, with the door opened for it
        int oldDock = record.Dock, oldPad = record.Pad;
        record.NextLook = 0;
        record.State = CargoShipRecord.Returning;
        record.Going = "QUARTZ";
        record.Home = "QUARTZ";
        await Seconds(7);
        Check($"it asked to leave ({record.Phase}), and its pad is free for others", record.Phase == CargoShipRecord.Flying && !state.Station("ALPHA").CargoPads.ContainsKey(CargoShips.Key(oldDock, oldPad)));
        Check("the door opens for it", station.Docks.First(d => d.Number == oldDock).IsOpen);
        Check("the pad it left rests a while before anyone gets it", CargoShips.Taken(state, "ALPHA", oldDock, oldPad));
        Check($"it waits on its pad for the door ({Off(ship, rest, main, first.Sector):0.0} m off)", Off(ship, rest, main, first.Sector) < 1);
        await Seconds(model.DoorSeconds + 4);
        Check($"then it lifts off ({Off(ship, rest, main, first.Sector):0.0} m off)", Off(ship, rest, main, first.Sector) > 1);

        // Near a station it asks for a pad: the nearest free one, not always the first
        record.Phase = CargoShipRecord.Holding;
        record.Going = "ALPHA";
        record.State = CargoShipRecord.Returning;
        record.Home = "ALPHA";
        StationBook.Pad far = first.Pads.OrderByDescending(p => p.Door.DistanceTo(first.Pads[0].Door)).First();
        Vector3 toward = (far.Door - first.At).Normalized();
        record.ContactPoint = first.At + toward * model.AskAtMeters;
        record.NextLook = 0;
        foreach (StationBook.Pad taken in first.Pads.Where(p => p.Dock == far.Dock && p.Number != far.Number))
            state.Station("ALPHA").CargoPads[CargoShips.Key(taken.Dock, taken.Number)] = "somebody";
        await Seconds(7);
        Check($"it is given the nearest free pad ({record.Dock}:{record.Pad}, wanted {far.Dock}:{far.Number})", record.Phase == CargoShipRecord.Landing && record.Dock == far.Dock && record.Pad == far.Number);
        record.Left = state.Clock - (record.Arrives - record.Left) + 3;
        record.Arrives = state.Clock + 3;
        await Seconds(1);
        float high = Off(ship, book.Rest(far, model), main, first.Sector);
        Check($"it goes down onto it ({high:0.0} m off)", high > 0.5f && high < 20);
        await Seconds(10);
        Check($"and stands there ({record.Phase}, {Off(ship, book.Rest(far, model), main, first.Sector):0.00} m off)", record.Phase == CargoShipRecord.Parked && Off(ship, book.Rest(far, model), main, first.Sector) < 0.1f);

        state.Clock += model.PadRestSeconds + 1;
        Check("then it can be given again", !CargoShips.Taken(state, "ALPHA", oldDock, oldPad));

        // Destroyed where it can be seen: gone from the universe, its pad free, the wreck stays a while
        view.WreckSeconds = 3;
        string landed = CargoShips.Key(record.Dock, record.Pad);
        ship.Find<ShipHull>().Hit(100000);
        await Seconds(2.5);
        Check("destroyed, it is gone from the universe, and its pad is free", !state.CargoShips.ContainsKey(record.Id) && !state.Station("ALPHA").CargoPads.ContainsKey(landed));
        Check("its wreck is still there", GodotObject.IsInstanceValid(ship) && !view.Shown.ContainsKey(record.Id));
        await Seconds(4);
        Check("and then it goes", !GodotObject.IsInstanceValid(ship));
        Check("its owner orders another", state.ShipOrders.Values.Any(o => o.Home == "ALPHA"));

        // No free pad: it holds off and asks again
        var late = state.CargoShips["QUARTZ-1-1"];
        foreach (StationBook.Pad any in first.Pads)
            state.Station("ALPHA").CargoPads.TryAdd(CargoShips.Key(any.Dock, any.Number), "somebody");
        late.Dock = late.Pad = 0;
        late.Phase = CargoShipRecord.Holding;
        late.Going = "ALPHA";
        late.ContactPoint = first.At + Vector3.Right * model.AskAtMeters;
        late.NextLook = 0;
        await Seconds(7);
        Check($"with no pad free it holds ({late.Phase})", late.Phase == CargoShipRecord.Holding && late.Pad == 0);
        Finish();
    }

    private static float Off(Node3D ship, Vector3 at, SectorHost main, string sector) =>
        ship.GlobalPosition.DistanceTo(main.Root(sector).Origin.To(new SectorPoint(at.X, at.Y, at.Z)));

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
