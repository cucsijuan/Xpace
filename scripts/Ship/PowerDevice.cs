using System;
using Godot;
using Xpace.Computer;

namespace Xpace.Ship;

/// <summary>The three stores the power distributor fills.</summary>
public enum PowerStore { Systems, Engines, Weapons }

/// <summary>
/// The power plant and its distributor, bus slot 10 (1A0h). The plant gives
/// up to <see cref="PlantMegawatts"/>; the distributor shares that among
/// three capacitors, systems (the shields), engines (the thrusters) and
/// weapons (the laser), by the program's setting: twelve half pips, up to
/// eight on one (Elite's four pips each way). A capacitor fills at its share
/// of the plant while it has room, and empties as what hangs off it draws:
/// a share too small for what draws on it runs dry, and then the shields
/// come back slowly, the thrusters push only as hard as the power coming in
/// lets them, the laser waits. The plant makes only what is drawn; of that,
/// <see cref="WasteHeat"/> goes to heat (<see cref="HeatDevice"/>).
///
/// Registers: +0 type 12  +2 systems  +3 engines  +4 weapons (half pips,
/// 0 to 8; writing all three, ending with +4, sets them, if they add up to
/// 12)  +6 +7 +8 the capacitors, 0 to 255 full
/// </summary>
public partial class PowerDevice : Node, IShipDevice, IRearmable
{
    public const int Slot = 10;
    public const byte Type = 12;
    public const int HalfPips = 12, MostHalfPips = 8;

    [Export] public float PlantMegawatts = 20f;
    [Export] public float SystemsMegajoules = 30f;
    [Export] public float EnginesMegajoules = 30f;
    [Export] public float WeaponsMegajoules = 30f;
    [Export(PropertyHint.Range, "0,1")] public float WasteHeat = 0.35f;

    /// <summary>Half pips: systems, engines, weapons.</summary>
    public int[] Pips { get; } = { 4, 4, 4 };

    /// <summary>What each capacitor holds now, MJ.</summary>
    public double[] Charge { get; } = new double[3];

    /// <summary>Power drawn from the plant in the last step, MW.</summary>
    public double Drawn { get; private set; }

    /// <summary>Heat the plant made in the last step, MJ (taken by the ship's heat).</summary>
    public double HeatMade { get; private set; }

    public float Capacity(PowerStore store) => store switch
    {
        PowerStore.Systems => SystemsMegajoules,
        PowerStore.Engines => EnginesMegajoules,
        _ => WeaponsMegajoules,
    };

    /// <summary>How full a capacitor is, 0 to 1.</summary>
    public float Fill(PowerStore store) => (float)(Charge[(int)store] / Math.Max(1e-6, Capacity(store)));

    public override void _Ready() => Rearm();

    public void Rearm()
    {
        for (int i = 0; i < 3; i++)
            Charge[i] = Capacity((PowerStore)i);
    }

    /// <summary>Sets the half pips if they make a setting (12 in all, at most 8 on one).</summary>
    public bool Set(int systems, int engines, int weapons)
    {
        if (systems + engines + weapons != HalfPips || Math.Max(systems, Math.Max(engines, weapons)) > MostHalfPips ||
            Math.Min(systems, Math.Min(engines, weapons)) < 0)
            return false;
        (Pips[0], Pips[1], Pips[2]) = (systems, engines, weapons);
        return true;
    }

    /// <summary>Takes <paramref name="megajoules"/> from a capacitor if it holds that much; else takes nothing.</summary>
    public bool Draw(PowerStore store, double megajoules)
    {
        if (Charge[(int)store] < megajoules)
            return false;
        Charge[(int)store] -= megajoules;
        return true;
    }

    /// <summary>Takes up to <paramref name="megajoules"/> from a capacitor: what it got.</summary>
    public double DrawUpTo(PowerStore store, double megajoules)
    {
        double got = Math.Clamp(megajoules, 0, Charge[(int)store]);
        Charge[(int)store] -= got;
        return got;
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        for (int i = 0; i < 3; i++)
        {
            bus[Slot, 2 + i] = (byte)Pips[i];
            bus[Slot, 6 + i] = (byte)Math.Round(Fill((PowerStore)i) * 255);
        }
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus.TakeWritten(Slot, 2);
        bus.TakeWritten(Slot, 3);
        if (bus.TakeWritten(Slot, 4))
            Set(bus[Slot, 2], bus[Slot, 3], bus[Slot, 4]);
        Supply(delta);
    }

    /// <summary>The plant fills each capacitor at its share, as far as it has room.</summary>
    private void Supply(double delta)
    {
        double drawn = 0;
        for (int i = 0; i < 3; i++)
        {
            double room = Capacity((PowerStore)i) - Charge[i];
            double got = Math.Min(room, PlantMegawatts * Pips[i] / HalfPips * delta);
            Charge[i] += got;
            drawn += got;
        }
        Drawn = drawn / delta;
        HeatMade = drawn * WasteHeat;
    }
}
