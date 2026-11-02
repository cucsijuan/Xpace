using System;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The cruise generator, bus slot 18 (220h): it thins the veil (GDD 4.3.1), so
/// the drag falls, and it pushes the ship itself in place of the main engine,
/// which rests. The rating plate reads what the ship can do as it is now, so
/// the flight program goes on flying it as ever.
///
/// How thin depends on the medium: the veil's drag is the normal one times
/// density / thinning (<see cref="MediumMass"/>), never above normal. Nearing
/// a mass the top speed falls with the distance, and where the density
/// reaches the generator's thinning it can do nothing and drops out: no rule
/// says so, there is just no veil left to thin. It will not start there
/// either. Coming back, the generator lets the drag rise only as fast as
/// <see cref="GeneratorSpec.BrakeAcceleration"/> allows.
///
/// Registers: +0 type 20  +1 version 1  +2 status: bit 0 on, bit 1 charging,
/// bit 2 too near a mass to thin, bit 3 it dropped out since last asked to
/// start  +3 how thin the veil is, 255 as normal, 0 at its thinnest
/// +4 word the medium's density (1/16; 32767 at most)  +5 write: 1 start,
/// 0 stop  +6 word the speed along the ship's way, m/s  +7 the pilot's
/// cruise button  +8 write: a contact of the radio's table to steer for
/// (FFh none)  +9 that contact, or FFh
///
/// Against an interdictor: its beam thickens the medium at a frequency that wanders. Tuned to it, the generator
/// cancels most of it; off by more than <see cref="GeneratorSpec.TuneBand"/>, none. Past what it can bear it holds on
/// for <see cref="GeneratorSpec.HoldSeconds"/> (the strain builds and eases), then drops out.
/// Status bit 4: a beam is on it. +10 its frequency (read), where to tune it (write), 0 to 255, 0.5 to 4 Hz
/// +11 the pilot's tuning buttons: bit 0 up, bit 1 down  +12 strain, 255 drops out
/// +13 how much of the beam it cancels, 255 all it can  +14 the beam's wave as received, a signed byte
/// +15 its own wave, a signed byte
/// </summary>
public partial class CruiseDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 18;
    public const byte Type = 20, Version = 1;
    public const byte NoCourse = 0xFF;
    private const float WakeFullAt = 500f;      // m/s
    public const byte StatusOn = 1, StatusCharging = 2, StatusLocked = 4, StatusDropped = 8, StatusBeam = 16;

    [Export] public GeneratorSpec Spec { get; set; }

    /// <summary>What the veil's drag is multiplied by now: 1 as normal.</summary>
    public float Thin { get; private set; } = 1;
    /// <summary>The generator is holding the veil thin (or thinning it).</summary>
    public bool On { get; private set; }
    public bool Charging => _charge > 0;
    /// <summary>The medium here is too dense for it to thin anything.</summary>
    public bool Locked { get; private set; }
    /// <summary>It dropped out by itself since the pilot last started it.</summary>
    public bool DroppedOut { get; private set; }
    public float Density { get; private set; } = 1;
    /// <summary>How much of the medium it is stirring, 0 to 1: how thin the veil is, at speed.</summary>
    public float Wake { get; private set; }
    /// <summary>What that adds to the radar echo (m²).</summary>
    public float WakeEcho => Wake * S.WakeEcho;
    /// <summary>An interdictor's beam is on it.</summary>
    public bool BeamOn => _thickened > BeamFloor;
    /// <summary>The beam's frequency, 0 to 255.</summary>
    public byte BeamBand { get; private set; }
    /// <summary>The generator's own frequency, 0 to 255.</summary>
    public float Tune { get; private set; } = 128;
    /// <summary>How much of the beam it is set to cancel, 0 to 1.</summary>
    public float Coupling { get; private set; }
    /// <summary>How near it is to giving up against the beam, 0 to 1.</summary>
    public float Strain { get; private set; }

    /// <summary>A frequency on the 0-255 band, in Hz.</summary>
    public static float Hertz(float band) => 0.5f + band / 255f * 3.5f;

    /// <summary>Seconds of charging left.</summary>
    public double ChargeLeft => _charge;
    /// <summary>The generator is pushing the ship, in place of the main engine.</summary>
    public bool Pushes => On;
    public float Push => S.Push;
    public float Megawatts => S.Megawatts;
    /// <summary>The contact it steers for, or null.</summary>
    public IRadio Course { get; private set; }

    private GeneratorSpec S => Spec ??= new GeneratorSpec();
    private double _charge;
    private const float BeamFloor = 0.05f, BeamGoneSeconds = 0.5f;
    private float _sinceBeam;
    private float _thickened;
    private float _tuneTo = 128;
    private float _beamPhase, _ownPhase;
    private byte _courseIndex = NoCourse;

    /// <summary>Adds to the medium's density round this ship (an interdictor's beam, on that frequency), for this step and a little after.</summary>
    public void Thicken(float density, byte band)
    {
        _thickened = Mathf.Max(_thickened, density);
        BeamBand = band;
        _sinceBeam = 0;
    }

    public void Rearm()
    {
        On = false;
        _charge = 0;
        Thin = 1;
        Wake = 0;
        DroppedOut = false;
        _thickened = 0;
        Strain = 0;
        Coupling = 0;
        ClearCourse();
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        float dt = (float)delta;
        float natural = MediumMass.Density(ship.GetWorld3D(), ship.GlobalPosition);
        Tune = Mathf.MoveToward(Tune, _tuneTo, S.TuneRate * dt);
        Coupling = BeamOn ? Mathf.Clamp((S.TuneBand - Mathf.Abs(Tune - BeamBand)) / Mathf.Max(S.TuneBand * (1 - S.TuneFlat), 0.01f), 0, 1) : 0;
        Density = natural + _thickened * (1 - Coupling * S.Rejection);
        Locked = Density >= S.Thinning;
        bool massed = natural >= S.Thinning;
        if (ship.Held)
            On = false;
        if (Charging)
        {
            _charge -= delta;
            if (Locked || ship.Held)
                _charge = 0;
            else if (_charge <= 0)
                On = true;
        }
        Strain = On && Locked && !massed
            ? Strain + dt / Mathf.Max(S.HoldSeconds, 0.01f)
            : Mathf.Max(0, Strain - dt / Mathf.Max(2 * S.HoldSeconds, 0.01f));
        if (On && (massed || Strain >= 1))
        {
            On = false;
            DroppedOut = true;
            Strain = 0;
        }
        _beamPhase = Mathf.Wrap(_beamPhase + Mathf.Tau * Hertz(BeamBand) * dt, 0, Mathf.Tau);
        _ownPhase = Mathf.Wrap(_ownPhase + Mathf.Tau * Hertz(Tune) * dt, 0, Mathf.Tau);
        Step(FullDrag(ship), (float)delta);
        Wake = Mathf.Clamp((1 - Thin) / (1 - 1 / S.Thinning), 0, 1) * Mathf.Clamp(ship.LinearVelocity.Length() / WakeFullAt, 0, 1);
        KeepCourse(ship);

        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus[Slot, 2] = (byte)((On ? StatusOn : 0) | (Charging ? StatusCharging : 0) |
                              (Locked ? StatusLocked : 0) | (DroppedOut ? StatusDropped : 0) | (BeamOn ? StatusBeam : 0));
        bus[Slot, 3] = (byte)Math.Clamp((Thin - 1 / S.Thinning) / (1 - 1 / S.Thinning) * 255, 0, 255);
        bus.SetWord(Slot, 4, (int)Math.Clamp(Density * 16, 0, 32767));
        bus.SetWord(Slot, 6, (int)Math.Clamp(ship.LinearVelocity.Length(), 0, 32767));
        bus[Slot, 7] = (byte)(ship.Controls.Cruise ? 1 : 0);
        bus[Slot, 9] = _courseIndex;
        bus[Slot, 10] = (byte)Mathf.RoundToInt(Tune);
        bus[Slot, 11] = (byte)((ship.Controls.TuneUp ? 1 : 0) | (ship.Controls.TuneDown ? 2 : 0));
        bus[Slot, 12] = (byte)Mathf.Clamp(Strain * 255, 0, 255);
        bus[Slot, 13] = (byte)Mathf.Clamp(Coupling * 255, 0, 255);
        bus[Slot, 14] = BeamOn ? unchecked((byte)(sbyte)(Mathf.Sin(_beamPhase) * 127)) : (byte)0;
        bus[Slot, 15] = unchecked((byte)(sbyte)(Mathf.Sin(_ownPhase) * 127));
        _sinceBeam += dt;
        _thickened = _sinceBeam > BeamGoneSeconds ? 0 : Mathf.Max(0, _thickened - 2 * dt * _thickened);
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (bus.TakeWritten(Slot, 5))
        {
            bool start = bus[Slot, 5] != 0;
            if (!start)
            {
                On = false;
                _charge = 0;
            }
            else if (!On && !Charging && !Locked && !ship.Held)
            {
                _charge = S.SpoolSeconds;
                DroppedOut = false;
            }
        }
        if (bus.TakeWritten(Slot, 10))
            _tuneTo = bus[Slot, 10];
        if (bus.TakeWritten(Slot, 8))
        {
            byte which = bus[Slot, 8];
            RadioDevice radio = ship.Device<RadioDevice>();
            IRadio chosen = radio != null && which < radio.Contacts.Count ? radio.Contacts[which] : null;
            if (chosen == null || ReferenceEquals(chosen, Course))
                ClearCourse();
            else
                Course = chosen;
        }
    }

    // How thin the veil is this step: thinning takes its time, and it comes back
    // no faster than the generator can bear
    private void Step(float fullDrag, float dt)
    {
        float wanted = On ? Mathf.Clamp(Density / S.Thinning, 1 / S.Thinning, 1) : 1;
        if (wanted < Thin)
        {
            Thin = Mathf.Max(wanted, Thin - Thin * dt / Mathf.Max(S.ThinSeconds, 0.01f));
            return;
        }
        float bearable = fullDrag > 0 ? S.BrakeAcceleration / fullDrag : 1;
        Thin = Mathf.Max(Thin, Mathf.Min(wanted, bearable));
    }

    // How hard the normal veil would brake the ship as it is moving now (m/s²): harder across the hull than along the nose
    private static float FullDrag(ShipBody ship)
    {
        Vector3 v = ship.GlobalBasis.Inverse() * ship.LinearVelocity;
        Vector3 k = ship.Spec.VeilAxes;
        return v.Length() * new Vector3(k.X * v.X, k.Y * v.Y, k.Z * v.Z).Length();
    }

    // The contact it steers for: its place in the table moves, and it can go out of hearing
    private void KeepCourse(ShipBody ship)
    {
        if (Course == null)
            return;
        RadioDevice radio = ship.Device<RadioDevice>();
        _courseIndex = NoCourse;
        for (int i = 0; radio != null && i < radio.Contacts.Count; i++)
            if (ReferenceEquals(radio.Contacts[i], Course))
                _courseIndex = (byte)i;
        if (_courseIndex == NoCourse)
            ClearCourse();
    }

    private void ClearCourse()
    {
        Course = null;
        _courseIndex = NoCourse;
    }
}
