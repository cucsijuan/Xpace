using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// How often the turret hits, by range, against a drone that jinks hard once
/// locked; and what a jammer on the drone does to the radar.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/accuracy_test.tscn
/// Prints the table; exits with 0 if the checks passed.
/// </summary>
public partial class AccuracyTest : Node3D
{
    private ShipBody _ship;
    private RadarDevice _radar;
    private TurretDevice _turret;
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
        _display = new DisplayDevice();
        _ship.AddChild(_display);
        _turret = new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) };
        _ship.AddChild(_turret);
        _radar = new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" };
        _ship.AddChild(_radar);
        _ship.ControlsSource = () => _controls;
        AddChild(_ship);
        _drone = new Drone { Straight = true, Speed = 120, Health = 1e9f, Position = new Vector3(0, 0, -3000) };
        AddChild(_drone);

        Check("the ship's software is running", await Until(() => _display.Text(1).Contains("FIRE CONTROL"), 40));

        GD.Print("     range   target          hits / rounds   hit rate   SNR");
        float straight2k = await Trial(2000, evades: false);
        float[] ranges = { 500, 1000, 2000, 3000, 4000, 5000 };
        var rates = new float[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            float sum = 0;
            for (int trial = 0; trial < Trials; trial++)
                sum += await Trial(ranges[i], evades: true);
            rates[i] = sum / Trials;
            GD.Print($"     {ranges[i],5:0} m  average of {Trials}               {rates[i],6:P0}");
        }

        Check($"a straight target at 2 km is still hit almost always ({straight2k:P0})", straight2k >= 0.8f);
        Check($"close in, a jinking target is hit often ({rates[0]:P0} at 500 m)", rates[0] >= 0.3f);
        Check($"far out, it is almost always missed ({rates[4]:P0} at 4 km)", rates[4] <= 0.1f);
        Check("the hit rate falls with the range", (rates[0] + rates[1]) / 2 > rates[2] && rates[2] >= rates[4]);

        // A jammer on the drone: no lock far out, burn-through close in
        var pod = new JammerPod();
        _drone.AddChild(pod);
        Place(4000, evades: false);
        await Seconds(1.2);
        await Lock();
        Check($"jammed at 4 km: the radar cannot lock (SNR {Db(_radar.Snr(_drone))} dB)", _radar.Locked == null);
        Place(1500, evades: false);
        await Seconds(1.2);
        await Lock();
        Check($"jammed at 1.5 km: it burns through and locks (SNR {Db(_radar.Snr(_drone))} dB)", _radar.Locked == _drone);
        pod.QueueFree();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The drone crossing at a range; lock it, let the filter settle, fire 3 s, count the hits.</summary>
    private async Task<float> Trial(float range, bool evades)
    {
        Place(range, evades);
        await Seconds(1.2);                     // a sweep, so the radar has it in its table
        await Lock();
        await Seconds(1);                       // the filter settles
        int rounds0 = _turret.RoundsFired, hits0 = _drone.Hits;
        _controls.Trigger = true;
        await Seconds(3);
        _controls.Trigger = false;
        int rounds = _turret.RoundsFired - rounds0;
        await Seconds(range / _turret.MuzzleSpeed + 0.5);
        int hits = _drone.Hits - hits0;
        float rate = rounds == 0 ? 0 : (float)hits / rounds;
        GD.Print($"     {range,5:0} m  {(evades ? "jinking 3 g" : "straight   "),-14}  {hits,4} / {rounds,-4}      {rate,6:P0}   {Db(_radar.LockedSnr),3}");
        await Seconds(1);                       // the last heat goes
        return rate;
    }

    private void Place(float range, bool evades)
    {
        _drone.Evades = evades;
        _drone.GlobalPosition = _ship.GlobalPosition + new Vector3(-0.1f * range, 0.35f * range, -range);    // well above: a dorsal turret looks up
        _drone.LookAt(_drone.GlobalPosition + Vector3.Right, Vector3.Up);
        _drone.Fly(Vector3.Right * _drone.Speed);
    }

    private async Task Lock()
    {
        _controls.TargetAhead = true;
        await Seconds(0.1);
        _controls.TargetAhead = false;
        await Seconds(0.2);
    }

    private static int Db(float snr) => (int)MathF.Round(10 * MathF.Log10(MathF.Max(snr, 1e-3f)));

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
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
