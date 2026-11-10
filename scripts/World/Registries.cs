using System;
using System.Linq;

namespace Xpace.World;

/// <summary>
/// The rules of stations' registries and the false callsigns made of them, the same for a pilot at a terminal and a
/// person out of sight: what a copy gives, who is blamed for it, and a callsign that will pass.
/// </summary>
public static class Registries
{
    private static readonly Random Random = new();

    /// <summary>
    /// A copy of a station's registry by <paramref name="actor"/>: the tonnes it gives (between <paramref name="min"/> and
    /// <paramref name="max"/>, as much as <paramref name="room"/> allows), or 0 if it has nothing new for them yet.
    /// </summary>
    public static int Copy(StationRecord station, string actor, int min, int max, double freshHours, int room, double clock)
    {
        if (max <= 0 || room <= 0 || !Fresh(station, actor, freshHours, clock))
            return 0;
        station.RegistryCopied[actor] = clock;
        return Math.Min(room, Random.Next(min, max + 1));
    }

    /// <summary>The registry has something new for <paramref name="actor"/>: they never copied it, or long enough ago.</summary>
    public static bool Fresh(StationRecord station, string actor, double freshHours, double clock) =>
        !station.RegistryCopied.TryGetValue(actor, out double at) || clock - at >= freshHours * 3600;

    /// <summary>A false callsign that will still pass (on record, not found out, dockings left), or null.</summary>
    public static ForgedRecord Usable(UniverseState state, string callsign) =>
        state.Forged.TryGetValue(callsign ?? "", out ForgedRecord forged) && !forged.Burned && forged.Uses > 0 ? forged : null;

    /// <summary>One of the person's false callsigns that will still pass (those that won't are thrown away), or null.</summary>
    public static ForgedRecord Usable(UniverseState state, PersonRecord person)
    {
        person.Callsigns.RemoveAll(c => Usable(state, c) == null);
        return person.Callsigns.Select(c => Usable(state, c)).FirstOrDefault();
    }
}
