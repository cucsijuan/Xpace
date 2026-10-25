using System;
using System.Collections.Generic;
using Godot;
using Xpace.Computer;

namespace Xpace.Ship;

/// <summary>
/// The main engine, its controller and its cooling unit, bus slot 15
/// (1F0h). What the engine is, is its model's (<see cref="Spec"/>, an
/// <see cref="EngineSpec"/>).
///
/// The ship's software asks for thrust (the thrusters' forward command);
/// the engine's own controller (a bare 8086 with firmware in ROM,
/// software/engine/engine.asm) decides what it gives: what was asked,
/// times its trim; nothing while it reckons the core too hot. It has no
/// thermometer in the core: it works the core's temperature out from the
/// coolant loop's, as the cooling unit reports it, and the thrust it gives.
///
/// The physics is this device's: the core heats with the thrust, passes
/// the heat to the coolant, and the radiators give it off to space, harder
/// with more power to the engines and with the cooling boost (which draws
/// power and makes the radiators glow brighter for heat seekers). A core
/// over its safe temperature wears (the engine gives less, for good); at
/// its failing temperature the engine fails and takes the ship with it.
/// Thrust beyond what the hull is rated for (<see cref="ShipSpec.RatedAcceleration"/>)
/// damages the structure. None of that is the controller's business: it
/// does not know.
///
/// Registers: +0 type 17  +1 version 1
/// +2 the core's temperature, as the controller works it out (C)
/// +4 the coolant's, as the cooling unit reports it (C); written while the
///    cooling unit is in calibration mode, it reports that instead
/// +6 status (bit 0 cut out, bit 2 bench mode, bit 3 cooling boost,
///    bit 4 calibration mode, bit 5 worn, bit 6 failed); write: the cooling
///    unit (bit 0 boost, bit 7 calibration mode)
/// +7 thrust given, 0 to 255 of the rating (more is shown as 255)
/// +8 cut-out  +10 resume temperatures (C, as the controller has them)
/// +12 write: an address in the controller's ROM (the diagnostic port)
/// +14 the byte at that address, the step after
/// +15 write: a byte to the controller's service port
/// </summary>
public partial class EngineDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 15;
    public const byte Type = 17;
    public const byte StatusCut = 1, StatusBench = 4, StatusBoost = 8, StatusCalibration = 16, StatusWorn = 32, StatusFailed = 64;

    [Export] public EngineSpec Spec { get; set; }

    // The truth
    public float CoreTemperature { get; private set; }
    public float CoolantTemperature { get; private set; }
    /// <summary>The share of its thrust the core has lost, 0 to 1.</summary>
    public float Wear { get; private set; }
    public bool Failed { get; private set; }
    /// <summary>What it pushed in the last step (N).</summary>
    public float Thrust { get; private set; }
    /// <summary>What the radiators gave off in the last step (MW).</summary>
    public float Radiated { get; private set; }

    // What the controller and the cooling unit say
    public float ReportedCore { get; private set; }
    public float ReportedCoolant => _calibrated ?? CoolantTemperature;
    public byte ControllerStatus { get; private set; }
    public float CutOut { get; private set; }
    public float Resume { get; private set; }
    public bool Boosting { get; private set; }

    /// <summary>What heat seekers see of it besides the plume: the radiators, and a core past safe.</summary>
    public float Glow => Failed ? 0 : S.RadiatorGlowPerMegawatt * Radiated +
        S.CoreGlow * Math.Clamp((CoreTemperature - S.SafeTemperature) / (S.FailTemperature - S.SafeTemperature), 0, 1);

    private EngineSpec S => Spec ??= new EngineSpec();
    private Microcontroller _mcu;
    private byte[] _firmware;
    private PowerDevice _power;
    private ShipHull _hull;
    private float? _calibrated;
    private bool _calibrating, _boostAsked;
    private ushort _diagAddress;
    private byte _serviceCount;
    private static readonly Dictionary<string, byte[]> Firmwares = new();

    public override void _Ready()
    {
        foreach (Node n in GetParent().GetChildren())
        {
            _power ??= n as PowerDevice;
            _hull ??= n as ShipHull;
        }
        Rearm();
    }

    public override void _ExitTree()
    {
        _mcu?.Dispose();
        _mcu = null;
    }

    /// <summary>A new engine, or the old one repaired (insurance): cold, unworn, its controller as from the factory.</summary>
    public void Rearm()
    {
        CoreTemperature = CoolantTemperature = S.SinkTemperature;
        Wear = 0;
        Failed = false;
        _calibrated = null;
        _calibrating = _boostAsked = false;
        _mcu?.Dispose();
        if (!Firmwares.TryGetValue(S.FirmwarePath, out _firmware))
        {
            _firmware = Godot.FileAccess.GetFileAsBytes(S.FirmwarePath);
            Firmwares[S.FirmwarePath] = _firmware;
            if (_firmware == null || _firmware.Length == 0)
                GD.PushError($"EngineDevice: cannot read {S.FirmwarePath} (build it: see native/CMakeLists.txt)");
        }
        _mcu = new Microcontroller(_firmware) { InstructionsPerSecond = S.InstructionsPerSecond };
    }

    // ---------- the bus ----------

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        bus.SetWord(Slot, 2, (int)MathF.Round(ReportedCore));
        bus.SetWord(Slot, 4, (int)MathF.Round(ReportedCoolant));
        bus[Slot, 6] = (byte)(ControllerStatus & (StatusCut | StatusBench) | (Boosting ? StatusBoost : 0) |
                              (_calibrating ? StatusCalibration : 0) | (Wear > 0.01f ? StatusWorn : 0) | (Failed ? StatusFailed : 0));
        bus[Slot, 7] = (byte)Math.Clamp(MathF.Round(Thrust / S.RatedThrust * 255), 0, 255);
        bus.SetWord(Slot, 8, (int)MathF.Round(CutOut));
        bus.SetWord(Slot, 10, (int)MathF.Round(Resume));
        bus[Slot, 14] = _firmware != null && _diagAddress >= 0x100 && _diagAddress - 0x100 < _firmware.Length
            ? _firmware[_diagAddress - 0x100] : (byte)0xFF;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (bus.TakeWritten(Slot, 6))
        {
            int command = bus[Slot, 6];
            _boostAsked = (command & 1) != 0;
            _calibrating = (command & 0x80) != 0;
            if (!_calibrating)
                _calibrated = null;
        }
        if (bus.TakeWritten(Slot, 4) | bus.TakeWritten(Slot, 5) && _calibrating)
            _calibrated = bus.Word(Slot, 4);
        if (bus.TakeWritten(Slot, 12) | bus.TakeWritten(Slot, 13))
            _diagAddress = (ushort)bus.Word(Slot, 12);
        if (bus.TakeWritten(Slot, 15))
            Service(bus[Slot, 15]);
    }

    /// <summary>A byte to the controller's service port.</summary>
    public void Service(byte b)
    {
        if (_mcu == null)
            return;
        _mcu[2] = b;
        _mcu[1] = ++_serviceCount;
    }

    /// <summary>The cooling boost, asked for directly (a test, the AI); the program's last word otherwise.</summary>
    public void AskBoost(bool on) => _boostAsked = on;

    // ---------- every step ----------

    /// <summary>
    /// The ship (<paramref name="hull"/>'s spec, at <paramref name="at"/>)
    /// asks for <paramref name="wanted"/> (0 to 1 of the rating); the
    /// controller decides, the engine pushes and heats. What it pushes (N).
    /// </summary>
    public float Drive(ShipSpec hull, Node3D at, float wanted, double delta)
    {
        float dt = (float)delta;
        float given = 0;
        if (_mcu != null && _mcu.Valid)
        {
            _mcu[0] = (byte)Math.Clamp(MathF.Round(wanted * 255), 0, 255);
            _mcu.SetWord(4, (int)MathF.Round(ReportedCoolant * 4));
            _mcu.Tick(delta);
            given = (ushort)_mcu.Word(6) / 256f;
            ControllerStatus = _mcu[8];
            ReportedCore = _mcu.Word(10) / 4f;
            CutOut = _mcu.Word(12) / 4f;
            Resume = _mcu.Word(14) / 4f;
        }
        Thrust = Failed ? 0 : MathF.Min(given * S.RatedThrust, S.MaxThrust) * (1 - Wear);

        // The cooling: the engines' share of the power drives it; the boost, harder, if the capacitor pays for it
        float drive = S.CoolingWithNoPips + (S.CoolingWithMostPips - S.CoolingWithNoPips) *
                      (_power == null ? 0.5f : _power.Pips[(int)PowerStore.Engines] / (float)PowerDevice.MostHalfPips);
        Boosting = false;
        if (_boostAsked && !Failed)
        {
            double want = S.BoostMegawatts * delta;
            double got = _power?.DrawUpTo(PowerStore.Engines, want) ?? want;
            float share = want > 0 ? (float)(got / want) : 1;
            drive *= 1 + (S.BoostCooling - 1) * share;
            Boosting = share > 0.5f;
        }

        // Heat: into the core, core to coolant, coolant to space
        float heatIn = S.HeatAtRated * Thrust / S.RatedThrust;
        float toCoolant = S.CoreConductance * (CoreTemperature - CoolantTemperature);
        Radiated = MathF.Max(0, S.RadiatorConductance * drive * (CoolantTemperature - S.SinkTemperature));
        CoreTemperature += (heatIn - toCoolant) / S.CoreCapacity * dt;
        CoolantTemperature += (toCoolant - Radiated) / S.CoolantCapacity * dt;

        if (!Failed && CoreTemperature > S.SafeTemperature)
            Wear = MathF.Min(0.95f, Wear + S.WearPerDegreeSecond * (CoreTemperature - S.SafeTemperature) * dt);
        if (!Failed && CoreTemperature >= S.FailTemperature)
        {
            Failed = true;
            _hull?.Burn(S.FailDamage);
            Space.Explosion.Spawn(at.GetParent(), at.GlobalPosition + at.GlobalBasis.Z * 4, 12f, 1.0, 8f);
        }

        // The structure: more than the hull is rated for breaks it
        float rated = hull.RatedAcceleration;
        float acceleration = Thrust / hull.Mass;
        if (rated > 0 && acceleration > rated)
            _hull?.Burn(hull.StressDamagePerSecond * (acceleration / rated - 1) * dt);
        return Thrust;
    }
}
