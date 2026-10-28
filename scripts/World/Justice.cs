using System;

namespace Xpace.World;

/// <summary>
/// The rules of a jurisdiction's record (<see cref="CrimeRecord"/>), the same for every station:
///
///   fines        owed; the pilot docks, trades and pays them as anyone;
///   a bounty     wanted: a docked pilot is held on the pad until it is paid (<see cref="Wanted"/>);
///   infamy       from destroying ships that had done nothing: while it is at <see cref="Notorious"/> or more, docking
///                is refused and the police attack the pilot on sight; it wears off at <see cref="DecayPerHour"/> an
///                hour of the pilot's played time (not the universe's: time away from the game doesn't count), and
///                paying doesn't take it away.
///
/// A pilot who can't pay what holds them surrenders: the cargo is seized (the market buys it, the money goes to the
/// debt), and if that isn't enough the ship is impounded where it is, kept free until the rest is paid; the insurer
/// gives the pilot a free courtesy ship meanwhile (<see cref="ShipRecord.CourtesyHull"/>).
/// </summary>
public static class Justice
{
    public const double Notorious = 1, DecayPerHour = 0.5, MostInfamy = 10;

    /// <summary>The pilot's infamy now.</summary>
    public static double Infamy(CrimeRecord record, PilotRecord pilot) =>
        Math.Max(0, record.Infamy - DecayPerHour * Math.Max(0, pilot.PlayedSeconds - record.InfamyAsOf) / 3600);

    public static void AddInfamy(CrimeRecord record, PilotRecord pilot, double amount)
    {
        record.Infamy = Math.Min(MostInfamy, Infamy(record, pilot) + amount);
        record.InfamyAsOf = pilot.PlayedSeconds;
    }

    public static bool IsNotorious(CrimeRecord record, PilotRecord pilot) => Infamy(record, pilot) >= Notorious;

    public static bool Wanted(CrimeRecord record) => record.Bounty > 0;

    /// <summary>Pays all the pilot owes here, if they have it.</summary>
    public static bool Pay(CrimeRecord record, PilotRecord pilot)
    {
        if (record.Owed == 0 || !pilot.Spend(record.Owed))
            return false;
        record.Fines = record.Bounty = 0;
        return true;
    }

    /// <summary>
    /// The pilot can't pay: <paramref name="sell"/> turns their cargo into credits (a market's prices), which go to
    /// the debt with whatever credits they have; if it still isn't paid, their ship is impounded at
    /// <paramref name="station"/> and they get the insurer's courtesy ship. Returns whether the ship was impounded.
    /// </summary>
    public static bool Surrender(StationRecord station, CrimeRecord record, PilotRecord pilot, Func<ShipRecord, long> sell)
    {
        pilot.Credits += sell(pilot.Ship);
        if (Pay(record, pilot))
            return false;
        // What they have goes to the debt: bounty first
        long toBounty = Math.Min(pilot.Credits, record.Bounty);
        record.Bounty -= toBounty;
        long toFines = Math.Min(pilot.Credits - toBounty, record.Fines);
        record.Fines -= toFines;
        pilot.Credits -= toBounty + toFines;
        if (record.Owed == 0)
            return false;
        station.Impounded[pilot.Id] = pilot.Ship;
        pilot.Ship = new ShipRecord { Hull = ShipRecord.CourtesyHull };
        return true;
    }

    /// <summary>All paid at the station that impounded the pilot's ship: it is theirs again (the courtesy ship goes back).</summary>
    public static bool Release(StationRecord station, CrimeRecord record, PilotRecord pilot)
    {
        if (record.Owed > 0 || !station.Impounded.Remove(pilot.Id, out ShipRecord ship))
            return false;
        foreach ((string good, int tonnes) in pilot.Ship.Cargo)
            ship.Load(good, tonnes);            // what the courtesy ship carried comes along
        pilot.Ship = ship;
        return true;
    }
}
