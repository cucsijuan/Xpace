using System;
using Godot;
using Xpace.Computer;

namespace Xpace.Ship;

/// <summary>
/// The shield generator, bus slot 12 (1C0h): a field round the ship that
/// takes the hits before the hull does, up to <see cref="Strength"/>
/// points; what gets past it goes on to the <see cref="ShipHull"/>. It
/// builds back <see cref="RechargePerSecond"/> points a second, paying
/// <see cref="MegajoulesPerPoint"/> for each from the systems capacitor
/// (<see cref="PowerDevice"/>): with that dry, only as fast as the power
/// coming in. Knocked down to nothing it collapses, and starts again from
/// nothing after <see cref="CollapseSeconds"/>. Hits on it flash the
/// <see cref="Bubble"/>, if the ship has one.
///
/// Registers: +0 type 13  +2 strength, 0 to 255 full  +3 status (bit 0 up,
/// bit 1 building back, bit 2 collapsed)  +4 quarter seconds until it
/// starts again (collapsed)
/// </summary>
public partial class ShieldDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 12;
    public const byte Type = 13;

    [Export] public float Strength = 150f;
    [Export] public float RechargePerSecond = 8f;
    [Export] public float MegajoulesPerPoint = 0.5f;
    [Export] public double CollapseSeconds = 8;
    /// <summary>Optional: what shows the field (a mesh round the ship), seen when it is hit.</summary>
    [Export] public NodePath Bubble;
    [Export] public float FlashSeconds = 0.4f;

    public float Points { get; private set; }
    public bool Collapsed => _collapsedFor > 0;
    public float Fraction => Points / Math.Max(1e-6f, Strength);

    /// <summary>All the damage it has taken off the hull.</summary>
    public float Absorbed { get; private set; }

    private double _collapsedFor;
    private float _flash;
    private GeometryInstance3D _bubble;
    private PowerDevice _power;
    private bool _building;

    public override void _Ready()
    {
        _bubble = Bubble == null || Bubble.IsEmpty ? null : GetNodeOrNull<GeometryInstance3D>(Bubble);
        if (_bubble != null)
            _bubble.Visible = false;
        foreach (Node n in GetParent().GetChildren())
            _power ??= n as PowerDevice;
        Rearm();
    }

    public void Rearm()
    {
        Points = Strength;
        _collapsedFor = 0;
    }

    /// <summary>A hit of <paramref name="damage"/> on the ship: what the field doesn't stop, for the hull.</summary>
    public float Absorb(float damage)
    {
        if (damage <= 0 || Collapsed || Points <= 0)
            return damage;
        float taken = Math.Min(Points, damage);
        Points -= taken;
        Absorbed += taken;
        _flash = FlashSeconds;
        if (Points <= 0)
        {
            Points = 0;
            _collapsedFor = CollapseSeconds;
        }
        return damage - taken;
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        bus[Slot, 2] = (byte)Math.Round(Fraction * 255);
        bus[Slot, 3] = (byte)((Points > 0 && !Collapsed ? 1 : 0) | (_building ? 2 : 0) | (Collapsed ? 4 : 0));
        bus[Slot, 4] = (byte)Math.Min(255, Math.Ceiling(_collapsedFor * 4));
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        _building = false;
        if (Collapsed)
        {
            _collapsedFor -= delta;
            return;
        }
        float wanted = Math.Min(Strength - Points, RechargePerSecond * (float)delta);
        if (wanted <= 0)
            return;
        double paid = _power?.DrawUpTo(PowerStore.Systems, wanted * MegajoulesPerPoint) ?? wanted * MegajoulesPerPoint;
        Points += (float)(paid / MegajoulesPerPoint);
        _building = paid > 0;
    }

    public override void _Process(double delta)
    {
        if (_bubble == null)
            return;
        _flash = Math.Max(0, _flash - (float)delta);
        _bubble.Visible = _flash > 0;
        _bubble.Transparency = 1 - _flash / FlashSeconds * 0.8f;
    }
}
