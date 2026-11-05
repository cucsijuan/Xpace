using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>
/// The universe's work, whether anyone is looking or not: every station brought up to date, and every cargo ship
/// (<see cref="CargoShipRecord"/>) doing its job by the same rules as a pilot. A ship works for its home station, for
/// its owner: when the home runs short of something it uses it goes to the cheapest maker of it that has some, pays
/// the market's prices and the tolls out of its owner's money, and brings it back. Voyages take what the ship's real
/// numbers say. It asks each station for a pad as it comes near: the nearest free one that fits; none free, it holds
/// there and asks again. It stands on that pad and leaves from it. Only the universe's owner runs it.
/// </summary>
public static class CargoShips
{
    /// <summary>Gives each station the fleet it starts with, once: after that, ships are ordered and built.</summary>
    public static void Muster(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (StationBook.Station station in book.Stations)
        {
            StationRecord record = state.Station(station.Id);
            if (record.Mustered)
                continue;
            record.Mustered = true;
            for (int line = 0; line < (station.Fleet?.Length ?? 0); line++)
                for (int n = 0; station.Fleet[line]?.Model != null && !station.Fleet[line].WhenThreatened && n < station.Fleet[line].Count; n++)
                {
                    string id = $"{station.Id}-{line + 1}-{n + 1}";
                    if (state.CargoShips.ContainsKey(id))
                        continue;
                    var ship = new CargoShipRecord
                    {
                        Id = id, Model = station.Fleet[line].Model.ResourcePath, Owner = OwnerOf(state, station), Home = station.Id, At = station.Id,
                        NextLook = state.Clock + n * 60,
                    };
                    state.CargoShips[id] = ship;
                    // From the far end of the station's pads: the first hangars stay open for those who come
                    if (station.Pads.LastOrDefault(p => book.Fits(p, station.Fleet[line].Model) && Free(station, p, state)) is StationBook.Pad pad)
                        Take(ship, station, pad, state);
                }
        }
    }

    /// <summary>The free people as they are set up, for what they do.</summary>
    public static PopulationSpec Population;

    /// <summary>A ship of someone's own, on a pad at a station, from the start.</summary>
    public static void Enlist(string owner, string id, ShipModel model, StationBook.Station at, UniverseState state, StationBook book)
    {
        var ship = new CargoShipRecord { Id = id, Model = model.ResourcePath, Owner = owner, Home = at.Id, At = at.Id, NextLook = state.Clock };
        state.CargoShips[id] = ship;
        if (at.Pads.LastOrDefault(p => book.Fits(p, model) && Free(at, p, state)) is StationBook.Pad pad)
            Take(ship, at, pad, state);
    }

    /// <summary>Someone orders a ship of their own, to stand at a station, from the nearest yard that builds it.</summary>
    public static void OrderFor(string owner, ShipModel model, StationBook.Station home, UniverseState state, StationBook book)
    {
        StationBook.Station yard = book.Stations.Where(s => s.Shipyard?.Contains(model) == true && book.Way(home.Sector, s.Sector) != null).OrderBy(s => book.Way(home.Sector, s.Sector).Count).FirstOrDefault();
        if (yard == null)
            return;
        string id = $"{owner}-ship";
        state.ShipOrders[id] = new ShipOrderRecord { Id = id, Model = model.ResourcePath, Owner = owner, Home = home.Id, Yard = yard.Id };
    }

    /// <summary>It flies to a station with nothing to do there but be there.</summary>
    public static bool Hop(CargoShipRecord ship, ShipModel model, StationBook.Station to, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        ship.Home = to.Id;
        ship.State = CargoShipRecord.Returning;
        Depart(ship, model, to, state, book, account);
        return true;
    }

    /// <summary>It goes to fetch a good, for its <see cref="CargoShipRecord.Home"/>, as a working ship does.</summary>
    public static bool FetchFor(CargoShipRecord ship, ShipModel model, string good, StationBook.Station from, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        Fetch(ship, model, good, from, state, book, account);
        return true;
    }

