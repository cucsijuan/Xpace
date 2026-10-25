using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The power distributor, the shields and the ship's heat, on a ship whose
/// computer runs COS and POWER.COM:
///
///   - the pilot's buttons move the pips as in Elite (a pip from the others,
///     at most four), and even them out;
///   - the shield takes the hits first, collapses when knocked down, starts
///     again from nothing, and builds back as fast as its share of the
///     power lets it;
///   - with no power to the engines the thrusters hardly push;
///   - the heat settles where the radiators give off what comes in, rises
///     with the thrusters, cooks the hull (past the shield) over 100%; a heat
///     sink takes it down, and the hull's glow with it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/power_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PowerTest : Node3D
{
    private ShipBody _ship;
    private PowerDevice _power;
    private ShieldDevice _shield;
    private HeatDevice _heat;
    private ShipHull _hull;
    private ShipSignature _signature;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _ship.AddChild(new DisplayDevice { Screens = new Godot.Collections.Array<DisplayScreen> { GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres") } });
        _power = new PowerDevice { Name = "Power", SystemsMegajoules = 2 };
        _shield = new ShieldDevice { Name = "Shield", Strength = 150, RechargePerSecond = 60, CollapseSeconds = 1 };
        _heat = new HeatDevice { Name = "Heat" };
        _hull = new ShipHull { Name = "Hull", Insured = false };
        _signature = new ShipSignature { Name = "Signature" };
        foreach (Node n in new Node[] { _power, _shield, _heat, _hull, _signature })
            _ship.AddChild(n);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);

        Check("POWER.COM is resident", await Until(() => _ship.Computer.ScreenText().Contains("Power manager resident"), 40));
        await Pips();
        await Shields();
        await Engines();
        await HeatAndSinks();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Pips()
    {
        string Setting() => $"{_power.Pips[0]}/{_power.Pips[1]}/{_power.Pips[2]}";
        Check($"it starts even ({Setting()})", Setting() == "4/4/4");
        await Press(c => c.PipSystems = true);
        Check($"a pip to systems: half from each of the others ({Setting()})", Setting() == "6/3/3");
        await Press(c => c.PipSystems = true);
        await Press(c => c.PipSystems = true);
        Check($"at most four pips ({Setting()})", Setting() == "8/2/2");
        await Press(c => c.PipEngines = true);
        Check($"a pip to engines from systems, which has more ({Setting()})", Setting() == "6/4/2");
        await Press(c => c.PipReset = true);
        Check($"evened out ({Setting()})", Setting() == "4/4/4");
        await Press(c => c.PipWeapons = true);
        await Press(c => c.PipWeapons = true);
        Check($"two pips to weapons ({Setting()})", Setting() == "2/2/8");
        await Press(c => c.PipReset = true);
    }

    private async Task Shields()
    {
        _shield.Rearm();
        float hull = _hull.Left;
        _signature.Hit(100);
        Check($"the shield takes a hit (shield {_shield.Points:0}, hull {_hull.Left:0})", _shield.Points == 50 && _hull.Left == hull);
        _signature.Hit(100);
        Check($"knocked down it collapses, and what got past hits the hull ({_hull.Left:0})", _shield.Collapsed && _hull.Left == hull - 50);
        _signature.Hit(10);
        Check("collapsed, everything gets through", _hull.Left == hull - 60);
        await Seconds(0.5);
        Check($"still down half a second later ({_shield.Points:0})", _shield.Points == 0);
        await Seconds(1);
        Check($"it starts again from nothing ({_shield.Points:0})", _shield.Points > 0 && _shield.Points < 40);

        // How fast it builds back, with two and with eight half pips to systems (its capacitor dry)
        float Rate(float before, float after) => (after - before) / 1.5f;
        _power.Set(2, 6, 4);
        _power.Charge[(int)PowerStore.Systems] = 0;
        _shield.Absorb(_shield.Points);
        await Seconds(_shield.CollapseSeconds + 0.1);
        float a = _shield.Points;
        await Seconds(1.5);
        float low = Rate(a, _shield.Points);
        _power.Set(8, 2, 2);
        _power.Charge[(int)PowerStore.Systems] = 0;
        _shield.Absorb(_shield.Points);
        await Seconds(_shield.CollapseSeconds + 0.1);
        a = _shield.Points;
        await Seconds(1.5);
        float high = Rate(a, _shield.Points);
        Check($"it builds back as fast as the power lets it: {low:0.0} points a second on one pip, {high:0.0} on four",
              high > low * 3 && high < low * 5);
        _power.Set(4, 4, 4);
    }

    private async Task Engines()
    {
        async Task<float> Speed()
        {
            _ship.State = new ShipState { Orientation = Quaternion.Identity };
            _controls = new PilotControls { Throttle = 1 };
            await Seconds(1);
            _controls = default;
            float v = _ship.LinearVelocity.Length();
            await Seconds(0.1);
            return v;
        }
        float even = await Speed();
        _power.Set(8, 0, 4);
        _power.Charge[(int)PowerStore.Engines] = 0;
        float none = await Speed();
        _power.Set(4, 4, 4);
        Check($"full throttle for a second: {even:0} m/s with the engines' share, {none:0} with none", even > 30 && none < even * 0.05f);
        _ship.State = new ShipState { Orientation = Quaternion.Identity };
    }

    private async Task HeatAndSinks()
    {
        await Seconds(30);
        float idle = _heat.Level;
        Check($"idle, the heat settles at {idle * 100:0}%", idle > 0.15f && idle < 0.25f);
        for (int i = 0; i < 10; i++)              // full ahead, full astern: the thrusters always pushing
        {
            _controls = new PilotControls { Throttle = i % 2 == 0 ? 1 : -1 };
            await Seconds(2);
        }
        _controls = default;
        float pushing = _heat.Level;
        Check($"with the thrusters pushing hard it rises to {pushing * 100:0}%", pushing > idle + 0.1f);

        _shield.Rearm();
        float hull = _hull.Left;
        _heat.Add(_heat.Megajoules * 1.2);
        await Seconds(3);
        Check($"overheated ({_heat.Level * 100:0}%) the hull cooks: {hull - _hull.Left:0.0} points, the shield untouched ({_shield.Points:0})",
              _hull.Left < hull && _shield.Points == _shield.Strength);

        float glow = HullGlow();
        await Press(c => c.HeatSink = true);
        Check($"the heat sink button throws one ({_heat.SinksLeft} left)", _heat.Sinking && _heat.SinksLeft == _heat.HeatSinks - 1);
        await Seconds(4);
        Check($"it takes the heat down to {_heat.Level * 100:0}%, and the hull's glow from {glow:0.00} to {HullGlow():0.00}",
              _heat.Level < 0.05f && HullGlow() < glow * 0.1f);
    }

    /// <summary>What a seeker 1 km ahead sees of the hull (the motor off).</summary>
    private float HullGlow() => _signature.HeatAt(_ship.GlobalPosition + new Vector3(0, 0, -1000), out _) * 1e6f;

    private async Task Press(Action<PilotControlsBox> press)
    {
        var box = new PilotControlsBox();
        press(box);
        _controls = box.Controls;
        await Seconds(0.1);
        _controls = default;
        await Seconds(0.1);
    }

    /// <summary>A way to set one button in a lambda (PilotControls is a struct).</summary>
    private sealed class PilotControlsBox
    {
        public PilotControls Controls;
        public bool PipSystems { set => Controls.PipSystems = value; }
        public bool PipEngines { set => Controls.PipEngines = value; }
        public bool PipWeapons { set => Controls.PipWeapons = value; }
        public bool PipReset { set => Controls.PipReset = value; }
        public bool HeatSink { set => Controls.HeatSink = value; }
    }

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
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
