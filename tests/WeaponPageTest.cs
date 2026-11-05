using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// FIRECTL's page on the WEAPON screen: the name of the weapon fills at least
/// a third of the screen and changes with it; the slot under it shows the
/// missiles left, NO AMMO in the screen's red with none, the cannon's rounds
/// with a short bar of its heat beside them, or the laser's heat, red when
/// too hot; the distance appears with a lock. The cannon fires only the
/// rounds its magazine holds.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/weapon_page_test.tscn
/// XPACE_PAGE_SHOTS=dir writes the pictures. Exits with 0 if every check passed.
/// </summary>
public partial class WeaponPageTest : Node3D
{
    private const int DistTop = 117, DistBottom = 140, SlotTop = 76, SlotBottom = 99;
    private ShipBody _ship;
    private RadarDevice _radar;
    private LaserDevice _laser;
    private LauncherDevice _launcher;
    private TurretDevice _turret;
    private const int Rounds = 5;
    private DisplayDevice _display;
    private Drone _drone;
    private PilotControls _controls;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        var computer = new ShipComputer { Name = "ShipComputer" };
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("XPACE_PAGE_SHOTS")))
            computer.Watch();                                   // pictures are only drawn for someone watching
        _ship.AddChild(computer);
        _display = new DisplayDevice
        {
            Screens = new()
            {
                GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
                GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
                GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
            },
        };
        _ship.AddChild(_display);
        _turret = new TurretDevice
        {
            Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f),
            Spec = new TurretSpec { Ammunition = new AmmoSpec(), Rounds = Rounds },
        };
        _ship.AddChild(_turret);
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _launcher = new LauncherDevice
        {
            Name = "Launcher", Position = new Vector3(0, -1f, -2f), Missiles = 1, HeatSeekers = 2,
            HeatSeekerScene = GD.Load<PackedScene>("res://scenes/weapons/ir_missile.tscn"),
        };
        _ship.AddChild(_launcher);
        _laser = new LaserDevice { Name = "Laser", Position = new Vector3(0, -0.8f, -5.5f) };
        _laser.AddChild(new Marker3D { Name = "Left", Position = new Vector3(-1, 0, 0) });
        _laser.AddChild(new Marker3D { Name = "Right", Position = new Vector3(1, 0, 0) });
        _ship.AddChild(_laser);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _drone = new Drone { Straight = true, Evades = false, Defends = false, Speed = 0, Health = 1e9f, Position = new Vector3(0, 0, -2500) };
        AddChild(_drone);

        Check("the ship's software is running", await Until(() => FireControl.Running(_ship), 40));
        Check("the page is up", await Until(() => FireControl.PageDrawn(_display), 2));

        int rows = 0;
        for (int y = 0; y < 160; y++)
            if (FireControl.Count(_display, 2, y, y) > 0 && y < 60)
                rows++;
        Check($"the name fills at least a third of the screen ({rows} rows of 160)", rows * 3 >= 160);
        await Seconds(0.2);
        Check("no target: no number in the distance's place", FireControl.Count(_display, 2, DistTop, DistBottom) == 0);
        Check("and a row of dashes", FireControl.Count(_display, 1, DistTop, DistBottom) > 0);
        Shot("gun_no_target");

        await Seconds(0.6);
        await Press(() => _controls.TargetAhead = true, () => _controls.TargetAhead = false);
        Check("T locks the drone", await Until(() => _radar.Locked == _drone, 1));
        Check("the distance appears", await Until(() => FireControl.Count(_display, 2, DistTop, DistBottom) > 50, 1));
        await Seconds(0.3);
        Check("all of it in its place, nothing under the rule", FireControl.Count(_display, 2, DistBottom + 1, 144) == 0);
        Shot("gun");

        string gun = Name();
        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon: MSL", FireControl.Weapon(_ship) == "MSL");
        await Seconds(0.3);
        Check("its name is another picture", Name() != gun);
        Check("one missile: a number in the slot", FireControl.Count(_display, 2, SlotTop, SlotBottom) > 30);
        Check("and nothing in red", FireControl.Count(_display, 3, SlotTop, SlotBottom) == 0);
        Check($"it is ready ({FireControl.Status(_ship)})", await Until(() => FireControl.Status(_ship) == "READY", 2));
        Shot("msl");

        await Press(() => _controls.Trigger = true, () => _controls.Trigger = false);
        Check("the last missile goes", await Until(() => _launcher.MissilesLeft == 0, 1));
        Check("NO AMMO in red", await Until(() => FireControl.Count(_display, 3, SlotTop, SlotBottom) > 200, 1));
        Shot("msl_none");
        Check($"the word at the bottom follows it ({FireControl.Status(_ship)})", FireControl.Status(_ship) is "LAUNCH" or "GUIDING");

        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon: IR", FireControl.Weapon(_ship) == "IR");
        await Seconds(0.3);
        Check("heat seekers left: no red", FireControl.Count(_display, 3, SlotTop, SlotBottom) == 0);
        Check($"its head is off ({FireControl.Status(_ship)})", await Until(() => FireControl.Status(_ship) == "SEEKER OFF", 1));
        await Seconds(0.3);
        Shot("ir");

        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon: LSR", FireControl.Weapon(_ship) == "LSR");
        await Seconds(0.3);
        Check("its heat bar is empty", FireControl.Count(_display, 2, SlotTop + 2, SlotBottom - 2) == 0);
        _controls.Trigger = true;
        await Seconds(1.5);
        Check("firing, it fills", FireControl.Count(_display, 2, SlotTop + 2, SlotBottom - 2) > 100);
        Shot("lsr");
        Check("held down it runs too hot", await Until(() => _laser.TooHot, 15));
        Check("the bar goes red", await Until(() => FireControl.Count(_display, 3, SlotTop + 2, SlotBottom - 2) > 500, 1));
        Check($"and the word says so ({FireControl.Status(_ship)})", FireControl.Status(_ship) == "TOO HOT");
        Shot("lsr_hot");
        _controls.Trigger = false;
        GD.Print($"     the processor: {FireControl.Load(_ship)}% while it draws");

        await Press(() => _controls.NextWeapon = true, () => _controls.NextWeapon = false);
        Check("next weapon goes back to the gun", FireControl.Weapon(_ship) == "GUN");
        await Seconds(0.3);
        Check("with the name it had", Name() == gun);
        Check("its rounds in the slot", FireControl.Count(_display, 2, SlotTop, SlotBottom) > 30);
        Check("with the frame of its heat beside them", FireControl.Count(_display, 1, SlotTop, SlotBottom) > 40);
        Check("and nothing in red", FireControl.Count(_display, 3, SlotTop, SlotBottom) == 0);
        Shot("gun_rounds");

        _controls.Trigger = true;
        Check($"held down on the target, it fires all the magazine holds ({_turret.RoundsFired})", await Until(() => _turret.RoundsFired == Rounds, 5));
        await Seconds(1);
        _controls.Trigger = false;
        Check($"and no more ({_turret.RoundsFired}, {_turret.RoundsLeft} left)", _turret.RoundsFired == Rounds && _turret.RoundsLeft == 0);
        Check($"the turret says so: not ready, no rounds (status {_ship.Computer.Bus[TurretDevice.Slot, 13]})",
              (_ship.Computer.Bus[TurretDevice.Slot, 13] & 9) == 8);
        Check($"its magazine in memory: 0 of {Rounds}", Magazine(0) == 0 && Magazine(2) == Rounds);
        Check("NO AMMO in red", await Until(() => FireControl.Count(_display, 3, SlotTop, SlotBottom) > 200, 1));
        Shot("gun_none");

        _turret.Rearm();
        bool clean = await Until(() => FireControl.Count(_display, 3, SlotTop, SlotBottom) == 0, 1);
        await Seconds(0.2);
        Check($"a full magazine again: its rounds, and no red (red {FireControl.Count(_display, 3, SlotTop, SlotBottom)}, bright {FireControl.Count(_display, 2, SlotTop, SlotBottom)}, dim {FireControl.Count(_display, 1, SlotTop, SlotBottom)})",
              clean && FireControl.Count(_display, 2, SlotTop, SlotBottom) > 30 && FireControl.Count(_display, 1, SlotTop, SlotBottom) > 40);
        Shot("gun_rearmed");

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private unsafe int Magazine(int offset)
    {
        byte* at = _ship.Computer.Memory + ShipMemory.Base + ShipMemory.Turret + offset;
        return at[0] | at[1] << 8;
    }

    /// <summary>The pixels of the weapon's name, as a string to compare.</summary>
    private string Name()
    {
        var text = new System.Text.StringBuilder();
        for (int y = 1; y < 59; y += 4)
            for (int x = 0; x < 112; x++)
                text.Append(_display.Pixel("WEAPON", x, y));
        return text.ToString();
    }

    private void Shot(string name)
    {
        string dir = System.Environment.GetEnvironmentVariable("XPACE_PAGE_SHOTS");
        if (!string.IsNullOrEmpty(dir))
            _display.Picture("WEAPON")?.SavePng($"{dir}/{name}.png");
    }

    private async Task Press(Action down, Action up)
    {
        down();
        await Seconds(0.1);
        up();
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
