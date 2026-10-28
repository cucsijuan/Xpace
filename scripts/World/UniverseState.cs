using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xpace.World;

/// <summary>
/// Everything the game keeps of the universe between plays, as plain data: no nodes, nothing of Godot's, keyed by
/// ids that outlive any scene (a pilot's id, a station's callsign). Whoever owns the universe (the host; alone, the
/// player's own machine) holds it and changes it; the scene's nodes read and write it through <see cref="Universe"/>.
///
/// It is the universe's whole state, so that it can grow into one: each kind of thing has its own records, each
/// record stamped with the universe time it was last brought up to date (<see cref="StationRecord.UpdatedAt"/>), so a
/// simulation can later advance what nobody is looking at from there to now, lazily, the first time someone does.
/// <see cref="UniverseStore"/> keeps it on disk as a folder of documents, one for each record.
/// </summary>
public sealed class UniverseState
{
    /// <summary>The documents' format; older ones are brought up to it as they load (<see cref="UniverseStore"/>).</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Universe time: seconds the universe has run. It runs while anyone plays in it.</summary>
    public double Clock { get; set; }

    public Dictionary<string, PilotRecord> Pilots { get; set; } = new();
    public Dictionary<string, StationRecord> Stations { get; set; } = new();

    /// <summary>A pilot's record, made the first time the pilot comes (with what a new pilot has).</summary>
    public PilotRecord Pilot(string id, string name = null)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (!Pilots.TryGetValue(id, out PilotRecord pilot))
        {
            Pilots[id] = pilot = new PilotRecord { Id = id, Name = name ?? id, Credits = PilotRecord.StartingCredits };
            pilot.Items.Add(ItemRecord.InsurersBackup());
        }
        else if (!string.IsNullOrEmpty(name))
            pilot.Name = name;
        return pilot;
    }

    /// <summary>A station's record, made the first time it is needed.</summary>
    public StationRecord Station(string callsign)
    {
        if (string.IsNullOrEmpty(callsign))
            return null;
        if (!Stations.TryGetValue(callsign, out StationRecord station))
            Stations[callsign] = station = new StationRecord { Callsign = callsign, UpdatedAt = Clock };
        return station;
    }
}

/// <summary>A pilot: a player, wherever they play from, known by an id of their own (<see cref="Universe.LocalPilotId"/>).</summary>
public sealed class PilotRecord
{
    /// <summary>What a new pilot has to start with.</summary>
    public const long StartingCredits = 1000;

    public string Id { get; set; }
    public string Name { get; set; }
    public long Credits { get; set; }

    /// <summary>Seconds played in this universe: the clock that infamy wears off by.</summary>
    public double PlayedSeconds { get; set; }

    /// <summary>The ship the pilot flies.</summary>
    public ShipRecord Ship { get; set; } = new();

    /// <summary>What the pilot carries on them (at most <see cref="InventorySlots"/> things).</summary>
    public List<ItemRecord> Items { get; set; } = new();

    public const int InventorySlots = 8;

    /// <summary>Takes <paramref name="amount"/> if the pilot has it.</summary>
    public bool Spend(long amount)
    {
        if (amount < 0 || Credits < amount)
            return false;
        Credits -= amount;
        return true;
    }
}

/// <summary>Something a pilot carries on them or keeps in a station's locker: diskettes now, later weapons and more.</summary>
public sealed class ItemRecord
{
    public const string Diskette = "diskette";

    public string Kind { get; set; } = Diskette;
    public string Name { get; set; } = "";
    /// <summary>What the kind needs to know of it (a diskette: the image it holds).</summary>
    public string Data { get; set; } = "";

    /// <summary>The diskette the insurer gives every pilot: the ship's software as it came from the factory.</summary>
    public static ItemRecord InsurersBackup() => new() { Kind = Diskette, Name = "Insurer's backup", Data = "factory" };
}

/// <summary>A pilot's ship: its hull and what its hold carries.</summary>
public sealed class ShipRecord
{
    /// <summary>The hull every new pilot is given.</summary>
    public const string StarterHull = "res://assets/ship/hulls/hull_fighter.tres";

    /// <summary>The insurer's free basic ship, for a pilot whose own was impounded.</summary>
    public const string CourtesyHull = "res://assets/ship/hulls/hull_courtesy.tres";

    /// <summary>The hull's spec (a <see cref="Xpace.Ship.ShipSpec"/> resource's path).</summary>
    public string Hull { get; set; } = StarterHull;

    /// <summary>Tonnes of each good aboard, by the good's id (<see cref="Good.Id"/>).</summary>
    public Dictionary<string, int> Cargo { get; set; } = new();

    [JsonIgnore] public int Tonnes
    {
        get
        {
            int tonnes = 0;
            foreach (int t in Cargo.Values)
                tonnes += t;
            return tonnes;
        }
    }

    public int Holds(string good) => Cargo.TryGetValue(good, out int t) ? t : 0;

    /// <summary>Adds (or with a negative count takes away) tonnes of a good; none left of it, it goes from the list.</summary>
    public void Load(string good, int tonnes)
    {
        int now = Holds(good) + tonnes;
        if (now > 0)
            Cargo[good] = now;
        else
            Cargo.Remove(good);
    }
}

/// <summary>A station: what it holds against each pilot, and later its market and lockers.</summary>
public sealed class StationRecord
{
    public string Callsign { get; set; }

    /// <summary>The universe time this station was last brought up to date.</summary>
    public double UpdatedAt { get; set; }

    /// <summary>Tonnes of each good its market has (by the good's id), as of <see cref="UpdatedAt"/>.</summary>
    public Dictionary<string, double> Stock { get; set; } = new();

    /// <summary>Each pilot's locker here (by the pilot's id): what they left at this station.</summary>
    public Dictionary<string, List<ItemRecord>> Lockers { get; set; } = new();

    public List<ItemRecord> LockerOf(string pilot)
    {
        if (!Lockers.TryGetValue(pilot, out List<ItemRecord> locker))
            Lockers[pilot] = locker = new List<ItemRecord>();
        return locker;
    }

    /// <summary>Ships impounded here for debts their pilots couldn't pay (by the pilot's id), kept free until paid.</summary>
    public Dictionary<string, ShipRecord> Impounded { get; set; } = new();

    /// <summary>What the station holds against each pilot (by the pilot's id): its jurisdiction.</summary>
    public Dictionary<string, CrimeRecord> Crimes { get; set; } = new();

    public CrimeRecord CrimesOf(string pilot)
    {
        if (!Crimes.TryGetValue(pilot, out CrimeRecord record))
            Crimes[pilot] = record = new CrimeRecord();
        return record;
    }
}

/// <summary>
/// What one jurisdiction holds against one pilot (<see cref="Justice"/> has the rules). Money owed never goes away by
/// itself; infamy wears off with the pilot's played time.
/// </summary>
public sealed class CrimeRecord
{
    /// <summary>Fines owed (credits): speeding, firing in the zone. Owed, not wanted.</summary>
    public long Fines { get; set; }

    /// <summary>The bounty on the pilot (credits): damaging or destroying a ship that had done nothing. Wanted till paid.</summary>
    public long Bounty { get; set; }

    /// <summary>Infamy as it was at <see cref="InfamyAsOf"/> of the pilot's played time.</summary>
    public double Infamy { get; set; }
    public double InfamyAsOf { get; set; }

    [JsonIgnore] public long Owed => Fines + Bounty;
    [JsonIgnore] public bool Clean => Fines == 0 && Bounty == 0 && Infamy <= 0;
}
