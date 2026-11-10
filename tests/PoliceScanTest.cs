using System.Collections.Generic;
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
/// A station's police reading the holds of ships in its zone (ships here are hulls with a radio; their computers
/// stay off; the police are the real police fighter):
///
///   - a ship in the zone a while is looked over: a police ship launches, locks it and is told it is being scanned;
///   - stolen goods found aboard a pilot's ship: a fine for each tonne, owed to the station and told by radio;
///   - a clean hold: read, and nothing said; a ship read is left alone afterwards;
///   - stolen goods aboard a ship with its radio off: it cannot be told, so it is hostile;
///   - a ship that leaves the zone while it is being read: no reading, and the police stop looking for it;
///   - a crew's ship held on its way, as cargo ships are, is read as any ship: its loot puts a price on the person's
///     head with the station's owner.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/police_scan_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PoliceScanTest : Node3D
{
    private StationRadio _station;
    private StationSecurity _security;
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _security = station.GetNode<StationSecurity>("Security");
        _security.StandDownSeconds = 5;
        _security.PoliceCount = 1;
        _security.ScanAfterSeconds = 2;
        _station.OwnedBy = GD.Load<Organisation>("res://assets/organisations/miltec.tres");

        // A smuggler with its radio on: fined for each stolen tonne
        ShipBody sierra = Ship("Sierra", new Vector3(0, 0, 2500), "sierra", clean: 2, stolen: 3);
        ulong start = Engine.GetPhysicsFrames();
        Check("a ship in the zone a while is one to look over", await Until(() => _security.Suspects.Contains(sierra), 5));
        Check("a police ship launches to look at it", await Until(() => Police() != null, 10));
        Check($"locked, and told it is being scanned ({Last(sierra)})", await Until(() => Last(sierra).Contains("Cargo scan"), 90));
        double told = (Engine.GetPhysicsFrames() - start) / 60.0;
        Check("read", await Until(() => _security.ScannedAt(sierra) != null, 60));
        GD.Print($"     in the zone to scan told {told:0} s, to read {(Engine.GetPhysicsFrames() - start) / 60.0:0} s (game seconds)");
        long owed = Universe.Current.State.Station(_station.Id).CrimesOf("sierra").Fines;
        Check($"stolen goods aboard: a fine for each tonne ({owed}; {Last(sierra)})",
              owed == 3 * _security.StolenFinePerTonne && Last(sierra).Contains("Stolen goods aboard, 3 t"));
        await Seconds(3);
        Check("read once, it is left alone", !_security.Suspects.Contains(sierra));
        Check("the police go home", await Until(() => Police() == null, 90));

        // A clean hold: nothing said
        ShipBody tango = Ship("Tango", new Vector3(300, 0, 2500), "tango", clean: 4, stolen: 0);
        Check("a clean ship is read too", await Until(() => _security.ScannedAt(tango) != null, 150));
        Check($"and nothing held against it ({Last(tango)})",
              !Last(tango).Contains("Stolen") && Universe.Current.State.Station(_station.Id).CrimesOf("tango").Fines == 0);
        Check("the police go home", await Until(() => Police() == null, 90));

        // Its radio off: it can't be told, so carrying stolen goods it is hostile
        ShipBody uniform = Ship("Uniform", new Vector3(-300, 0, 2500), "uniform", clean: 0, stolen: 2);
        Radio(uniform).Powered = false;
        Check("a ship with its radio off is read", await Until(() => _security.ScannedAt(uniform) != null, 150));
        Check("carrying stolen goods, unidentified: hostile", _security.StandingOf(uniform) == Standing.Hostile);
        Check("no fine on anyone's record", Universe.Current.State.Station(_station.Id).CrimesOf("uniform").Fines == 0);
        uniform.QueueFree();
        Check("the police go home", await Until(() => Police() == null, 120));

        // Away from the police while being read: no reading
        ShipBody victor = Ship("Victor", new Vector3(0, 300, 2500), "victor", clean: 0, stolen: 4);
        Check("the next ship's scan starts", await Until(() => Last(victor).Contains("Cargo scan"), 150));
        victor.GlobalPosition = _station.GlobalPosition + new Vector3(0, 0, _station.Laws.ZoneRadius + 2000);
        await Seconds(2);
        Check("out of the zone it is no longer looked for", !_security.Suspects.Contains(victor));
        await Seconds(20);
        Check("never read, never fined",
              _security.ScannedAt(victor) == null && Universe.Current.State.Station(_station.Id).CrimesOf("victor").Fines == 0);

        // A crew on its way, held to it as cargo ships are: read as any ship, its loot a price on the person's head
        Check("the police go home", await Until(() => Police() == null, 120));
        ShipBody whiskey = Ship("Whiskey", new Vector3(-2000, 0, 3000), null, 0, 0);
        whiskey.Actor = "person-7";
        whiskey.RecordId = "cargo-7";
        Universe.Current.State.CargoShips["cargo-7"] = new CargoShipRecord
        {
            Id = "cargo-7", Owner = "person-7", Good = "food", Tonnes = 2, Loot = { ["iron"] = 3 },
        };
        whiskey.Holder = new OnItsWay();
        Check("a ship on its way is looked over too", await Until(() => _security.Suspects.Contains(whiskey), 5));
        Check("and read, moving", await Until(() => _security.ScannedAt(whiskey) != null, 150) && whiskey.Holder is OnItsWay);
        var owner = (OrganisationRecord)_station.Cash;
        Check($"its loot puts a price on the person's head with the station's owner ({owner.Bounties.GetValueOrDefault("person-7")}; {Last(whiskey)})",
              owner.Bounties.GetValueOrDefault("person-7") == 3 * _security.StolenFinePerTonne && Last(whiskey).Contains("Stolen goods aboard, 3 t"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private ShipBody Ship(string name, Vector3 at, string pilot, int clean, int stolen)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio" });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        AddChild(ship);
        if (pilot == null)
            return ship;
        Universe.Current.Fly(ship, pilot);
        PilotRecord record = Universe.Current.State.Pilot(pilot);
        record.Ship.Empty();
        record.Ship.Load("food", clean);
        record.Ship.Steal("food", stolen);
        return ship;
    }

    // Along its way at 20 m/s, as a crew's way holds a cargo ship
    private sealed class OnItsWay : IShipHold
    {
        public ShipState Hold(ShipBody ship, double delta)
        {
            ShipState s = ship.State;
            s.Velocity = new Vector3(20, 0, 0);
            s.AngularVelocity = Vector3.Zero;
            s.Position += s.Velocity * (float)delta;
            return s;
        }
    }

    private static RadioDevice Radio(ShipBody ship) => ship.Find<RadioDevice>();

    private static string Last(ShipBody ship) => Radio(ship).Heard.Count > 0 ? Radio(ship).Heard[^1].Text : "";

    private Node3D Police() => GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Name.ToString().StartsWith("Police") && !n.IsQueuedForDeletion());

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
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
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
