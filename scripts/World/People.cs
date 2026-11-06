using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// The free people: pilots on their own account, each with a name, money, a ship (a <see cref="CargoShipRecord"/>
/// owned by them) and memories. The same rules as anyone: they pay for what they get, they only know what they saw or
/// heard, they die. Hunters look for what is offered for heads at the stations they stand at, and go where the quarry
/// was last seen; with nothing worth it they trade a little. Pirates roam the ways after what others carry. Traders buy
/// where they heard it is cheap and sell where they heard it is dear, and go and look when they know nothing.
/// </summary>
public static class People
{
    /// <summary>Brings the population up to what it should be: at the start with ships of their own, later newcomers order one from a yard.</summary>
    public static void Muster(UniverseState state, PopulationSpec spec, StationBook book, Func<string, IAccount> account)
    {
        if (spec == null || book.Stations.Count == 0)
            return;
        for (int l = 0; l < spec.Lines.Length; l++)
        {
            PeopleLine line = spec.Lines[l];
            if (line?.Model == null)
                continue;
            int have = state.People.Values.Count(p => p.Line == l);
            for (; have < line.Count; have++)
            {
                if (state.PeopleMustered && state.Clock < state.NextArrival.GetValueOrDefault(l))
                    break;
                Arrive(state, spec, line, l, book, account);
                if (state.PeopleMustered)
                    state.NextArrival[l] = state.Clock + line.ReplaceHours * 3600;
            }
        }
        state.PeopleMustered = true;
    }

    private static void Arrive(UniverseState state, PopulationSpec spec, PeopleLine line, int index, StationBook book, Func<string, IAccount> account)
    {
        int serial = ++state.PeopleEver;
        var stations = book.Stations.Where(s => s.Pads.Any(p => book.Fits(p, line.Model))).ToList();
        if (stations.Count == 0)
            return;
        StationBook.Station home = stations[serial % stations.Count];
        var person = new PersonRecord
        {
            Id = PersonRecord.OwnerPrefix + serial, Line = index, Livelihood = line.Livelihood.ToString(),
            First = Pick(spec.FirstNames, serial, 7), Last = Pick(spec.Surnames, serial, 13),
            Character = line.Personalities.Length > 0 ? line.Personalities[serial % line.Personalities.Length].ResourcePath : "",
            MemoryRoom = line.MemoryRoom, Credits = state.PeopleMustered ? line.NewcomerSavings : line.Savings,
        };
        state.People[person.Id] = person;
        if (line.WantedBy != null && line.Reward > 0 && account(line.WantedBy.Id) is OrganisationRecord payer)
            payer.Bounties[person.Id] = line.Reward;
        if (state.PeopleMustered)
            CargoShips.OrderFor(person.Id, line.Model, home, state, book);
        else
            CargoShips.Enlist(person.Id, $"{person.Id}-ship", line.Model, home, state, book);
    }

    private static string Pick(string[] names, int serial, int stride) =>
        names.Length == 0 ? $"Pilot{serial}" : names[(int)((uint)(serial * stride + serial / Math.Max(names.Length, 1)) % names.Length)];

    /// <summary>A person's ship is gone: for now, so are they. What was on their head goes to whoever gets it.</summary>
    public static void Died(UniverseState state, string actor)
    {
        if (state.People.Remove(actor, out PersonRecord person) && CargoShips.Population is PopulationSpec spec && person.Line < spec.Lines.Length)
            state.NextArrival[person.Line] = Math.Max(state.NextArrival.GetValueOrDefault(person.Line), state.Clock + spec.Lines[person.Line].ReplaceHours * 3600);
        state.Sightings.Remove(actor);
    }

    /// <summary>Someone saw an actor there.</summary>
    public static void Seen(UniverseState state, string actor, string sector, Vector3 at)
    {
        if (!string.IsNullOrEmpty(actor))
            state.Sightings[actor] = new Sighting { Sector = sector, X = at.X, Y = at.Y, Z = at.Z, Clock = state.Clock };
    }

