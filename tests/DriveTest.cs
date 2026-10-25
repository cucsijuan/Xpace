using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The drive (GDD 6.5), on a ship whose computer runs FLIGHT, flown with
/// flight assist off so the throttle is the engine's command:
///
///   - the veil: a top speed where full thrust meets its drag, and a ship
///     coasting from 250 m/s halves its speed in about 13 s;
///   - the main engine's controller: full throttle gives the rating until
///     the core (as the controller works it out) reaches the cut-out, then
///     the engine pulses and keeps up only what the cooling carries away;
///     half throttle never cuts out; the real core stays below safe;
///   - the cooling boost (the pilot's boost button, through FLIGHT) keeps
///     up more thrust and makes the radiators glow brighter;
///   - the diagnostic port reads the controller's ROM;
///   - its flaws: a lower coolant reading in calibration mode, thrust timed
///     between the controller's looks, and the bench mode (no cut-out, the
///     trim) all get more thrust out of it, and the engine pays: it wears;
///     thrust past what the hull is rated for breaks the structure; pushed
///     far enough the engine fails and takes the ship with it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/drive_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DriveTest : Node3D
{
    private ShipBody _ship;
    private EngineDevice _engine;
    private PowerDevice _power;
    private ShipHull _hull;
    private PilotControls _controls;
    private Func<float> _throttle;
    private int _failures;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        Veil();

        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _ship.AddChild(new DisplayDevice { Screens = new Godot.Collections.Array<DisplayScreen> { GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres") } });
        _power = new PowerDevice { Name = "Power" };
        _hull = new ShipHull { Name = "Hull", Insured = false, Integrity = 300 };
        _engine = new EngineDevice { Name = "Engine", Spec = GD.Load<EngineSpec>("res://assets/ship/engines/engine_d50.tres") };
        foreach (Node n in new Node[] { _power, new HeatDevice { Name = "Heat" }, _hull, _engine, new ShipSignature { Name = "Signature" } })
            _ship.AddChild(n);
        _ship.ControlsSource = () =>
        {
            PilotControls c = _controls;
            if (_throttle != null)
                c.Throttle = _throttle();
            return c;
        };
        AddChild(_ship);

        Check("FLIGHT is resident", await Until(() => _ship.Computer.ScreenText().Contains("FLIGHT OFF removes it"), 40));
        await Press(() => _controls.AssistButton = true, () => _controls.AssistButton = false);
        Check("flight assist off: the throttle is the engine's command", !_ship.AssistOn);

        await Honest();
        await Boost();
        Diagnostic();
        await Calibration();
        await Sampling();
        await Bench();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---------- the veil ----------

    private void Veil()
    {
        var spec = new ShipSpec();
        var s = new ShipState { Orientation = Quaternion.Identity };
        for (int i = 0; i < 120 * 60; i++)
            s = ShipMotion.Step(s, new Vector3(0, 0, -spec.MainThrust), Vector3.Zero, spec.Mass, spec.Inertia, 1f / 60, spec.VeilDrag);
        float top = MathF.Sqrt(spec.MainThrust / spec.Mass / spec.VeilDrag);
        Check($"the veil: full thrust tops out at {s.Velocity.Length():0} m/s ({top:0} where thrust meets drag)",
              MathF.Abs(s.Velocity.Length() - top) < top * 0.01f);
        s = new ShipState { Orientation = Quaternion.Identity, Velocity = new Vector3(0, 0, -250) };
        float t = 0;
        while (s.Velocity.Length() > 125)
        {
            s = ShipMotion.Step(s, Vector3.Zero, Vector3.Zero, spec.Mass, spec.Inertia, 1f / 60, spec.VeilDrag);
            t += 1f / 60;
        }
        Check($"coasting from 250 m/s the veil halves the speed in {t:0.0} s", t > 12 && t < 14);
        s = new ShipState { Orientation = Quaternion.Identity, Velocity = new Vector3(0, 0, -50) };
        for (int i = 0; i < 60; i++)
            s = ShipMotion.Step(s, Vector3.Zero, Vector3.Zero, spec.Mass, spec.Inertia, 1f / 60, spec.VeilDrag);
        Check($"at combat speeds it hardly brakes: 50 m/s is {s.Velocity.Length():0.0} a second later", s.Velocity.Length() > 49);
    }

    // ---------- the controller, honest ----------

    private async Task Honest()
    {
        Fresh();
        Run run = await Fly(60, () => 1);
        Check($"full throttle: the rating until the cut-out ({run.FirstCut:0.0} s), then it pulses ({run.Cuts} cuts)",
              run.FirstCut > 10 && run.FirstCut < 40 && run.Cuts >= 5);
        Check($"it keeps up only what the cooling carries away: {run.MeanLast:P0} over the last 20 s, {run.Mean:P0} over the minute",
              run.MeanLast > 0.4f && run.MeanLast < 0.75f);
        Check($"the real core stays below safe ({run.HottestCore:0} C of {_engine.Spec.SafeTemperature:0}), no wear",
              run.HottestCore < _engine.Spec.SafeTemperature && _engine.Wear == 0);
        _honestMean = run.Mean;
        _honestGlow = run.MeanGlow;

        Fresh();
        run = await Fly(60, () => 0.5f);
        Check($"half throttle never cuts out ({run.Cuts} cuts, core {_engine.ReportedCore:0} C)", run.Cuts == 0);
    }

    private float _honestMean, _honestGlow;

    private async Task Boost()
    {
        Fresh();
        _controls.BoostButton = true;               // FLIGHT turns it into the cooling boost
        Run run = await Fly(60, () => 1);
        _controls.BoostButton = false;
        await Seconds(0.1);
        Check($"the cooling boost: {run.Mean:P0} over the minute against {_honestMean:P0}, the first cut at {run.FirstCut:0.0} s",
              run.Mean > _honestMean + 0.1f);
        Check($"its radiators glow brighter for heat seekers ({run.MeanGlow:0} against {_honestGlow:0})", run.MeanGlow > _honestGlow * 1.2f);
    }

    // ---------- the diagnostic port ----------

    private void Diagnostic()
    {
        byte[] rom = Godot.FileAccess.GetFileAsBytes(_engine.Spec.FirmwarePath);
        int matches = 0, n = Math.Min(64, rom.Length);
        ShipBus bus = _ship.Computer.Bus;
        for (int i = 0; i < n; i++)
        {
            int address = 0x100 + i;
            bus.Out(EngineDevice.Slot, 12, (byte)address);
            bus.Out(EngineDevice.Slot, 13, (byte)(address >> 8));
            _engine.AfterStep(_ship, 0);
            _engine.BeforeStep(_ship, 0);
            if (bus[EngineDevice.Slot, 14] == rom[i])
                matches++;
        }
        Check($"the diagnostic port reads the controller's ROM ({matches} of {n} bytes)", matches == n);
    }

    // ---------- the flaws ----------

    private async Task Calibration()
    {
        Fresh();
        Write(6, 0x80);                             // the cooling unit's calibration mode
        Write(4, 20, 0);                            // the coolant "is" at 20 C
        await Seconds(0.05);
        Run run = await Fly(60, () => 1);
        Check($"calibration mode: the coolant reads {_engine.ReportedCoolant:0} C, the controller never cuts ({run.Cuts} cuts, {run.Mean:P0})",
              run.Cuts == 0 && run.Mean > 0.95f);
        Check($"the real core runs to {run.HottestCore:0} C while the controller reckons {_engine.ReportedCore:0}: it wears ({_engine.Wear:P1} of its thrust lost)",
              run.HottestCore > _engine.Spec.SafeTemperature && _engine.Wear > 0.01f);
        Write(6, 0);
        await Seconds(0.05);
    }

    /// <summary>
    /// Writes the engine's registers as a program would in the middle of a
    /// step (the engine takes them at once: between steps the next step's
    /// readings would cover them first).
    /// </summary>
    private void Write(int offset, params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            _ship.Computer.Bus.Out(EngineDevice.Slot, offset + i, bytes[i]);
        _engine.AfterStep(_ship, 0);
    }

    private async Task Sampling()
    {
        Fresh();
        // Its reckoning on the bus changes only when it looks: watched step by step (the controls are read
        // at the start of each step, so a change seen there was made by the step before)
        int calls = 0, looked = -1;
        float last = _engine.ReportedCore;
        _throttle = () =>
        {
            calls++;
            if (looked < 0 && _engine.ReportedCore != last && calls > 2)
                looked = calls - 1;
            last = _engine.ReportedCore;
            return 1;
        };
        await Seconds(2);
        Check("the controller's looks can be seen on the bus", looked >= 0);
        // It looks every 15 ticks at the thrust of the tick before: ease off on that tick only
        Run run = await Fly(60, () => (++calls - looked) % 15 == 14 ? 0 : 1);
        Check($"thrust timed between its looks: {run.Mean:P0} over the minute against {_honestMean:P0} honestly",
              run.Mean > _honestMean + 0.15f);
        Check($"and the core pays: {run.HottestCore:0} C, {_engine.Wear:P1} worn", run.HottestCore > _engine.Spec.SafeTemperature && _engine.Wear > 0);
    }

    private async Task Bench()
    {
        Fresh();
        await Serve(0xA7, 0x1C, 0x00);
        Check("a wrong service sequence opens nothing", (_ship.Computer.Bus[EngineDevice.Slot, 6] & EngineDevice.StatusBench) == 0);
        await Serve(0xA7, 0x1C, 0xE5, 0x52);
        Check("the shop's sequence opens the bench mode", (_ship.Computer.Bus[EngineDevice.Slot, 6] & EngineDevice.StatusBench) != 0);

        float rated = _engine.Spec.RatedThrust;
        float hull = _hull.Left;
        Run run = await Fly(3, () => 1);
        Check($"bench mode at trim 1: the rating ({_engine.Thrust / rated:P0}), the hull untouched ({hull - _hull.Left:0.0})",
              MathF.Abs(_engine.Thrust / rated - 1) < 0.02f && _hull.Left == hull);
        await Serve((byte)'T', 64);
        hull = _hull.Left;
        run = await Fly(3, () => 1);
        float a = _engine.Thrust / _ship.Spec.Mass;
        Check($"trim 1.25: {_engine.Thrust / rated:P0} of the rating, {a:0} m/s² on a hull rated for {_ship.Spec.RatedAcceleration:0}: the structure breaks ({hull - _hull.Left:0.0} points)",
              MathF.Abs(_engine.Thrust / rated - 1.25f) < 0.02f && _hull.Left < hull - 10);
        await Serve((byte)'T', 255);
        _throttle = () => 1;
        await Seconds(0.2);
        Check($"trim 2: the engine gives no more than it physically can ({_engine.Thrust / rated:P0}, its most is {_engine.Spec.MaxThrust / rated:P0})",
              MathF.Abs(_engine.Thrust - _engine.Spec.MaxThrust) < rated * 0.01f);
        _throttle = () => 0;
        await Seconds(0.5);
        _hull.Restore();                            // the insurer's ship: the engine repaired, its controller as from the factory

        // On the bench nothing cuts it out: pushed as hard as it goes the core runs away (a worn core gives
        // less and heats less, but not enough) and the engine fails
        await Serve(0xA7, 0x1C, 0xE5, 0x52, (byte)'T', 255);
        hull = _hull.Left;
        _ship.Spec.RatedAcceleration = 1000;        // the structure out of it: only the engine's heat
        run = await Fly(90, () => 1);
        Check($"bench mode, the trim at its most: never cut out ({run.Cuts}); the core reaches {run.HottestCore:0} C and the engine fails ({_engine.Failed})",
              run.Cuts == 0 && _engine.Failed);
        Check($"and takes the ship with it (hull {_hull.Left:0}, wrecked {_hull.Wrecked})", _hull.Wrecked);
    }

    // ---------- helpers ----------

    /// <summary>A repaired engine and a whole ship, at rest, the power even.</summary>
    private void Fresh()
    {
        _throttle = () => 0;
        _ship.Spec.RatedAcceleration = 50;
        if (_hull.Wrecked)
            _hull.Restore();
        _engine.Rearm();
        _power.Set(4, 4, 4);
        _power.Rearm();
        _ship.State = new ShipState { Orientation = Quaternion.Identity };
    }

    private sealed class Run
    {
        public float FirstCut = -1, Mean, MeanLast, HottestCore, MeanGlow;
        public int Cuts;
    }

    /// <summary>Flies for a while at a throttle; what the engine did.</summary>
    private async Task<Run> Fly(double seconds, Func<float> throttle)
    {
        var run = new Run();
        _throttle = throttle;
        int steps = (int)(seconds * 60), lastSteps = Math.Min(steps, 20 * 60);
        bool wasCut = false;
        double sum = 0, sumLast = 0, glow = 0;
        for (int i = 0; i < steps; i++)
        {
            await Seconds(1.0 / 60);
            float f = _engine.Thrust / _engine.Spec.RatedThrust;
            sum += f;
            if (i >= steps - lastSteps)
                sumLast += f;
            glow += _engine.Glow;
            bool cut = (_engine.ControllerStatus & EngineDevice.StatusCut) != 0;
            if (cut && !wasCut)
            {
                run.Cuts++;
                if (run.FirstCut < 0)
                    run.FirstCut = i / 60f;
            }
            wasCut = cut;
            run.HottestCore = MathF.Max(run.HottestCore, _engine.CoreTemperature);
            if (_ship.State.Position.Length() > 20000)
                _ship.State = _ship.State with { Position = Vector3.Zero };     // stay near the origin
        }
        _throttle = () => 0;
        run.Mean = (float)(sum / steps);
        run.MeanLast = (float)(sumLast / lastSteps);
        run.MeanGlow = (float)(glow / steps);
        return run;
    }

    /// <summary>Bytes to the engine's service port, one a step, as a program would write them.</summary>
    private async Task Serve(params byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            _ship.Computer.Bus.Out(EngineDevice.Slot, 15, b);
            await Seconds(1.0 / 60);
        }
        await Seconds(0.05);
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
