using System;
using System.Collections.Generic;
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

    /// <summary>How many dockings a station's logbook keeps.</summary>
    public const int LogbookSize = 32;

    /// <summary>A ship docked at the station, under the callsign it gave: into its logbook.</summary>
    public static void Log(StationRecord station, string callsign, double clock, int dock, int pad)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return;
        station.Logbook.Add(new LogEntry { Callsign = callsign.Trim(), At = clock, Dock = dock, Pad = pad });
        if (station.Logbook.Count > LogbookSize)
            station.Logbook.RemoveRange(0, station.Logbook.Count - LogbookSize);
    }

    /// <summary>
    /// The seed of what a station's registry core holds for an actor now: the same for host and terminal, new once the
    /// actor's copy is old (<paramref name="copiedAt"/>, below 0 never).
    /// </summary>
    public static int Seed(string station, string actor, double copiedAt)
    {
        uint hash = 2166136261;                 // FNV-1a: string.GetHashCode differs between runs
        foreach (char c in $"{station}/{actor}/{Math.Floor(copiedAt)}")
            hash = (hash ^ c) * 16777619;
        return (int)(hash & 0x7FFFFFFF);
    }

    /// <summary>The service key of a station's terminals for that seed: <paramref name="length"/> hex digits.</summary>
    public static string Key(int seed, int length)
    {
        var random = new Random(seed ^ 0x5EC7E7);
        return new string(Enumerable.Range(0, Math.Clamp(length, 1, 16)).Select(_ => "0123456789ABCDEF"[random.Next(16)]).ToArray());
    }

    /// <summary>The bytes of a record in a registry core.</summary>
    public const int RecordBytes = 32;
    public const byte Live = 0x01, Deleted = 0xE5;

    /// <summary>
    /// A registry core: the signatures of the ships registered at the station, <paramref name="records"/> live records
    /// of 32 bytes ("RG", flag 01, class, callsign, signature, checksum: the sum of the 31 bytes before it), the first
    /// of them the ships it knows (<paramref name="known"/>, its logbook), among deleted ones (flag E5), records half
    /// overwritten and plain noise, each starting on a 16-byte row. The callsigns of the live ones, by their offset.
    /// </summary>
    public static (byte[] Core, Dictionary<int, string> Records) Core(int seed, int records, IEnumerable<string> known = null)
    {
        var real = new Queue<string>((known ?? Array.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(records));
        var random = new Random(seed);
        var kinds = new List<int>();
        for (int i = 0; i < records; i++)
            kinds.Add(0);
        for (int i = 0; i < records; i++)
            kinds.Add(1 + random.Next(2));          // deleted or broken
        for (int i = 0; i < records * 2; i++)
            kinds.Add(3);                           // noise, a row
        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        var core = new List<byte>();
        var live = new Dictionary<int, string>();
        foreach (int kind in kinds)
        {
            if (kind == 3)
            {
                core.AddRange(Noise(random, 16));
                continue;
            }
            string sign = kind == 0 && real.Count > 0 ? real.Dequeue() : Callsign(random);
            byte[] record = Record(random, sign, kind == 1 ? Deleted : Live);
            if (kind == 2)
                for (int k = 0, at = 4 + random.Next(6); k < 4; k++)
                    record[at + k] = (byte)random.Next(1, 32);      // overwritten across the callsign
            if (kind == 0)
                live[core.Count] = sign;
            core.AddRange(record);
        }
        return (core.ToArray(), live);
    }

    private static string Callsign(Random random) =>
        $"{(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))}-{random.Next(10000):D4}";

    private static byte[] Record(Random random, string sign, byte flag)
    {
        var r = new byte[RecordBytes];
        r[0] = (byte)'R';
        r[1] = (byte)'G';
        r[2] = flag;
        r[3] = (byte)"CFPTM"[random.Next(5)];
        for (int i = 0; i < 8; i++)
            r[4 + i] = (byte)(i < sign.Length ? char.ToUpperInvariant(sign[i]) : ' ');
        for (int i = 12; i < RecordBytes - 1; i++)
            r[i] = (byte)random.Next(256);
        int sum = 0;
        for (int i = 0; i < RecordBytes - 1; i++)
            sum += r[i];
        r[RecordBytes - 1] = (byte)sum;
        return r;
    }

    // Memory nobody cleared: mostly binary, now and then a run of letters
    private static byte[] Noise(Random random, int n)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++)
            b[i] = random.Next(5) == 0 ? (byte)('A' + random.Next(26)) : (byte)random.Next(256);
        if (random.Next(4) == 0)
        {
            b[random.Next(n - 1)] = (byte)'R';
        }
        return b;
    }

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