    /// <summary>What the person does next from a pad; false: nothing for now.</summary>
    public static bool Plan(CargoShipRecord ship, ShipModel model, PersonRecord person, PopulationSpec spec, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Find(ship.At) is not StationBook.Station here)
            return false;
        MarketNews.See(person, here, state, account);
        Fence(ship, here, person, spec, state, account);
        foreach (StationBook.Beacon beacon in book.Beacons.Where(b => b.Sector == here.Sector))
            MarketNews.Hear(person, beacon, state, book, account);
        Personality character = person.Personality;
        double fade = character?.MemoryHours ?? 48;
        string owner = CargoShips.OwnerOf(state, here);
        bool hunts = person.Livelihood == nameof(Livelihood.Hunter);
        person.Quarry = "";
        if (hunts && Worthwhile(person, spec, owner, state, account) is string quarry)
            person.Quarry = quarry;
        else
        {
            // Anyone goes after who wronged them, if their nature says so and the grudge is big enough
            (string actor, double feeling) = Memories.WorstGrudge(person, state.Clock, fade);
            if (actor != "" && -feeling * (character?.Aggression ?? 0.5) >= (spec?.GrudgeToHunt ?? 1))
                person.Quarry = actor;
        }
        if (person.Quarry != "" && state.Sightings.GetValueOrDefault(person.Quarry) is Sighting seen)
        {
            StationBook.Station nearest = book.Stations.Where(s => s.Sector == seen.Sector && s.Pads.Any(p => book.Fits(p, model))).OrderBy(s => s.At.DistanceTo(seen.At)).FirstOrDefault();
            if (nearest != null && nearest != here)
                return CargoShips.Hop(ship, model, nearest, state, book, account);
        }
        if (person.Livelihood == nameof(Livelihood.Pirate))
            return Roam(ship, model, here, person, state, book, account);
        return Trade(ship, model, here, person, state, book, account) || Roam(ship, model, here, person, state, book, account);
    }

    /// <summary>A ship of an owner with no station left to work for lives as a free trader does, for its owner.</summary>
    public static bool Wander(CargoShipRecord ship, ShipModel model, IAccount owner, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Find(ship.At) is not StationBook.Station here || owner == null)
            return false;
        MarketNews.See(owner, here, state, account);
        foreach (StationBook.Beacon beacon in book.Beacons.Where(b => b.Sector == here.Sector))
            MarketNews.Hear(owner, beacon, state, book, account);
        return Trade(ship, model, here, owner, state, book, account) || Roam(ship, model, here, owner, state, book, account);
    }

    // What it stole goes for less than a market pays: the rest stays with the station's owner
    private static void Fence(CargoShipRecord ship, StationBook.Station here, PersonRecord person, PopulationSpec spec, UniverseState state, Func<string, IAccount> account)
    {
        IAccount cash = account(CargoShips.OwnerOf(state, here));
        StationRecord record = CargoShips.Bring(state, here, account);
        foreach ((string good, int tonnes) in ship.Loot.ToList())
            if (here.Goods.FirstOrDefault(g => g.Good.Id == good) is MarketGood line)
            {
                long had = person.Credits;
                Market.Hand(record, line, tonnes, person, cash);
                long got = person.Credits - had;
                long kept = (long)(got * (1 - (spec?.FenceShare ?? 1)));
                person.Credits -= kept;
                if (cash != null)
                    cash.Credits += kept;
                ship.Loot.Remove(good);
            }
    }

    // The best offer on this station's owner's board that is worth a trip for someone they could hope to find
    private static string Worthwhile(PersonRecord person, PopulationSpec spec, string owner, UniverseState state, Func<string, IAccount> account)
    {
        long least = spec != null && person.Line < spec.Lines.Length ? spec.Lines[person.Line].LeastReward : 0;
        return account(owner) is OrganisationRecord board
            ? board.Bounties.Where(b => b.Value >= least && b.Key != person.Id && state.Sightings.ContainsKey(b.Key)).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).Select(b => b.Key).FirstOrDefault()
            : null;
    }

    // Where it has been least lately, of those it can reach and pay the way to
    private static bool Roam(CargoShipRecord ship, ShipModel model, StationBook.Station here, IAccount person, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        StationBook.Station next = book.Stations.Where(s => s != here && s.Pads.Any(p => book.Fits(p, model)) && book.Way(here.Sector, s.Sector) != null && person.Credits >= CargoShips.TollsBetween(book, here, s))
            .OrderBy(s => person.Known.GetValueOrDefault(s.Id)?.AsOf ?? 0).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        return next != null && CargoShips.Hop(ship, model, next, state, book, account);
    }

    // The best difference it knows of between two markets, for what its hold and its money can carry
    private static bool Trade(CargoShipRecord ship, ShipModel model, StationBook.Station here, IAccount person, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        int hold = (int)model.Hull.CargoCapacity;
        if (hold <= 0)
            return false;
        double since = state.Clock - model.NewsHours * 3600;
        (StationBook.Station From, StationBook.Station To, string Good, long Gain) best = default;
        foreach ((string fromId, StationNews fromNews) in person.Known.Where(k => k.Value.AsOf >= since).OrderBy(k => k.Key, StringComparer.Ordinal))
            foreach ((string toId, StationNews toNews) in person.Known.Where(k => k.Key != fromId && k.Value.AsOf >= since).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (book.Find(fromId) is not StationBook.Station from || book.Find(toId) is not StationBook.Station to || !from.Pads.Any(p => book.Fits(p, model)) || !to.Pads.Any(p => book.Fits(p, model)))
                    continue;
                long tolls = CargoShips.TollsBetween(book, here, from) + CargoShips.TollsBetween(book, from, to);
                foreach ((string good, GoodNews buy) in fromNews.Goods)
                {
                    if (buy.Buy <= 0 || buy.Stock < 1 || !toNews.Goods.TryGetValue(good, out GoodNews sell) || sell.Sell <= buy.Buy)
                        continue;
                    int tonnes = (int)Math.Min(hold, (person.Credits - tolls) / Math.Max(buy.Buy, 1));
                    long gain = tonnes * (sell.Sell - buy.Buy) - tolls;
                    if (gain > best.Gain)
                        best = (from, to, good, gain);
                }
            }
        if (best.From == null)
            return false;
        ship.Home = best.To.Id;
        return CargoShips.FetchFor(ship, model, best.Good, best.From, state, book, account);
    }

    /// <summary>
    /// Who is going for whom on the ways now, by where the records say they are: a grid for each sector, so it costs what
    /// the ships near each other cost.
    /// </summary>
    public static List<(CargoShipRecord Attacker, CargoShipRecord Victim)> Meetings(UniverseState state, StationBook book, float meters)
    {
        var meetings = new List<(CargoShipRecord, CargoShipRecord)>();
        var grids = new Dictionary<string, SpatialGrid<CargoShipRecord>>();
        var fixes = new Dictionary<CargoShipRecord, StationBook.Fix>();
        foreach (CargoShipRecord ship in state.CargoShips.Values)
            if (ship.Phase == CargoShipRecord.Flying && ResourceLoader.Exists(ship.Model) && GD.Load<ShipModel>(ship.Model) is ShipModel model && book.FixOf(ship, model, state.Clock) is StationBook.Fix fix)
            {
                fixes[ship] = fix;
                if (!grids.TryGetValue(fix.Sector, out SpatialGrid<CargoShipRecord> grid))
                    grids[fix.Sector] = grid = new SpatialGrid<CargoShipRecord>(meters);
                grid.Add(ship, fix.At);
            }
        var near = new List<CargoShipRecord>();
        foreach ((CargoShipRecord ship, StationBook.Fix fix) in fixes)
        {
            if (!state.People.TryGetValue(ship.Owner, out PersonRecord person))
                continue;
            near.Clear();
            grids[fix.Sector].Within(fix.At, meters, near);
            CargoShipRecord prey = near.Where(o => o != ship && o.Owner != ship.Owner && Goes(person, o, state)).OrderBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();
            if (prey != null)
                meetings.Add((ship, prey));
        }
        return meetings;
    }

    // Its quarry, or for a pirate whoever carries something; hunters and pirates also go for pirates with a price on them
    private static bool Goes(PersonRecord person, CargoShipRecord other, UniverseState state)
    {
        if (person.Quarry != "" && other.Owner == person.Quarry)
            return true;
        if (person.Livelihood == nameof(Livelihood.Pirate))
            return other.Tonnes > 0;
        return person.Livelihood == nameof(Livelihood.Hunter) && state.People.GetValueOrDefault(other.Owner)?.Livelihood == nameof(Livelihood.Pirate);
    }
}
