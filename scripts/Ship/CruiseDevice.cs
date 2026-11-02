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
/// </summary>
public partial class CruiseDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 18;
    public const byte Type = 20, Version = 1;
    public const byte NoCourse = 0xFF;
    private const float WakeFullAt = 500f;      // m/s
    public const byte StatusOn = 1, StatusCharging = 2, StatusLocked = 4, StatusDropped = 8;

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
    private float _thickened;
    private byte _courseIndex = NoCourse;

    /// <summary>Adds to the medium's density round this ship (an interdictor's beam), for this step and a little after.</summary>
    public void Thicken(float density) => _thickened = Mathf.Max(_thickened, density);

    public void Rearm()
    {
        On = false;
        _charge = 0;
        Thin = 1;
        Wake = 0;
        DroppedOut = false;
        _thickened = 0;
        ClearCourse();
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        Density = MediumMass.Density(ship.GetWorld3D(), ship.GlobalPosition) + _thickened;
        _thickened = Mathf.Max(0, _thickened - 2 * (float)delta * _thickened);
        Locked = Density >= S.Thinning;
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
        if (On && Locked)
        {
            On = false;
            DroppedOut = true;
        }
        Step(FullDrag(ship), (float)delta);
        Wake = Mathf.Clamp((1 - Thin) / (1 - 1 / S.Thinning), 0, 1) * Mathf.Clamp(ship.LinearVelocity.Length() / WakeFullAt, 0, 1);
        KeepCourse(ship);

        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus[Slot, 2] = (byte)((On ? StatusOn : 0) | (Charging ? StatusCharging : 0) |
                              (Locked ? StatusLocked : 0) | (DroppedOut ? StatusDropped : 0));
        bus[Slot, 3] = (byte)Math.Clamp((Thin - 1 / S.Thinning) / (1 - 1 / S.Thinning) * 255, 0, 255);
        bus.SetWord(Slot, 4, (int)Math.Clamp(Density * 16, 0, 32767));
        bus.SetWord(Slot, 6, (int)Math.Clamp(ship.LinearVelocity.Length(), 0, 32767));
        bus[Slot, 7] = (byte)(ship.Controls.Cruise ? 1 : 0);
        bus[Slot, 9] = _courseIndex;
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
