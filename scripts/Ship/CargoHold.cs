using System.Linq;
using Godot;
using Xpace.Computer;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>
/// The ship's hold, bus slot 17 (210h): the cargo racks its hull has (<see cref="ShipSpec.CargoRacks"/>) and what
/// they carry, a tonne of anything weighing a tonne (<see cref="ShipBody.CargoMass"/>: a laden ship is slower to
/// push and to turn, and crashes harder). A player's cargo is in the universe, with the pilot
/// (<see cref="ShipRecord.Cargo"/>): the hold shows the owner's record (on a guest, the copy the host sent), and
/// cargo is bought and sold through the universe's owner, never here. Any other ship carries its own
/// (<see cref="Carried"/>). Cargo goes with the ship when it blows up.
///
/// Registers: +0 type 19  +1 version 1  +2 capacity (t, word)  +4 carried (t, word)  +6 kinds of good aboard
/// +8 segment of the list (D000h)  +10 its offset (<see cref="ShipMemory.Cargo"/>)
/// The list: up to <see cref="MaxKinds"/> entries of 16 bytes: the good's name (12 characters, upper case, padded
/// with spaces), its tonnes (word), 2 spare.
/// </summary>
public partial class CargoHold : Node, IShipDevice
{
    public const int Slot = 17;
    public const byte Type = 19, Version = 1;
    public const int MaxKinds = 16, EntrySize = 16, NameLength = 12;
    public const float KilogramsPerTonne = 1000f;

    /// <summary>What a ship no player flies carries.</summary>
    public ShipRecord Carried { get; } = new();

    private ShipBody _ship;

    public override void _Ready()
    {
        _ship = GetParentOrNull<ShipBody>();
        foreach (ShipHull hull in _ship?.GetChildren().OfType<ShipHull>() ?? Enumerable.Empty<ShipHull>())
            hull.Destroyed += () => Universe.Current?.Lose(_ship);
    }

    public int Capacity => _ship?.Spec?.CargoCapacity ?? 0;

    /// <summary>What the hold carries: the pilot's record, or its own.</summary>
    public ShipRecord Contents => Universe.Current?.RecordOf(_ship)?.Ship ?? Carried;

    public int Free => Capacity - Contents.Tonnes;

    public unsafe void BeforeStep(ShipBody ship, double delta)
    {
        ShipRecord contents = Contents;
        ship.CargoMass = contents.Tonnes * KilogramsPerTonne;
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = Version;
        bus.SetWord(Slot, 2, (short)Capacity);
        bus.SetWord(Slot, 4, (short)contents.Tonnes);
        bus.SetWord(Slot, 8, unchecked((short)ShipMemory.Segment));
        bus.SetWord(Slot, 10, ShipMemory.Cargo);
        byte* list = ship.Computer.Memory + ShipMemory.Base + ShipMemory.Cargo;
        int kinds = 0;
        foreach ((string id, int tonnes) in contents.Cargo.OrderBy(c => c.Key, System.StringComparer.Ordinal))
        {
            if (kinds == MaxKinds)
                break;
            byte* entry = list + kinds++ * EntrySize;
            string name = (GoodsCatalog.Find(id)?.Name ?? id).ToUpperInvariant().PadRight(NameLength);
            for (int i = 0; i < NameLength; i++)
                entry[i] = name[i] is >= ' ' and <= '~' ? (byte)name[i] : (byte)'?';
            entry[12] = (byte)tonnes;
            entry[13] = (byte)(tonnes >> 8);
            entry[14] = entry[15] = 0;
        }
        bus[Slot, 6] = (byte)kinds;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
    }
}
