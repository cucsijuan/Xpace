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
/// A lock held close by, against a ship with its jammer on and off, and on a freighter on its way, flown after over
/// several kilometres so that the world's origin moves under the tracker: how much of the time the radar keeps it,
/// with FIRECTL at the buttons.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/close_lock_test.tscn
/// </summary>
public partial class CloseLockTest : Node
{
    private int _failures;
    private ShipBody _ship, _prey;
    private RadarDevice _radar;
    private bool _press;
    private Vector3 _at, _velocity;
    private float _range, _jink, _clock;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        _radar = _ship.Find<RadarDevice>();
        _prey = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        _prey.GetNode("Pilot").ProcessMode = ProcessModeEnum.Disabled;
        main.Sector.AddChild(_prey);
        _ship.ControlsSource = () => new PilotControls { TargetAhead = _press };
        await Until(() => _ship.Computer.ScreenText().Contains("resident"), 90);
        await Seconds(8);
        _ship.State = new ShipState { Position = _ship.GlobalPosition + new Vector3(0, 20000, 0), Orientation = Quaternion.Identity };
        await Seconds(1);
        var jammer = _prey.Device<JammerDevice>();
        foreach (float range in new[] { 60f, 400f, 600f, 1000f })
            foreach (bool jam in new[] { false, true })
                    {
                        float speed = 0, jink = 0;
                        float held = await Case(range, jam, speed, jink, jammer);
                        GD.Print($"{range,6:0} m  jammer {(jam ? "on " : "off")}  speed {speed,3:0}  jink {jink,2:0} m/s2  lock held {held * 100,3:0}%  snr {_radar.LockedSnr:0}");
                        Check($"held at {range:0} m, jammer {jam}, speed {speed}, jink {jink}", held > 0.9f);
                    }
        await Freighter(main);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // A freighter on its way, as a pilot finds it, followed from behind
    private async Task Freighter(SectorHost main)
    {
        var view = main.GetNode<CargoView>("CargoView");
        _prey.QueueFree();
        ShipBody cargo = null;
        await Until(() => (cargo = view.Shown.Values.FirstOrDefault(s => IsInstanceValid(s) && s.GetParent() == main.Sector && s.LinearVelocity.Length() > 50 && s.LinearVelocity.Length() < 400 && s.Holder != null)) != null, 300);
        Check("a freighter is under way", cargo != null);
        if (cargo == null)
            return;
        var rwr = _ship.Find<RwrDevice>();
        foreach (float range in new[] { 400f, 600f })
        {
            bool had = false;
            int kept = 0, total = 0;
            for (int i = 0; i < 60 * 40 && IsInstanceValid(cargo); i++)
            {
                Vector3 back = cargo.LinearVelocity.LengthSquared() > 1 ? cargo.LinearVelocity.Normalized() : Vector3.Forward;
                Basis facing = Basis.LookingAt(back, Vector3.Up);
                _ship.State = new ShipState { Position = cargo.GlobalPosition - back * range, Orientation = facing.GetRotationQuaternion(), Velocity = cargo.LinearVelocity };
                _press = i % 60 < 8 && _radar.Locked == null && i > 60;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                total++;
                if (_radar.Locked != null)
                    kept++;
                else if (had)
                    GD.Print($"   lost at {i / 60.0:0.00} s: origin {FloatingOrigin.Of(_ship).Origin.X:0} ship at {_ship.Position.Length():0} speed {cargo.LinearVelocity.Length():0} track was {_radar.TrackPosition.DistanceTo(cargo.GlobalPosition):0} m off, jam {_radar.Jamming(cargo.GlobalPosition - _ship.GlobalPosition):0}, in cone {Vector3.Forward.AngleTo(_ship.GlobalBasis.Inverse() * (cargo.GlobalPosition - _ship.GlobalPosition)) * 57.3f:0}");
                had = _radar.Locked != null;
                if (i % 180 == 0)
                {
                    var j = cargo.Device<JammerDevice>();
                    GD.Print($"   {i / 60,3} s {cargo.Name} {range:0} m speed {cargo.LinearVelocity.Length():0} lock {_radar.Locked?.GetType().Name} snr {_radar.LockedSnr:0} rcs {(_radar.Locked as IRadarTarget)?.RadarCrossSection:0.0} dormant {cargo.Dormant} jam {j?.Mode} comp {cargo.Computer.Powered} ship {_ship.GlobalPosition.DistanceTo(cargo.GlobalPosition):0}");
                }
            }
            GD.Print($"freighter {range:0} m: lock held {kept * 100 / Math.Max(1, total)}%");
            Check($"the lock stays on the freighter at {range:0} m, the origin moving under it", kept > total * 0.9f);
            Check("awake and locked, a freighter's radar stays off", cargo.Device<RadarDevice>()?.On == false);
            string heard = string.Join(", ", rwr.Contacts.Where(c => Combat.ShipOf(c.Emitter as Node) == cargo).Select(c => c.Emitter.GetType().Name));
            Check($"the warning receiver hears no radar of it, only the jammer it answers a lock with ({heard})", !rwr.Contacts.Any(c => c.Emitter is RadarEmitter && Combat.ShipOf(c.Emitter as Node) == cargo));
        }
    }

    private async Task<float> Case(float range, bool jam, float speed, float jink, JammerDevice jammer)
    {
        _range = range;
        _velocity = Vector3.Right * speed;
        _jink = jink;
        _clock = 0;
        _at = _ship.GlobalPosition;
        Place();
        if (_radar.Locked != null)
            await Tap();
        _prey.Computer.Bus.Out(JammerDevice.Slot, 2, (byte)(jam ? 1 : 0));
        jammer.AfterStep(_prey, 0);
        await Fly(1);
        for (int i = 0; i < 5 && _radar.Locked == null; i++)
            await Tap();
        int kept = 0, steps = 600;
        for (int i = 0; i < steps; i++)
        {
            await Fly(1.0 / 60);
            if (_radar.Locked != null)
                kept++;
        }
        return kept / (float)steps;
    }

    private async Task Tap()
    {
        _press = true;
        await Fly(0.12);
        _press = false;
        await Fly(0.5);
    }

    // The two ships flown by hand: the same way, the prey ahead, shaking across if it jinks
    private async Task Fly(double seconds)
    {
        for (int i = 0; i < Math.Max(1, seconds * 60); i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _clock += 1f / 60;
            _at = _ship.GlobalPosition + _velocity / 60;
            Place();
        }
    }

    private void Place()
    {
        float w = 2f;
        Vector3 shake = Vector3.Up * (_jink / (w * w) * MathF.Sin(w * _clock));
        Vector3 shakeSpeed = Vector3.Up * (_jink / w * MathF.Cos(w * _clock));
        _ship.State = new ShipState { Position = _at, Orientation = Quaternion.Identity, Velocity = _velocity };
        _prey.State = new ShipState { Position = _at + new Vector3(0, 0, -_range) + shake, Orientation = Quaternion.Identity, Velocity = _velocity + shakeSpeed };
    }

    private async Task Seconds(double s)
    {
        for (int i = 0; i < s * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        if (!ok)
        {
            GD.Print($"FAIL {what}");
            _failures++;
        }
    }
}