    /// <summary>What the gates between two stations ask.</summary>
    public static long TollsBetween(StationBook book, StationBook.Station from, StationBook.Station to) => Tolls(book, from, to);

    /// <summary>A ship is gone, and what it carried with it: its owner has one less, and its pad is free. A person's only ship takes them with it.</summary>
    public static void Lose(UniverseState state, string id)
    {
        Release(state, id);
        if (state.CargoShips.GetValueOrDefault(id)?.Owner is string owner && state.People.ContainsKey(owner))
            People.Died(state, owner);
        if (!state.CargoShips.Remove(id, out CargoShipRecord ship) || ship.Dock == 0)
            return;
        state.Station(ship.Phase == CargoShipRecord.Landing ? ship.Going : ship.At).CargoPads.Remove(Key(ship.Dock, ship.Pad));
    }

    // Its escorts have done with it
    private static void Release(UniverseState state, string ward)
    {
        foreach (CargoShipRecord escort in state.CargoShips.Values.Where(s => s.Ward == ward))
            escort.Ward = "";
    }

    private static ShipModel ModelOf(CargoShipRecord ship) => ResourceLoader.Exists(ship.Model) ? GD.Load<ShipModel>(ship.Model) : null;

    // An owner short of the fleet a station needs orders what is missing from the nearest yard that builds it
    private static void Order(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (StationBook.Station station in book.Stations)
            foreach (FleetLine line in station.Fleet ?? Array.Empty<FleetLine>())
            {
                if (line?.Model == null)
                    continue;
                string owner = OwnerOf(state, station);
                if (line.WhenThreatened && !Reactions.Threatened(account(owner) as OrganisationRecord, OrganisationCatalog.Find(owner)?.Personality, state.Clock))
                    continue;
                string model = line.Model.ResourcePath;
                int have = state.CargoShips.Values.Count(s => s.Home == station.Id && s.Model == model) + state.ShipOrders.Values.Count(o => o.Home == station.Id && o.Model == model);
                StationBook.Station yard = book.Stations.Where(s => s.Shipyard?.Contains(line.Model) == true && book.Way(station.Sector, s.Sector) != null)
                    .OrderBy(s => book.Way(station.Sector, s.Sector).Count).ThenBy(s => s.At.DistanceTo(station.At)).FirstOrDefault();
                for (; yard != null && have < line.Count; have++)
                {
                    StationRecord record = state.Station(station.Id);
                    string id = $"{station.Id}-R{++record.Ordered}";
                    var wanted = line.Fitting.Count > 0 ? line.Fitting : line.Model.Hull.Fitting;
                    state.ShipOrders[id] = new ShipOrderRecord
                    {
                        Id = id, Model = model, Owner = OwnerOf(state, station), Home = station.Id, Yard = yard.Id,
                        Fittings = wanted.Where(f => f.Value != null).ToDictionary(f => f.Key, f => f.Value.ResourcePath),
                    };
                }
            }
    }

    // A yard starts on an order when it has the materials and the buyer the money, and hands the ship over on a free pad
    private static void Build(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (ShipOrderRecord order in state.ShipOrders.Values.OrderBy(o => o.Id, StringComparer.Ordinal).ToList())
        {
            if (book.Find(order.Yard) is not StationBook.Station yard || !ResourceLoader.Exists(order.Model) || GD.Load<ShipModel>(order.Model) is not ShipModel model || model.Recipe == null)
                continue;
            if (order.Ready == 0)
                Start(order, yard, model, state, book, account);
            else if (state.Clock >= order.Ready && yard.Pads.FirstOrDefault(p => book.Fits(p, model) && Free(yard, p, state)) is StationBook.Pad pad)
            {
                var ship = new CargoShipRecord
                {
                    Id = order.Id, Model = order.Model, Owner = order.Owner, Home = order.Home, At = yard.Id, NextLook = state.Clock,
                    State = order.Home == yard.Id ? CargoShipRecord.Waiting : CargoShipRecord.Returning, Fittings = order.Fittings,
                };
                state.CargoShips[ship.Id] = ship;
                Take(ship, yard, pad, state);
                state.ShipOrders.Remove(order.Id);
            }
        }
    }

