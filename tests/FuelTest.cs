using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Fuel: flying on the engine costs none; the cruise generator burns hydrogen from the tank, and with it dry drops out
/// and won't start; the computer sees the tank; a ship on a record fills up and pays, burns what its way takes, and
/// without enough stays where it is. A new ship from the insurer comes full.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/fuel_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class FuelTest : Node
{
    private int _failures;
    private PilotControls _controls;

    public override async void _Ready()
    {
        Records();
        await Flying();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Records()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        var state = new UniverseState();
        IAccount Account(string owner) => string.IsNullOrEmpty(owner) ? null : state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 5_000_000 });
        CargoShips.Muster(state, book, Account);
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == "ALPHA");
        var model = GD.Load<ShipModel>(ship.Model);
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e15;
        StationBook.Station alpha = book.Find("ALPHA"), far = book.Find("DRYDOCK");
        double need = book.Seconds(alpha, far, model, 0) * model.Generator.FuelPerHour / 3600;
        Check($"a voyage takes fuel ({need:0.00} t of a tank of {model.Hull.FuelTank})", need > 0 && need < model.Hull.FuelTank);

        ship.State = CargoShipRecord.Returning;
        ship.Home = far.Id;
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"setting out it burns what the way takes ({ship.Fuel:0.00} t left)", ship.Phase == CargoShipRecord.Flying && Math.Abs(ship.Fuel - (model.Hull.FuelTank - need)) < 0.01);

        // Dry at a station with none to sell, it stays
        CargoShipRecord dry = state.CargoShips.Values.First(s => s != ship && s.Home == "DRYDOCK");
        StationRecord yard = CargoShips.Bring(state, far, Account);
        yard.Stock["hydrogen"] = 0;
        dry.Fuel = 0;
        dry.At = far.Id;
        dry.Home = alpha.Id;
        dry.State = CargoShipRecord.Returning;
        dry.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check("with too little fuel and none to buy, it stays where it is", dry.Phase == CargoShipRecord.Parked);
        long money = Account(dry.Owner).Credits;
        yard.Stock["hydrogen"] = 100;
        dry.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"with some to buy it fills up, pays and goes ({dry.Fuel:0.00} t)", dry.Phase == CargoShipRecord.Flying && dry.Fuel > 0);
    }

    private async Task Flying()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        ShipBody ship = main.Ship;
        var cruise = ship.Find<CruiseDevice>();
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        ship.ControlsSource = () => _controls;
        PilotRecord pilot = Universe.Current.RecordOf(ship);
        Check("the ship has a tank, and comes full", ship.Spec.FuelTank > 0 && cruise.Fuel(ship) == ship.Spec.FuelTank);
        await Until(() => ship.Computer.ScreenText().Contains("Comm"), 60);
        await Seconds(20);
        ship.State = new ShipState { Position = new Vector3(60000, 30000, -60000), Orientation = Quaternion.Identity };
        await Seconds(1);
        double full = cruise.Fuel(ship);
        _controls.Throttle = 1;
        await Seconds(3);
        Check("flying on the engine burns none", cruise.Fuel(ship) == full);
        Check($"the computer sees the tank ({ship.Computer.Bus.Word(CargoHold.Slot, 12)} of {ship.Computer.Bus.Word(CargoHold.Slot, 14)})", ship.Computer.Bus.Word(CargoHold.Slot, 14) == (int)(ship.Spec.FuelTank * 16));
        await Button();
        await Until(() => cruise.On && !cruise.Charging, 10);
        Check($"the generator runs ({cruise.On})", cruise.On);
        double was = cruise.Fuel(ship);
        await Seconds(10);
        double burnt = was - cruise.Fuel(ship);
        Check($"in cruise it burns ({burnt:0.0000} t in 10 s)", Math.Abs(burnt - 10 * cruise.Spec.FuelPerHour / 3600) < 0.002);

        pilot.Ship.Fuel = 0.002;
        await Until(() => !cruise.On, 5);
        Check("with the tank dry it drops out", !cruise.On && cruise.DroppedOut);
        Check("the computer is told", (ship.Computer.Bus[CruiseDevice.Slot, 2] & CruiseDevice.StatusDry) != 0);
        await Button();
        await Seconds(2);
        Check("and it won't start", !cruise.On && !cruise.Charging);
        Universe.Current.Lose(ship);
        Check("the insurer's new ship comes full", cruise.Fuel(ship) == ship.Spec.FuelTank);
    }

    private async Task Button()
    {
        _controls.Cruise = true;
        await Seconds(0.3);
        _controls.Cruise = false;
        await Seconds(0.3);
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
