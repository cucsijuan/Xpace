using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The Synty station's security, judging only what it sees ships do in its
/// zone (ships here are hulls with a radio; their computers stay off):
///
///   - a round fired in the zone: a warning by radio; firing again within
///     the grace, still only warned; later, hostile (and told);
///   - the police launch from a hangar (its door opens) and go after the
///     hostile ship;
///   - fired upon, a ship fires back freely; destroying a hostile ship is no
///     crime, and the destroyed ship starts clean;
///   - destroying a ship the security had nothing against: hostile at once,
///     and told why; docking is refused to a hostile ship;
///   - too fast near the station: a fine (counted), told by radio; outside
///     the speed limit's radius, no fine;
///   - with no hostile ship left the police fly back into their hangar.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/security_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SecurityTest : Node3D
{
    private StationRadio _station;
    private StationSecurity _security;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _security = station.GetNode<StationSecurity>("Security");
        _security.StandDownSeconds = 5;
        _security.PoliceCount = 1;

        ShipBody a = Ship("Alpha", new Vector3(0, 0, 2500));
        ShipBody b = Ship("Bravo", new Vector3(300, 0, 2500));
        ShipBody c = Ship("Charlie", new Vector3(-300, 0, 2500));
        ShipBody d = Ship("Delta", new Vector3(0, 300, 2500));
        ShipBody far = Ship("Echo", new Vector3(0, 0, 9000));
        await Seconds(1);

        Fire(a);
        await Seconds(0.2);
        Check($"a round fired in the zone: a warning ({Last(a)})", _security.StandingOf(a) == Standing.Warned && Last(a).Contains("Cease fire"));
        Check("the others hear nothing of it", Radio(b).Heard.All(m => !m.Text.Contains("Cease")));
        Check($"spoken in the station operator's voice ({Radio(a).Heard[^1].Speech})",
              _station.Operator != null && Radio(a).Heard[^1].Speech == $"{_station.Operator.Name}:warning");
        Fire(far);
        await Seconds(0.2);
        Check("fired outside the zone: nothing", _security.StandingOf(far) == Standing.Clean);
        await Seconds(1);
        Fire(a);
        await Seconds(0.2);
        Check("again within the grace: still warned", _security.StandingOf(a) == Standing.Warned);
        await Seconds(4);
        Fire(a);
        await Seconds(0.2);
        Check($"later: hostile, and told ({Last(a)})", _security.StandingOf(a) == Standing.Hostile && Last(a).Contains("Police are engaging"));

        Check("a police ship launches", await Until(() => Police() != null, 10));
        Node3D police = Police();
        HangarDock from = _station.Docks.OrderBy(k => k.InsidePoint.DistanceTo(police.GlobalPosition)).First();
        Check($"out of hangar {from.Number}, its door opening", from.Door.Open == 1);
        var pilot = police.GetNode<FighterAi>("Pilot");
        Check("it flies out and goes after the hostile ship",
              await Until(() => pilot.Doing is "intercepting" or "attacking" && !from.Holds(police), 90));
        GD.Print($"     police: {pilot.Doing}, {police.GlobalPosition.DistanceTo(a.GlobalPosition):0} m from it");

        Strike(c, b, 10);                       // Bravo hits Charlie: Charlie may answer
        Fire(c);
        await Seconds(0.2);
        Check("fired upon, a ship fires back freely", _security.StandingOf(c) == Standing.Clean);

        Strike(a, b, 10000);                    // Bravo destroys the hostile Alpha
        await Seconds(0.2);
        Check("destroying a hostile ship is no crime; the wreck starts clean",
              a.Wrecked && _security.StandingOf(b) == Standing.Clean && _security.StandingOf(a) == Standing.Clean);

        Strike(d, c, 10000);                    // Charlie destroys Delta, who did nothing
        await Seconds(0.2);
        Check($"destroying a clean ship: hostile at once, told why ({Last(c)})",
              _security.StandingOf(c) == Standing.Hostile && Last(c).Contains("You destroyed DELTA"));
        Radio(c).AskDocking();
        await Seconds(1);
        Check($"a hostile ship is refused a dock ({Last(c)})", Radio(c).Dock == 0 && Last(c).Contains("denied"));
        c.GetNode<ShipHull>("Hull").Destroy();  // (the police would see to it; here it ends)
        await Seconds(0.2);

        ShipBody fast = Ship("Foxtrot", _station.GlobalPosition + new Vector3(0, 0, 1000));
        await Seconds(1);
        int heard = Radio(fast).Heard.Count;
        for (int i = 0; i < 4 * 60; i++)
        {
            fast.LinearVelocity = new Vector3(80, 0, 0);
            await Seconds(1.0 / 60);
        }
        Check($"too fast near the station: fined ({Last(fast)})",
              _security.Fines.TryGetValue(fast, out int fines) && fines == 1 && Radio(fast).Heard.Count > heard && Last(fast).Contains("fined"));
        for (int i = 0; i < 4 * 60; i++)
        {
            b.LinearVelocity = new Vector3(80, 0, 0);
            await Seconds(1.0 / 60);
        }
        Check("as fast outside the limit's radius: no fine", !_security.Fines.ContainsKey(b));
        b.LinearVelocity = Vector3.Zero;

        Check("nothing hostile left: the police fly back into their hangar", await Until(() => Police() == null, 240));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private ShipBody Ship(string name, Vector3 at)
    {
        var ship = new ShipBody { Name = name, Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = at };
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        ship.AddChild(new RadioDevice { Name = "Radio" });
        ship.AddChild(new ShipHull { Name = "Hull", Insured = false });
        ship.AddChild(new ShipSignature { Name = "Signature" });
        AddChild(ship);
        return ship;
    }

    private static RadioDevice Radio(ShipBody ship) => ship.GetNode<RadioDevice>("Radio");

    private static string Last(ShipBody ship) => Radio(ship).Heard.Count > 0 ? Radio(ship).Heard[^1].Text : "";

    private static void Fire(ShipBody ship) =>
        Projectiles.Instance.Fire(ship.GlobalPosition + new Vector3(0, 10, 0), new Vector3(0, 1200, 0), 10, ship);

    private static void Strike(ShipBody victim, ShipBody by, float damage) =>
        Combat.Strike(victim.GetNode<ShipSignature>("Signature"), damage, by, victim.GlobalPosition);

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