    // The yard buys what the buyer wants fitted from whoever makes it: no pieces, no start. So what the buyers choose is what the arms makers sell
    private static void Start(ShipOrderRecord order, StationBook.Station yard, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        StationRecord record = state.Station(yard.Id);
        double cost = 0;
        foreach (RecipePart part in model.Recipe.Inputs)
        {
            if (yard.Goods.FirstOrDefault(g => g.Good.Id == Recipe.IdOf(part.Item)) is not MarketGood line || record.Stock.GetValueOrDefault(line.Good.Id) < part.Amount)
                return;
            cost += part.Amount * Market.Price(line, record.Stock[line.Good.Id]);
        }
        var pieces = new List<(StationBook.Station Maker, OutfitLine Line, long Price)>();
        foreach (string path in order.Fittings.Values)
        {
            var sold = book.Stations.SelectMany(s => (s.Outfits ?? Array.Empty<OutfitLine>()).Where(o => o?.Item?.ResourcePath == path).Select(o => (Maker: s, Line: o)))
                .Select(o => (o.Maker, o.Line, Price: Market.OutfitPrice(Bring(state, o.Maker, account), o.Line) ))
                .Where(o => o.Price >= 0 && state.Station(o.Maker.Id).Stock.GetValueOrDefault(o.Line.Id) >= 1 + pieces.Count(p => p.Line == o.Line))
                .OrderBy(o => o.Price).ToList();
            if (sold.Count == 0)
                return;
            pieces.Add(sold[0]);
        }
        IAccount buyer = account(order.Owner), builder = account(OwnerOf(state, yard));
        long price = (long)Math.Ceiling(cost * (1 + (builder?.Margin ?? 0))) + model.BuildFee;
        long total = (ReferenceEquals(buyer, builder) ? 0 : price) + pieces.Sum(p => p.Price);
        if (buyer == null || buyer.Credits < total)
            return;
        if (!ReferenceEquals(buyer, builder))
        {
            buyer.Credits -= price;
            if (builder != null)
                builder.Credits += price;
        }
        foreach ((StationBook.Station maker, OutfitLine line, long paid) in pieces)
        {
            buyer.Credits -= paid;
            if (account(OwnerOf(state, maker)) is IAccount seller)
                seller.Credits += paid;
            state.Station(maker.Id).Stock[line.Id] -= 1;
        }
        foreach (RecipePart part in model.Recipe.Inputs)
            record.Stock[Recipe.IdOf(part.Item)] -= part.Amount;
        order.Ready = state.Clock + model.BuildHours * 3600;
    }

    // How it calls itself on the radio
    private static string Sign(CargoShipRecord ship) => $"{ModelOf(ship)?.Name} {CallsignOf(ship.Id)}";

    /// <summary>A ship's callsign: short, its own, and the same on every machine.</summary>
    public static string CallsignOf(string id)
    {
        uint hash = 2166136261;                 // FNV-1a: string.GetHashCode differs between runs
        foreach (char c in id)
            hash = (hash ^ c) * 16777619;
        return $"FR-{hash % 65536:X4}";
    }

    /// <summary>The owner of a station now.</summary>
    public static string OwnerOf(UniverseState state, StationBook.Station station)
    {
        StationRecord record = state.Station(station.Id);
        if (string.IsNullOrEmpty(record.Owner))
            record.Owner = station.Owner;
        return record.Owner;
    }

