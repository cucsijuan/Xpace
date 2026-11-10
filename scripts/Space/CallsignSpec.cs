using System;
using Godot;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// A false callsign, as a pirate base sells it (assets/outfits): a registration made out of stations' registries,
/// carried by the pilot. Sent by a ship's radio through a program that changes what it says (SPOOF), stations take it
/// as a ship of its own, clean, for <see cref="Uses"/> dockings, until a scan finds the hull isn't it.
/// </summary>
[GlobalClass]
public partial class CallsignSpec : Outfit
{
    /// <summary>Dockings it is good for.</summary>
    [Export] public int Uses = 5;

    public override string About => "A clean registration, for a ship's radio to send in place of its own.";
    public override string Details => $"Good for {Uses} dockings, or until a scan finds you out.";
    public override bool Carried => true;

    private static readonly Random Random = new();

    public override ItemRecord MakeItem(UniverseState state)
    {
        string callsign;
        do
            callsign = $"{(char)('A' + Random.Next(26))}{(char)('A' + Random.Next(26))}-{Random.Next(1000, 10000)}";
        while (state.Forged.ContainsKey(callsign));
        state.Forged[callsign] = new ForgedRecord { Callsign = callsign, Uses = Uses };
        return new ItemRecord { Kind = ItemRecord.Callsign, Name = callsign, Data = ResourcePath };
    }

    public override bool Makes(ItemRecord item) => item.Kind == ItemRecord.Callsign && item.Data == ResourcePath;
}
