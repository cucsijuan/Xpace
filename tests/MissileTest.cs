using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Missiles guided over the datalink: FIRECTL switches to them, fires one
/// at the locked drone, and the missile's firmware steers it there on the
/// radar's track. By range, against a straight and a jinking drone, then
/// against one that defends itself: a hard break when its warner sees the
/// missile, and chaff that can pull the radar's track away.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/missile_test.tscn
/// Prints the table; exits with 0 if the checks passed.
/// </summary>
public partial class MissileTest : Node3D
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private LauncherDevice _launcher;
    private DisplayDevice _display;
    private Drone _drone;
    private PilotControls _controls;
    private int _failures;
    private const int Trials = 3;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _ship.AddChild(new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) });
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _launcher = new LauncherDevice { Name = "Launcher", Position = new Vector3(0, -1f, -2f), Missiles = 99 };
        _ship.AddChild(_launcher);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _drone = new Drone { Straight = true, Speed = 120, Health = 1e9f, Position = new Vector3(0, 0, -3000) };
        AddChild(_drone);

        Check("the ship's software is running", await Until(() => _display.Text("WEAPON").Contains("FIRECTL"), 40));
        await Press(c => c.NextWeapon = true);
        await Seconds(0.2);
        Check("next weapon selects the missiles", _display.Text("WEAPON").Contains("MSL"));

        GD.Print("     range   target          kills  hits   closest (m)   flight (s)   fuel left (m/s)   chaff   track on chaff");
        float[] ranges = { 2000, 3500, 5000 };
        var straight = new int[ranges.Length];
        var jinking = new int[ranges.Length];
        var breaking = new int[ranges.Length];
        var chaffed = new int[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            straight[i] = await Trials_(ranges[i], evades: false, Defence.None, Trials);
            jinking[i] = await Trials_(ranges[i], evades: true, Defence.None, Trials);
            breaking[i] = await Trials_(ranges[i], evades: true, Defence.Break, DefendedTrials);
            chaffed[i] = await Trials_(ranges[i], evades: true, Defence.Chaff, DefendedTrials);
        }
        int breakHits = breaking[0] + breaking[1] + breaking[2], chaffHits = chaffed[0] + chaffed[1] + chaffed[2];
        int all = DefendedTrials * ranges.Length;

        Check($"a straight target is hit ({straight[0]}/{Trials} at 2 km, {straight[1]}/{Trials} at 3.5 km)", straight[0] == Trials && straight[1] >= Trials - 1);
        Check($"a jinking target is hit close in ({jinking[0]}/{Trials} at 2 km)", jinking[0] >= Trials - 1);
        Check($"the firmware kept running", !_firmwareFailed);
        Check($"the drone saw the missiles coming and broke ({_sawWarning})", _sawWarning);
        Check($"chaff took the radar's track now and then ({_seduced} times)", _seduced > 0);
        Check($"chaff helps the drone, but it is no shield ({chaffHits}/{all} hit with chaff, {breakHits}/{all} breaking only)",
              chaffHits <= breakHits && chaffHits > 0);

        // The ship turns its back after the launch: the radar loses the lock,
        // the datalink goes quiet and the missiles fly on what they last knew
        GD.Print("     the ship turns away 1 s after the launch:");
        _turnAway = true;
        int blind = await Trials_(3500, evades: true, Defence.None, Trials);
        _turnAway = false;
        Check($"the missile heard nothing more and said so ({(_sawNoWord ? "no word" : "nothing")})", _sawNoWord);
        Check($"blind, a jinking target at 3.5 km is hit less ({blind}/{Trials} against {jinking[1]}/{Trials})", blind < jinking[1] || jinking[1] == 0);

        await Press(c => c.NextWeapon = true);
        await Seconds(0.2);
        Check("next weapon goes back to the gun", _display.Text("WEAPON").Contains("GUN"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private bool _firmwareFailed, _turnAway, _sawNoWord, _sawWarning;
    private int _seduced;
    private const int DefendedTrials = 5;
    private const float Lethal = 100f;          // a drone's health

    private enum Defence { None, Break, Chaff }

    private async Task<int> Trials_(float range, bool evades, Defence defence, int trials)
    {
        int hits = 0, kills = 0, chaffUsed = 0, seduced = 0;
        float closest = 0, flight = 0, fuel = 0;
        _drone.Defends = defence != Defence.None;
        _drone.ChaffCharges = defence == Defence.Chaff ? 6 : 0;
        for (int trial = 0; trial < trials; trial++)
        {
            Place(range, evades);
            _drone.Rearm();
            await Seconds(1.2);                 // a sweep, so the radar has it in its table
            await Press(c => c.TargetAhead = true);
            await Seconds(0.3);
            int launched = _launcher.Launched;
            await Press(c => c.Trigger = true);
            if (_launcher.Launched == launched)
            {
                GD.Print($"     {range,5:0} m  no launch (locked: {_radar.Locked != null}, SNR {_radar.Snr(_drone):0})");
                continue;
            }
            Missile m = _launcher.Newest;
            float closestNow = float.MaxValue, fuelNow = 0, age = 0;
            Basis facing = _ship.Basis;
            bool onChaff = false;
            while (IsInstanceValid(m) && _launcher.NewestResult == Missile.Outcome.Flying)
            {
                onChaff |= _radar.Locked is ChaffCloud;
                _sawWarning |= _drone.MissileWarning;
                if (_turnAway && m.Age > 1 && _ship.Basis.IsEqualApprox(facing))
                    _ship.Basis = facing.Rotated(Vector3.Up, Mathf.Pi);
                _sawNoWord |= _turnAway && (m.Status & Missile.StatusNoWord) != 0;
                closestNow = m.ClosestApproach;
                fuelNow = m.FuelLeft;
                age = m.Age;
                _firmwareFailed |= m.FirmwareFailed;
                await Seconds(1.0 / 60);
            }
            if (_launcher.NewestResult == Missile.Outcome.Hit)
                hits++;
            if (_launcher.NewestDamage >= Lethal)
                kills++;
            if (onChaff)
                seduced++;
            chaffUsed += _drone.ChaffCharges - _drone.ChaffLeft;
            _ship.Basis = facing;
            _ship.AngularVelocity = Vector3.Zero;
            closest += closestNow;
            flight += age;
            fuel += fuelNow;
            await Seconds(5);                   // the chaff fades
        }
        string what = !evades ? "straight" : defence switch
        {
            Defence.Break => "breaking 3 g",
            Defence.Chaff => "break + chaff",
            _ => "jinking 2 g",
        };
        GD.Print($"     {range,5:0} m  {what,-14}  {kills}/{trials}   {hits}/{trials}   {closest / trials,9:0.0}   {flight / trials,9:0.0}   {fuel / trials,13:0}   {chaffUsed,5}   {seduced,5}");
        _seduced += seduced;
        return kills;
    }

    private void Place(float range, bool evades)
    {
        _drone.Evades = evades;
        _drone.GlobalPosition = _ship.GlobalPosition + new Vector3(-0.1f * range, 0.2f * range, -range);
        _drone.LookAt(_drone.GlobalPosition + Vector3.Right, Vector3.Up);
        _drone.Fly(Vector3.Right * _drone.Speed);
    }

    private async Task Press(Action<PilotControlsRef> press)
    {
        var r = new PilotControlsRef();
        press(r);
        _controls.TargetAhead = r.TargetAhead;
        _controls.Trigger = r.Trigger;
        _controls.NextWeapon = r.NextWeapon;
        await Seconds(0.1);
        _controls.TargetAhead = _controls.Trigger = _controls.NextWeapon = false;
        await Seconds(0.1);
    }

    private sealed class PilotControlsRef
    {
        public bool TargetAhead, Trigger, NextWeapon;
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
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
    };
}