    /// <summary>Brings everything up to the universe's clock.</summary>
    public static void Tick(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (StationBook.Station station in book.Stations)
            Bring(state, station, account);
        foreach (CargoShipRecord ship in state.CargoShips.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            Work(ship, state, book, account);
        Order(state, book, account);
        Build(state, book, account);
    }

    public static StationRecord Bring(UniverseState state, StationBook.Station station, Func<string, IAccount> account)
    {
        StationRecord record = state.Station(station.Id);
        Market.Advance(record, station.Goods, state.Clock, station.Outfits, station.Factories, account(OwnerOf(state, station)), station.Upkeep);
        return record;
    }

    private static void Work(CargoShipRecord ship, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (!ResourceLoader.Exists(ship.Model) || GD.Load<ShipModel>(ship.Model) is not ShipModel model || model.Hull == null || model.Generator == null)
            return;
        switch (ship.Phase)
        {
            case CargoShipRecord.Parked:
                if (state.Clock < ship.NextLook)
                    return;
                if (model.Escort)
                {
                    // Left with nobody to guard, it goes home
                    if (ship.Ward == "" && ship.At != ship.Home)
                        Depart(ship, model, book.Find(ship.Home), state, book, account);
                    return;
                }
                if (ship.State == CargoShipRecord.Returning)
                    Depart(ship, model, book.Find(ship.Home), state, book, account);
                else if (state.People.TryGetValue(ship.Owner, out PersonRecord person))
                {
                    if (!People.Plan(ship, model, person, Population, state, book, account))
                        ship.NextLook = state.Clock + model.WaitSeconds;
                }
                else if (!Plan(ship, model, state, book, account))
                    ship.NextLook = state.Clock + model.WaitSeconds;
                return;
            case CargoShipRecord.Flying:
                Interdiction.Work(ship, model, state, book);
                if (state.Clock >= ship.Arrives)
                {
                    ship.Phase = CargoShipRecord.Holding;
                    ship.NextLook = state.Clock;
                }
                return;
            case CargoShipRecord.Holding:
                if (state.Clock >= ship.NextLook)
                    Ask(ship, model, state, book);
                return;
            case CargoShipRecord.Landing:
                if (state.Clock >= ship.Arrives)
                    Arrive(ship, model, state, book, account);
                return;
        }
    }

    /// <summary>Whether a pad is free: no cargo ship has it, and no pilot was cleared to it.</summary>
    private static bool Free(StationBook.Station station, StationBook.Pad pad, UniverseState state) =>
        !Taken(state, station.Id, pad.Dock, pad.Number) && Xpace.Space.StationRadio.Called(station.Id)?.PilotOn(pad.Dock, pad.Number) != true;

    /// <summary>A cargo ship has that pad, or has only just left it.</summary>
    public static bool Taken(UniverseState state, string station, int dock, int pad)
    {
        StationRecord record = state.Station(station);
        string key = Key(dock, pad);
        return record.CargoPads.ContainsKey(key) || record.PadsResting.GetValueOrDefault(key) > state.Clock;
    }

    public static string Key(int dock, int pad) => $"{dock}:{pad}";

    // The station gives it the free pad nearest to it that fits; none free, it holds there and asks again
    private static void Ask(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book)
    {
        if (book.Find(ship.Going) is not StationBook.Station station)
            return;
        Vector3 at = ship.ContactPoint;
        StationBook.Pad pad = station.Pads.Where(p => book.Fits(p, model) && Free(station, p, state)).OrderBy(p => p.Door.DistanceTo(at)).ThenBy(p => p.Pose.Origin.DistanceTo(at)).FirstOrDefault();
        Xpace.Space.StationRadio radio = Xpace.Space.StationRadio.Called(station.Id);
        if (pad == null)
        {
            if (!ship.AskedHold)
                radio?.Traffic(Sign(ship), radio.TrafficAskText, radio.TrafficAskSpeech, radio.TrafficHoldText, radio.TrafficHoldSpeech);
            ship.AskedHold = true;
            ship.NextLook = state.Clock + model.AskEverySeconds;
            return;
        }
        radio?.Traffic(Sign(ship), ship.AskedHold ? null : radio.TrafficAskText, radio.TrafficAskSpeech, radio.TrafficClearedText, radio.TrafficClearedSpeech, pad.Dock, pad.Number);
        ship.AskedHold = false;
        Take(ship, station, pad, state);
        ship.Phase = CargoShipRecord.Landing;
        ship.Left = state.Clock;
        ship.Arrives = state.Clock + book.Landing(station, pad, at, model, ship.Tonnes).Sum(l => l.Seconds);
    }

    private static void Take(CargoShipRecord ship, StationBook.Station station, StationBook.Pad pad, UniverseState state)
    {
        state.Station(station.Id).CargoPads[Key(pad.Dock, pad.Number)] = ship.Id;
        ship.Dock = pad.Dock;
        ship.Pad = pad.Number;
    }

    // Down on its pad: what it came for is done there, then it stays for as long as its crew takes
    private static void Arrive(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        Passed(ship, model, state, book, account);
        ship.At = ship.Going;
        ship.PaceModel = "";
        ship.Phase = CargoShipRecord.Parked;
        ship.NextLook = state.Clock + model.HandlingSeconds;
        if (ship.State == CargoShipRecord.Fetching)
        {
            Load(ship, model, state, book, account);
            ship.State = CargoShipRecord.Returning;
        }
        else
        {
            Unload(ship, state, book, account);
            ship.State = CargoShipRecord.Waiting;
            Release(state, ship.Id);
        }
        if (book.Find(ship.At) is StationBook.Station here)
        {
            MarketNews.See(account(ship.Owner), here, state, account);
            if (state.People.ContainsKey(ship.Owner))
                People.Seen(state, ship.Owner, here.Sector, here.At);
        }
    }

    // The crew tells its owner what the beacons it flew past had to say
    private static void Passed(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Way(ship, model, out _) is not List<StationBook.Leg> way)
            return;
        foreach (StationBook.Beacon beacon in book.BeaconsOn(way))
            MarketNews.Hear(account(ship.Owner), beacon, state, book, account);
    }

