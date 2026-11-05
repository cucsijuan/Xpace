using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Escorts are ships like any other, the owner's: ordered from a yard after a loss, by the kind of owner that does;
/// they set out with a ship from where it is, at its pace, and come home; a ship with one goes where its owner would
/// not send it alone; where they can be seen they fight who attacks their ward, flown as any fighter, and when it is
/// over they go back on their way from where they are, with no jump.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/escort_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class EscortTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Records();
        await Fight();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static CargoShipRecord AddEscort(UniverseState state, string home, string owner, ShipModel model)
    {
        var escort = new CargoShipRecord { Id = $"{home}-E", Model = model.ResourcePath, Owner = owner, Home = home, At = home };
        state.CargoShips[escort.Id] = escort;
        return escort;
    }

    private void Records()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres"), guard = GD.Load<ShipModel>("res://assets/ship/models/escort.tres");
        var state = new UniverseState { Clock = 1 };
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        CargoShips.Muster(state, book, Account);
        CargoShips.Muster(state, book, Account);
        Check("nobody starts with escorts", state.CargoShips.Values.All(s => s.Model != guard.ResourcePath));

        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        var miltec = (OrganisationRecord)Account(ship.Owner);
        Personality bold = OrganisationCatalog.Find(ship.Owner).Personality;
        StationBook.Station alpha = book.Find("ALPHA");
        var legs = book.Departure(alpha, alpha.Pads[0], book.Find("DRYDOCK"), freighter, 0, out _);
        StationBook.Leg open = legs.First(l => l.Open);
        Reactions.Happened(miltec, bold, freighter, open.Sector, open.From.Lerp(open.To, 0.5f), "", state.Clock, lost: true);
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check($"after a loss the owner orders an escort ({string.Join(", ", state.ShipOrders.Values.Select(o => o.Model.GetFile()))})", state.ShipOrders.Values.Any(o => o.Home == "ALPHA" && o.Model == guard.ResourcePath));
        state.ShipOrders.Clear();

        // Short of something: alone it would wait, with an escort beside it it goes
        MarketGood short_ = alpha.Goods.First(g => g.Good.Id == "iron");
        state.Clock += 1800;
        state.Station("ALPHA").Stock["iron"] = short_.Stock * 0.3;
        ship.NextLook = 0;
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check("on a way it fears, it stays", ship.Phase == CargoShipRecord.Parked);
        state.ShipOrders.Clear();
        CargoShipRecord escort = AddEscort(state, "ALPHA", ship.Owner, guard);
        StationBook.Pad pad = alpha.Pads.First(p => book.Fits(p, guard) && !CargoShips.Taken(state, "ALPHA", p.Dock, p.Number));
        state.Station("ALPHA").CargoPads[CargoShips.Key(pad.Dock, pad.Number)] = escort.Id;
        (escort.Dock, escort.Pad) = (pad.Dock, pad.Number);
        state.Station("ALPHA").Stock["iron"] = short_.Stock * 0.3;
        ship.NextLook = 0;
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check($"with an escort it goes ({ship.Going})", ship.Phase == CargoShipRecord.Flying);
        Check("and the escort goes with it", escort.Phase == CargoShipRecord.Flying && escort.Ward == ship.Id && escort.Going == ship.Going);
        StationBook.Fix a = book.FixOf(ship, freighter, state.Clock + 300).Value, b = book.FixOf(escort, guard, state.Clock + 300).Value;
        Check($"at its pace ({a.At.DistanceTo(b.At):0} m off after 5 minutes)", a.At.DistanceTo(b.At) < 3000);

        for (int i = 0; i < 20000 && !(ship.Phase == CargoShipRecord.Parked && ship.At == "ALPHA" && ship.State == CargoShipRecord.Waiting && escort.Phase == CargoShipRecord.Parked); i++)
        {
            state.Clock += 5;
            CargoShips.Tick(state, book, Account);
        }
        Check($"both come home, the escort free again ({ship.At}, {escort.At}, ward '{escort.Ward}')", ship.At == "ALPHA" && escort.At == "ALPHA" && escort.Ward == "");

        // Left alone far from home it goes back by itself
        escort.At = "QUARTZ";
        escort.Dock = escort.Pad = 0;
        escort.NextLook = 0;
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check("left with nobody to guard, it goes home", escort.Phase == CargoShipRecord.Flying && escort.Going == "ALPHA");

        // From a point in space
        Vector3 at = open.From.Lerp(open.To, 0.3f);
        CargoShips.Resume(escort, guard, open.Sector, at, state, book, Account);
        Check("it can set out from where it is", book.FixOf(escort, guard, state.Clock).Value.At.DistanceTo(at) < 1);
    }

    private async Task Fight()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        ShipBody me = main.Ship;
        CargoView view = main.GetNode<CargoView>("CargoView");
        view.StandDownSeconds = 3;
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres"), guard = GD.Load<ShipModel>("res://assets/ship/models/escort.tres");
        CargoShipRecord ward = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        CargoShipRecord escort = AddEscort(state, "ALPHA", ward.Owner, guard);

        // Both flying, out in the open
        StationBook.Station from = book.Find("ALPHA"), to = book.Find("QUARTZ");
        foreach (CargoShipRecord s in new[] { ward, escort })
        {
            var way = book.Departure(from, null, to, freighter, 0, out Vector3 contact);
            s.Phase = CargoShipRecord.Flying;
            s.From = from.Id;
            s.Going = to.Id;
            s.LeftDock = s.LeftPad = s.Dock = s.Pad = 0;
            s.PaceModel = freighter.ResourcePath;
            s.ContactPoint = contact;
            s.Left = state.Clock - way.Sum(l => l.Seconds) * 0.5;
            s.Arrives = s.Left + way.Sum(l => l.Seconds + l.Transit);
            s.WorkedTo = state.Clock;
        }
        escort.Ward = ward.Id;
        escort.State = CargoShipRecord.Escorting;
        await Seconds(3);
        Check("both are shown", view.Shown.ContainsKey(ward.Id) && view.Shown.ContainsKey(escort.Id));
        if (!view.Shown.TryGetValue(escort.Id, out ShipBody fighter) || !view.Shown.TryGetValue(ward.Id, out ShipBody freight))
            return;

        // The attacker: a target of the fighter's, near it
        ShipState ours = fighter.State;
        me.State = new ShipState { Position = ours.Position + new Vector3(0, 300, 0), Orientation = Quaternion.Identity };
        Combat.Struck(freight, me);
        await Seconds(1.5);
        FighterAi pilot = fighter.Find<FighterAi>();
        Check($"attacked, the ward's escort leaves its way ({escort.Phase})", escort.Phase == CargoShipRecord.Loose && fighter.Holder == null && !fighter.Dormant);
        Check("its orders are the attacker", pilot.Engagement.Hostiles.Contains(me));
        Check("the ward goes on", ward.Phase == CargoShipRecord.Flying && freight.Holder != null);

        // Its enemy gone, it goes back on its way from where it is
        foreach (Node3D enemy in pilot.Engagement.Hostiles.ToList())
            (pilot.Engagement as EscortDuty)?.Forget(enemy);
        Vector3 before = fighter.GlobalPosition;
        await Until(() => escort.Phase == CargoShipRecord.Flying, 8);
        Check($"with nobody to fight it goes back on its way ({escort.Going})", escort.Phase == CargoShipRecord.Flying && fighter.Holder is CargoRun);
        await Seconds(0.2);
        Check($"from where it was ({fighter.GlobalPosition.DistanceTo(before):0} m on)", fighter.GlobalPosition.DistanceTo(before) < 20000);
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

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
