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
/// A refit while FIRECTL runs: it looks at what the ship carries once a
/// second, so a weapon taken off is no longer chosen (the next one the ship
/// has is), the weapon button skips it, and a weapon put back can be chosen
/// again, all without installing the program again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/refit_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RefitTest : Node
{
    private const string Laser = "res://assets/weapons/lasers/lsr_1.tres", Gun = "res://assets/weapons/turrets/gun_1.tres";
    private ShipBody _ship;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = GD.Load<PackedScene>("res://scenes/ships/player_ship.tscn").Instantiate<ShipBody>();
        AddChild(_ship);
        _ship.ControlsSource = () => _controls;

        Check("FIRECTL is running", await Until(() => FireControl.Running(_ship), 60));
        Check($"as it came, the cannon is chosen ({FireControl.Weapon(_ship)})", FireControl.Weapon(_ship) == "GUN");

        Fit(("Spine", Laser));
        bool left = await Until(() => FireControl.Weapon(_ship) != "GUN", 1.5);
        Check($"the turret taken off: within a second it chooses another weapon ({FireControl.Weapon(_ship)})", left);
        HashSet<string> seen = await Cycle(8);
        Check($"and the weapon button skips the cannon ({string.Join(" ", seen)})", !seen.Contains("GUN") && seen.Contains("LSR"));

        Fit();
        await Seconds(1.2);
        seen = await Cycle(8);
        Check($"the turret back: the cannon can be chosen again ({string.Join(" ", seen)})", seen.Contains("GUN"));

        while (FireControl.Weapon(_ship) != "LSR")
            await Press();
        Fit(("NoseLeft", ""), ("NoseRight", ""));
        left = await Until(() => FireControl.Weapon(_ship) != "LSR", 1.5);
        Check($"the lasers taken off: it leaves the laser ({FireControl.Weapon(_ship)})", left);
        seen = await Cycle(8);
        Check($"and the button skips it ({string.Join(" ", seen)})", !seen.Contains("LSR") && seen.Contains("GUN"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Fit(params (string hardpoint, string model)[] changes)
    {
        var record = new ShipRecord { Hull = _ship.Spec.ResourcePath };
        foreach ((string hardpoint, string model) in changes)
            record.Fittings[hardpoint] = model;
        _ship.Fit(Fittings.Of(_ship.Spec, record));
    }

    private async Task<HashSet<string>> Cycle(int presses)
    {
        var seen = new HashSet<string> { FireControl.Weapon(_ship) };
        for (int i = 0; i < presses; i++)
        {
            await Press();
            seen.Add(FireControl.Weapon(_ship));
        }
        return seen;
    }

    private async Task Press()
    {
        _controls.NextWeapon = true;
        await Seconds(0.1);
        _controls.NextWeapon = false;
        await Seconds(0.1);
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