    // How much of a good the home wants to keep: what it uses up, and what the ships ordered from it are waiting for
    private static double Wanted(MarketGood line, StationBook.Station home, ShipModel model, UniverseState state)
    {
        string good = line.Good.Id;
        double wanted = Market.Net(good, home.Goods, home.Factories) < 0 ? line.Stock * model.RestockBelow : 0;
        foreach (ShipOrderRecord order in state.ShipOrders.Values.Where(o => o.Yard == home.Id && o.Ready == 0))
            if (ResourceLoader.Exists(order.Model) && GD.Load<ShipModel>(order.Model)?.Recipe is Recipe recipe)
                wanted += recipe.Inputs.Where(p => Recipe.IdOf(p.Item) == good).Sum(p => p.Amount);
        return wanted;
    }

    // What the home is shortest of, and who sells it cheapest
    private static bool Plan(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Find(ship.Home) is not StationBook.Station home)
            return false;
        StationRecord record = state.Station(home.Id);
        IAccount money = account(ship.Owner);
        bool escorted = EscortFor(ship, state) != null;
        foreach ((MarketGood line, double wanted) in home.Goods.Select(g => (Line: g, Wanted: Wanted(g, home, model, state))).Where(g => g.Wanted > 0)
                     .OrderBy(g => record.Stock.GetValueOrDefault(g.Line.Good.Id) / g.Wanted))
        {
            string good = line.Good.Id;
            double stock = record.Stock.GetValueOrDefault(good);
            if (stock >= wanted)
                break;
            double need = 1 - stock / wanted;
            var makers = book.Stations.Where(s => s != home && book.Way(home.Sector, s.Sector) != null && s.Goods.Any(g => g.Good.Id == good)
                                                  && Market.Net(good, s.Goods, s.Factories) > 0 && !Feared(book, model, home, s, state.Clock, money, escorted ? 1 : need))
                .Select(s => (Station: s, News: Heard(ship, model, s, good, state, account))).ToList();
            foreach (var seller in makers.Where(s => s.News != null && s.News.Stock * model.TakeShare >= 1).OrderBy(s => s.News.Buy))
            {
                long price = seller.News.Buy;
                long tolls = Tolls(book, home, seller.Station) * 2;
                // Dearer than it is worth at home isn't worth fetching: what's made of it wouldn't sell
                if (price < 0 || price > Market.Price(line, stock) || money == null || money.Credits < price + tolls)
                    continue;
                Fetch(ship, model, good, seller.Station, state, book, account);
                return true;
            }
            // Nothing known that serves: the nearest maker nobody has news of is worth a look
            foreach (var maker in makers.Where(s => s.News == null).OrderBy(s => book.Seconds(home, s.Station, model, 0)))
                if (money != null && money.Credits >= Tolls(book, home, maker.Station) * 2)
                {
                    Fetch(ship, model, good, maker.Station, state, book, account);
                    return true;
                }
        }
        return false;
    }

    // The owner lost a ship on the way there and won't send another yet
    private static bool Feared(StationBook book, ShipModel model, StationBook.Station home, StationBook.Station to, double clock, IAccount owner, double need) =>
        owner is OrganisationRecord { Incidents.Count: > 0 } record &&
        Reactions.Avoids(record, OrganisationCatalog.Find(record.Id)?.Personality, book.Departure(home, home.Pads.FirstOrDefault(), to, model, 0, out _), model.DangerMeters, clock, need);

    private static void Fetch(CargoShipRecord ship, ShipModel model, string good, StationBook.Station from, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        ship.Good = good;
        ship.Tonnes = 0;
        ship.State = CargoShipRecord.Fetching;
        Depart(ship, model, from, state, book, account);
    }

    // What the ship's owner knows of that good there: its own station as it is, another's as last heard, null if that's no use
    private static GoodNews Heard(CargoShipRecord ship, ShipModel model, StationBook.Station station, string good, UniverseState state, Func<string, IAccount> account)
    {
        if (OwnerOf(state, station) == ship.Owner)
            return MarketNews.Report(station, Bring(state, station, account), state.Clock).Goods.GetValueOrDefault(good);
        return account(ship.Owner)?.Known.GetValueOrDefault(station.Id) is StationNews news && state.Clock - news.AsOf <= model.NewsHours * 3600
            ? news.Goods.GetValueOrDefault(good) : null;
    }

    private static long Tolls(StationBook book, StationBook.Station from, StationBook.Station to) =>
        book.Way(from.Sector, to.Sector)?.Sum(g => g.Toll) ?? 0;

    // Off from its pad, cleared to leave: the tolls are paid to the gates' owners as it sets out, and the pad is free for others
    private static void Depart(CargoShipRecord ship, ShipModel model, StationBook.Station to, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (to == null || book.Find(ship.At) is not StationBook.Station from)
            return;
        IAccount money = account(ship.Owner);
        PayTolls(from, to, money, book, account);
        if (Xpace.Space.StationRadio.Called(from.Id) is Xpace.Space.StationRadio radio)
            radio.Traffic(Sign(ship), radio.TrafficLaunchAskText, radio.TrafficLaunchAskSpeech, radio.TrafficLaunchText, radio.TrafficLaunchSpeech);
        StationRecord leaving = state.Station(from.Id);
        if (leaving.CargoPads.Remove(Key(ship.Dock, ship.Pad)))
            leaving.PadsResting[Key(ship.Dock, ship.Pad)] = state.Clock + model.PadRestSeconds;
        ship.From = from.Id;
        ship.FromSector = "";
        ship.LeftDock = ship.Dock;
        ship.LeftPad = ship.Pad;
        ship.Dock = ship.Pad = 0;
        ship.Going = to.Id;
        ship.Phase = CargoShipRecord.Flying;
        ship.Left = ship.WorkedTo = state.Clock;
        ship.Legs = null;
        ship.Pace = -1;
        ship.Generator = new Xpace.Ship.GeneratorState { On = true };
        ship.Arrives = state.Clock + book.Way(ship, model, out Vector3 contact).Sum(l => l.Seconds + l.Transit);
        ship.ContactPoint = contact;
        if (!model.Escort && EscortFor(ship, state, from.Id) is CargoShipRecord escort && ModelOf(escort) is ShipModel guard)
        {
            escort.Ward = ship.Id;
            escort.PaceModel = ship.Model;
            escort.PaceTonnes = ship.Tonnes;
            escort.State = CargoShipRecord.Escorting;
            Depart(escort, guard, to, state, book, account);
        }
    }

    // An escort of the ship's owner standing by where it is, its own first
    private static CargoShipRecord EscortFor(CargoShipRecord ship, UniverseState state, string at = null) =>
        state.CargoShips.Values.Where(s => s.Owner == ship.Owner && s.At == (at ?? ship.At) && s.Phase == CargoShipRecord.Parked && (s.Ward == ship.Id || s.Ward == "") && ModelOf(s)?.Escort == true)
            .OrderBy(s => s.Ward == ship.Id ? 0 : 1).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();

    // The tolls are paid to the gates' owners as it sets out
    private static void PayTolls(StationBook.Station from, StationBook.Station to, IAccount money, StationBook book, Func<string, IAccount> account)
    {
        foreach (StationBook.Gate gate in book.Way(from.Sector, to.Sector) ?? new List<StationBook.Gate>())
            if (money != null && money.Credits >= gate.Toll)
            {
                money.Credits -= gate.Toll;
                if (account(gate.Owner) is IAccount keeper)
                    keeper.Credits += gate.Toll;
            }
    }

    /// <summary>
    /// A ship flown by hand goes back on its record from where it is: an escort to its ward's destination, or home; a
    /// cargo ship on to where it was going, its tolls paid already.
    /// </summary>
    public static void Resume(CargoShipRecord ship, ShipModel model, string sector, Vector3 at, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        CargoShipRecord ward = state.CargoShips.GetValueOrDefault(ship.Ward);
        StationBook.Station to = book.Find(!model.Escort ? ship.Going : ward == null ? ship.Home : ward.Phase == CargoShipRecord.Parked ? ward.At : ward.Going);
        if (to == null)
            return;
        ship.From = "";
        ship.FromSector = sector;
        (ship.FromX, ship.FromY, ship.FromZ) = (at.X, at.Y, at.Z);
        ship.LeftDock = ship.LeftPad = 0;
        ship.PaceModel = "";
        ship.Going = to.Id;
        ship.Phase = CargoShipRecord.Flying;
        ship.Left = ship.WorkedTo = state.Clock;
        ship.Legs = null;
        ship.Pace = -1;
        ship.Generator = new Xpace.Ship.GeneratorState { On = true };
        if (model.Escort)
            PayTolls(book.Origin(ship), to, account(ship.Owner), book, account);
        ship.Arrives = state.Clock + book.Way(ship, model, out Vector3 contact).Sum(l => l.Seconds + l.Transit);
        ship.ContactPoint = contact;
    }

    private static void Load(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        StationBook.Station seller = book.Find(ship.At), home = book.Find(ship.Home);
        if (seller == null || home == null)
            return;
        StationRecord record = Bring(state, seller, account);
        StationRecord homeRecord = Bring(state, home, account);
        MarketGood line = seller.Goods.FirstOrDefault(g => g.Good.Id == ship.Good);
        MarketGood wanted = home.Goods.FirstOrDefault(g => g.Good.Id == ship.Good);
        // The news was old, or it went to look: what it finds may not be worth buying
        long price = line != null ? Market.BuyPrice(record, line) : -1;
        if (wanted != null && price >= 0 && price <= Market.Price(wanted, homeRecord.Stock.GetValueOrDefault(ship.Good)))
        {
            double room = Market.Full * wanted.Stock - homeRecord.Stock.GetValueOrDefault(ship.Good);
            int tonnes = (int)Math.Min(Math.Min(model.Hull.CargoCapacity, room), record.Stock.GetValueOrDefault(ship.Good) * model.TakeShare);
            ship.Tonnes = Market.Take(record, line, account(ship.Owner), tonnes, account(OwnerOf(state, seller)));
        }
    }

    private static void Unload(CargoShipRecord ship, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Find(ship.Home) is StationBook.Station home && home.Goods.FirstOrDefault(g => g.Good.Id == ship.Good) is MarketGood line)
        {
            StationRecord record = Bring(state, home, account);
            Market.Hand(record, line, ship.Tonnes, account(ship.Owner), account(OwnerOf(state, home)));
            ship.Delivered += ship.Tonnes;
        }
        ship.Tonnes = 0;
        ship.Good = "";
    }
}
