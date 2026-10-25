using System;
using Godot;
using Xpace.Computer;

namespace Xpace.Ship;

/// <summary>
/// The ship's heat, bus slot 13 (1D0h): what its systems always give
/// (<see cref="BaseMegawatts"/>), what the power plant wastes
/// (<see cref="PowerDevice.WasteHeat"/>), what the weapons leave in the
/// ship, warms its mass; the radiators give it off, the more the hotter
/// it is (<see cref="RadiatorMegawatts"/> at 100%). 100% is
/// <see cref="Megajoules"/> of heat: past it the ship cooks, its hull
/// wearing <see cref="OverheatDamagePerSecond"/> at 150% (from nothing at
/// 100%); shields don't help. The hull glows on a heat seeker the hotter it
/// is (<see cref="ShipSignature.HullHeatAtFull"/>).
///
/// A heat sink (<see cref="HeatSinks"/> of them) soaks heat up at
/// <see cref="SinkMegawatts"/> for <see cref="SinkSeconds"/>, and is thrown
/// away with it: the ship goes cold, for a while.
///
/// Registers: +0 type 14  +2 heat, in percent (to 255)  +3 heat sinks left
/// +4 write: bit 0 a heat sink  +5 status (bit 0 a sink working, bit 1 over 100%)
/// </summary>
public partial class HeatDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 13;
    public const byte Type = 14;

    [Export] public float Megajoules = 60f;
    [Export] public float BaseMegawatts = 1.2f;
    [Export] public float RadiatorMegawatts = 6f;
    [Export] public float OverheatDamagePerSecond = 10f;
    [Export] public int HeatSinks = 2;
    [Export] public float SinkMegawatts = 25f;
    [Export] public double SinkSeconds = 8;
    /// <summary>Where it starts (and a new ship), 0 to 1.</summary>
    [Export] public float StartLevel = 0.2f;

    /// <summary>The heat, 1 at 100%.</summary>
    public float Level => (float)(_heat / Megajoules);
    public int SinksLeft { get; private set; }
    public bool Sinking => _sinkFor > 0;

    private double _heat, _sinkFor, _pending;
    private PowerDevice _power;
    private ShipHull _hull;

    public override void _Ready()
    {
        foreach (Node n in GetParent().GetChildren())
        {
            _power ??= n as PowerDevice;
            _hull ??= n as ShipHull;
        }
        Rearm();
    }

    public void Rearm()
    {
        _heat = StartLevel * Megajoules;
        SinksLeft = HeatSinks;
        _sinkFor = 0;
    }

    /// <summary>Heat put into the ship (a laser pulse), MJ.</summary>
    public void Add(double megajoules) => _pending += Math.Max(0, megajoules);

    /// <summary>A heat sink, if one is left and none is working.</summary>
    public bool Sink()
    {
        if (SinksLeft <= 0 || Sinking)
            return false;
        SinksLeft--;
        _sinkFor = SinkSeconds;
        return true;
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        bus[Slot, 2] = (byte)Math.Clamp(Math.Round(Level * 100), 0, 255);
        bus[Slot, 3] = (byte)SinksLeft;
        bus[Slot, 5] = (byte)((Sinking ? 1 : 0) | (Level > 1 ? 2 : 0));
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (bus.TakeWritten(Slot, 4) && (bus[Slot, 4] & 1) != 0)
            Sink();
        Step(delta);
    }

    /// <summary>The heat in, the heat out, and what too much does.</summary>
    public void Step(double delta)
    {
        _heat += _pending + (_power?.HeatMade ?? 0) + BaseMegawatts * delta;
        _pending = 0;
        _heat -= RadiatorMegawatts * Level * delta;
        if (Sinking)
        {
            _heat -= Math.Min(_heat, SinkMegawatts * delta);
            _sinkFor -= delta;
        }
        _heat = Math.Max(0, _heat);
        if (Level > 1)
            _hull?.Burn(OverheatDamagePerSecond * (Level - 1) / 0.5f * (float)delta);
    }
}
