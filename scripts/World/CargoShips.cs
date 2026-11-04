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
    /// <summary>Makes the ships of the stations' fleets that the universe doesn't have yet.</summary>
    public static void Muster(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (StationBook.Station station in book.Stations)
            for (int line = 0; line < (station.Fleet?.Length ?? 0); line++)
                for (int n = 0; station.Fleet[line]?.Model != null && n < station.Fleet[line].Count; n++)
                {
                    string id = $"{station.Sign}-{line + 1}-{n + 1}";
                    if (state.CargoShips.ContainsKey(id))
                        continue;
                    var ship = new CargoShipRecord
                    {
                        Id = id, Model = station.Fleet[line].Model.ResourcePath, Owner = OwnerOf(state, station), Home = station.Sign, At = station.Sign,
                        NextLook = state.Clock + n * 60,
                    };
                    state.CargoShips[id] = ship;
                    // From the far end of the station's pads: the first hangars stay open for those who come
                    if (station.Pads.LastOrDefault(p => book.Fits(p, station.Fleet[line].Model) && Free(station, p, state)) is StationBook.Pad pad)
                        Take(ship, station, pad, state);
                }
    }

    // How it calls itself on the radio
    private static string Sign(CargoShipRecord ship) => $"Freighter {ship.Id}";

    /// <summary>The owner of a station now.</summary>
    public static string OwnerOf(UniverseState state, StationBook.Station station)
    {
        StationRecord record = state.Station(station.Sign);
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
    }

    private static StationRecord Bring(UniverseState state, StationBook.Station station, Func<string, IAccount> account)
    {
        StationRecord record = state.Station(station.Sign);
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
                if (ship.State == CargoShipRecord.Returning)
                    Depart(ship, model, book.Find(ship.Home), state, book, account);
                else if (!Plan(ship, model, state, book, account))
                    ship.NextLook = state.Clock + model.WaitSeconds;
                return;
            case CargoShipRecord.Flying:
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
        !Taken(state, station.Sign, pad.Dock, pad.Number) && Xpace.Space.StationRadio.Called(station.Sign)?.PilotOn(pad.Dock, pad.Number) != true;

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
        Xpace.Space.StationRadio radio = Xpace.Space.StationRadio.Called(station.Sign);
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
        state.Station(station.Sign).CargoPads[Key(pad.Dock, pad.Number)] = ship.Id;
        ship.Dock = pad.Dock;
        ship.Pad = pad.Number;
    }

    // Down on its pad: what it came for is done there, then it stays for as long as its crew takes
    private static void Arrive(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        ship.At = ship.Going;
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
        }
    }

    // What the home is shortest of, and who sells it cheapest
    private static bool Plan(CargoShipRecord ship, ShipModel model, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (book.Find(ship.Home) is not StationBook.Station home)
            return false;
        StationRecord record = state.Station(home.Sign);
        IAccount money = account(ship.Owner);
        foreach (MarketGood line in home.Goods.Where(g => Market.Net(g.Good.Id, home.Goods, home.Factories) < 0)
                     .OrderBy(g => record.Stock.GetValueOrDefault(g.Good.Id) / Math.Max(g.Stock, 1)))
        {
            string good = line.Good.Id;
            double stock = record.Stock.GetValueOrDefault(good);
            if (stock >= line.Stock * model.RestockBelow)
                break;
            var sellers = book.Stations.Where(s => s != home && book.Way(home.Sector, s.Sector) != null && s.Goods.Any(g => g.Good.Id == good)
                                                   && Market.Net(good, s.Goods, s.Factories) > 0)
                .Select(s => (Station: s, Line: s.Goods.First(g => g.Good.Id == good), Record: Bring(state, s, account)))
                .Where(s => s.Record.Stock.GetValueOrDefault(good) * model.TakeShare >= 1)
                .OrderBy(s => Market.BuyPrice(s.Record, s.Line));
            foreach (var seller in sellers)
            {
                long price = Market.BuyPrice(seller.Record, seller.Line);
                long tolls = Tolls(book, home, seller.Station) * 2;
                // Dearer than it is worth at home isn't worth fetching: what's made of it wouldn't sell
                if (price < 0 || price > Market.Price(line, stock) || money == null || money.Credits < price + tolls)
                    continue;
                ship.Good = good;
                ship.Tonnes = 0;
                ship.State = CargoShipRecord.Fetching;
                Depart(ship, model, seller.Station, state, book, account);
                return true;
            }
        }
        return false;
    }

    private static long Tolls(StationBook book, StationBook.Station from, StationBook.Station to) =>
        book.Way(from.Sector, to.Sector)?.Sum(g => g.Toll) ?? 0;

    // Off from its pad, cleared to leave: the tolls are paid to the gates' owners as it sets out, and the pad is free for others
    private static void Depart(CargoShipRecord ship, ShipModel model, StationBook.Station to, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (to == null || book.Find(ship.At) is not StationBook.Station from)
            return;
        IAccount money = account(ship.Owner);
        foreach (StationBook.Gate gate in book.Way(from.Sector, to.Sector) ?? new List<StationBook.Gate>())
            if (money != null && money.Credits >= gate.Toll)
            {
                money.Credits -= gate.Toll;
                if (account(gate.Owner) is IAccount keeper)
                    keeper.Credits += gate.Toll;
            }
        if (Xpace.Space.StationRadio.Called(from.Sign) is Xpace.Space.StationRadio radio)
            radio.Traffic(Sign(ship), radio.TrafficLaunchAskText, radio.TrafficLaunchAskSpeech, radio.TrafficLaunchText, radio.TrafficLaunchSpeech);
        StationRecord leaving = state.Station(from.Sign);
        if (leaving.CargoPads.Remove(Key(ship.Dock, ship.Pad)))
            leaving.PadsResting[Key(ship.Dock, ship.Pad)] = state.Clock + model.PadRestSeconds;
        ship.From = from.Sign;
        ship.LeftDock = ship.Dock;
        ship.LeftPad = ship.Pad;
        ship.Dock = ship.Pad = 0;
        ship.Going = to.Sign;
        ship.Phase = CargoShipRecord.Flying;
        ship.Left = state.Clock;
        ship.Arrives = state.Clock + book.Departure(from, StationBook.PadOf(from, ship.LeftDock, ship.LeftPad), to, model, ship.Tonnes, out Vector3 contact).Sum(l => l.Seconds + l.Transit);
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
        if (line != null && wanted != null)
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
