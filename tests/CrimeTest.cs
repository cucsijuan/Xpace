using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// What a station holds against pilots (ships here are hulls with a radio, their computers off; each named pilot
/// flies one):
///
///   - the rules: infamy wears off with the pilot's played time, not the universe's; paying takes all that is owed;
///     surrendering seizes the cargo, then impounds the ship and gives a courtesy ship; paying it all releases it;
///   - firing on after the warning: a fine; damaging a ship that did nothing: a bounty; destroying one: a big bounty
///     and infamy; all on the pilot's record with the station;
///   - a ship with its radio off can't be told: what it does costs its pilot nothing (it is still engaged);
///   - a notorious pilot is refused docking, and attacked on sight once the station can tell who it is; slipping into
///     a hangar with the radio off, they pay what they owe at face value (the infamy stays);
///   - infamy gone with play time, docking again;
///   - a factor at another station settles what is owed here, for half as much again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/crime_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CrimeTest : Node3D
{
    private StationRadio _station;
    private StationSecurity _security;
    private int _failures;

    public override async void _Ready()
    {
        Rules();

        Universe universe = Universe.Current;
        universe.Forget();
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _security = station.GetNode<StationSecurity>("Security");
        _security.PoliceScene = null;           // no police: only the records
        UniverseState state = universe.State;
        CrimeRecord Of(string pilot) => state.Station(_station.Sign).CrimesOf(pilot);

        ShipBody a = Ship("Alpha", new Vector3(0, 0, 2500), "ann");
        ShipBody b = Ship("Bravo", new Vector3(300, 0, 2500), "bob");
        ShipBody c = Ship("Charlie", new Vector3(-300, 0, 2500), "cat");
        ShipBody d = Ship("Delta", new Vector3(0, 300, 2500), "dan");
        await Seconds(1);

        Fire(a);
        await Seconds(1);
        Fire(a);
        await Seconds(4.5);
        Fire(a);
        await Seconds(0.2);
        Check($"firing on after the warning: a fine on the pilot's record ({Of("ann").Fines})", Of("ann").Fines == _security.FiringFine && Of("ann").Bounty == 0);

        Strike(c, b, 10);
        await Seconds(0.2);
        Check($"damaging a ship that did nothing: a bounty, told ({Last(b)})",
              Of("bob").Bounty == _security.DamageBounty && _security.StandingOf(b) == Standing.Hostile && Last(b).Contains("bounty"));
        Strike(c, b, 10);
        await Seconds(0.2);
        Check("hitting it again at once is the same attack", Of("bob").Bounty == _security.DamageBounty);

        Strike(d, c, 10000);
        await Seconds(0.2);
        Check($"destroying one: a big bounty and infamy ({Justice.Infamy(Of("cat"), state.Pilot("cat")):0.0})",
              Of("cat").Bounty == _security.KillBounty && Justice.IsNotorious(Of("cat"), state.Pilot("cat")) && Last(c).Contains("destroyed"));

        // Charlie's pilot, notorious, in a new ship (the old hostility forgotten) asks to dock
        c.Name = "Charlie1";
        ShipBody c2 = Ship("Charlie2", new Vector3(0, -300, 2500), "cat");
        await Seconds(1);
        Check($"a notorious pilot it can tell is attacked on sight ({Last(c2)})",
              _security.StandingOf(c2) == Standing.Hostile && Last(c2).Contains("not welcome"));
        Radio(c2).AskDocking();
        await Seconds(1);
        Check($"and refused docking ({Last(c2)})", Radio(c2).Dock == 0 && Last(c2).Contains("denied"));

        ShipBody e = Ship("Echo", new Vector3(-300, -300, 2500), "cat");
        ShipBody f = Ship("Foxtrot", new Vector3(-600, -300, 2500), "fay");
        Radio(e).Powered = false;
        await Seconds(1);
        Check("with its radio off it can't be told: not attacked for who it is", _security.StandingOf(e) != Standing.Hostile);
        Strike(f, e, 10);
        await Seconds(0.2);
        Check($"what it does costs its pilot nothing (bounty still {Of("cat").Bounty}), though it is engaged",
              Of("cat").Bounty == _security.KillBounty && _security.StandingOf(e) == Standing.Hostile);

        // Slipping in: the notorious pilot's ship, radio off, in a hangar here (a door left open for another): pays at face value
        Universe.Current.Fly(c, null);          // a pilot flies one ship
        Universe.Current.Fly(c2, null);
        HangarDock hangarHere = _station.Docks[0];
        e.Position = hangarHere.InsidePoint;
        state.Pilot("cat").Credits = 20_000;
        await Seconds(0.5);
        StationMarket market = _station.GetParent().GetNode<StationMarket>("Market");
        long due = Of("cat").Owed;
        MarketView paid = market.Serve("cat", new MarketRequest { Station = _station.Sign, Do = MarketRequest.Pay });
        Check($"slipped in with its radio off, the notorious pilot pays at face value ({paid.Message})",
              Of("cat").Owed == 0 && state.Pilot("cat").Credits == 20_000 - due);
        Check("paying takes no infamy away", Justice.IsNotorious(Of("cat"), state.Pilot("cat")));

        state.Pilot("cat").PlayedSeconds += 3 * 3600;
        state.Clock += 3 * 3600;
        ShipBody c3 = Ship("Charlie3", new Vector3(300, -300, 2500), "cat");
        await Seconds(1);
        Radio(c3).AskDocking();
        await Seconds(1);
        Check($"three hours of play later the infamy has worn off: cleared to dock ({Last(c3)})",
              !Justice.IsNotorious(Of("cat"), state.Pilot("cat")) && Radio(c3).Dock != 0);

        // A disreputable factor at another station settles Bravo's bounty here, dearer than paying here
        var other = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        other.Name = "Kestrel";
        other.Position = new Vector3(30000, 0, 0);
        AddChild(other);
        StationMarket factor = other.GetNode<StationMarket>("Market");
        factor.Factor = true;
        HangarDock hangar = other.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        b.Position = hangar.InsidePoint;
        state.Pilot("bob").Credits = 10_000;
        await Seconds(0.5);
        long owed = Of("bob").Owed;
        MarketView view = factor.Serve("bob", new MarketRequest { Station = factor.Radio.Sign, Do = MarketRequest.Look });
        DebtLine debt = view.Elsewhere.FirstOrDefault(l => l.Station == _station.Sign);
        Check($"a factor shows what is owed elsewhere ({debt?.Owed}), and asks half as much again ({debt?.Cost})",
              debt != null && debt.Owed == owed && debt.Cost == owed * 3 / 2);
        view = factor.Serve("bob", new MarketRequest { Station = factor.Radio.Sign, Do = MarketRequest.Factor, Good = _station.Sign });
        Check($"and settles it ({view.Message})", Of("bob").Owed == 0 && state.Pilot("bob").Credits == 10_000 - owed * 3 / 2);
        StationMarket plain = _station.GetParent().GetNode<StationMarket>("Market");
        Check("a station with no factor settles nothing of others'",
              plain.Serve("bob", new MarketRequest { Station = _station.Sign, Do = MarketRequest.Factor, Good = factor.Radio.Sign }).Message == plain.NoFactorText);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Rules()
    {
        var pilot = new PilotRecord { Id = "p", Credits = 100 };
        var record = new CrimeRecord();
        Justice.AddInfamy(record, pilot, 2);
        Check("infamy 2: notorious", Justice.IsNotorious(record, pilot));
        pilot.PlayedSeconds += 3600;
        Check($"an hour of play takes off {Justice.DecayPerHour} ({Justice.Infamy(record, pilot):0.00})",
              Math.Abs(Justice.Infamy(record, pilot) - (2 - Justice.DecayPerHour)) < 1e-9);
        Justice.AddInfamy(record, pilot, 1);
        Check($"more infamy adds to what is left ({Justice.Infamy(record, pilot):0.00})", Math.Abs(Justice.Infamy(record, pilot) - 2.5) < 1e-9);
        pilot.PlayedSeconds += 100 * 3600;
        Check("and it never goes below none", Justice.Infamy(record, pilot) == 0 && !Justice.IsNotorious(record, pilot));

        record.Fines = 50;
        record.Bounty = 500;
        Check("paying needs all that is owed", !Justice.Pay(record, pilot) && pilot.Credits == 100 && record.Owed == 550);

        var station = new StationRecord();
        pilot.Ship.Load("food", 2);
        bool impounded = Justice.Surrender(station, record, pilot, ship => { long v = ship.Holds("food") * 100; ship.Cargo.Clear(); return v; });
        Check($"surrendering: the cargo seized and sold, then the ship impounded for the rest ({record.Owed} owed)",
              impounded && record.Owed == 250 && pilot.Credits == 0 && station.Impounded.ContainsKey("p") &&
              station.Impounded["p"].Cargo.Count == 0 && pilot.Ship.Hull == ShipRecord.CourtesyHull);
        Check("nothing is released while anything is owed", !Justice.Release(station, record, pilot));
        pilot.Credits = 300;
        pilot.Ship.Load("water", 1);
        Check("paid, the ship is released, with what the courtesy ship carried",
              Justice.Pay(record, pilot) && Justice.Release(station, record, pilot) && pilot.Ship.Hull == ShipRecord.StarterHull &&
              pilot.Ship.Holds("water") == 1 && station.Impounded.Count == 0 && pilot.Credits == 50);

        var rich = new PilotRecord { Id = "r", Credits = 100 };
        rich.Ship.Load("food", 9);
        var small = new CrimeRecord { Bounty = 600 };
        Check("cargo enough to pay: settled, no ship impounded",
              !Justice.Surrender(station, small, rich, ship => { ship.Cargo.Clear(); return 900; }) && small.Owed == 0 && rich.Credits == 400);
    }

    private ShipBody Ship(string name, Vector3 at, string pilot)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio" });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        AddChild(ship);
        Universe.Current.Fly(ship, pilot);
        return ship;
    }

    private static RadioDevice Radio(ShipBody ship) => ship.GetNode<RadioDevice>("Radio");

    private static string Last(ShipBody ship) => Radio(ship).Heard.Count > 0 ? Radio(ship).Heard[^1].Text : "";

    private void Fire(ShipBody ship) => Combat.Fire(ship, ship.GlobalPosition);

    private static void Strike(ShipBody victim, ShipBody by, float damage) =>
        Combat.Strike(victim.GetNode<ShipSignature>("Signature"), damage, by, victim.GlobalPosition);

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
