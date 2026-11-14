using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>Something an owner has put up for sale: its asking price falls with time to its floor.</summary>
public sealed class SaleRecord
{
    public const string Ship = "ship", Station = "station";

    public string Id { get; set; }
    public string Kind { get; set; } = Ship;
    /// <summary>The ship's or the station's id.</summary>
    public string Item { get; set; } = "";
    /// <summary>Who sells it (empty: nobody: an abandoned station).</summary>
    public string Seller { get; set; } = "";
    public long Asking { get; set; }
    public long Floor { get; set; }
    public double Since { get; set; }
    /// <summary>How long the price takes to reach its floor, seconds.</summary>
    public double Falls { get; set; }

    public long PriceAt(double clock) => (long)(Asking + (Floor - Asking) * Math.Clamp((clock - Since) / Math.Max(Falls, 1), 0, 1));
}

/// <summary>
/// What an owner with no money does, the same for all. Broke for as long as its nerve allows
/// (<see cref="Personality.GraceHours"/>) it starts selling, one thing at a time: first what earns it nothing, escorts,
/// then the ships that have brought home least, never the last one that can earn; its stations last, cheapest first.
/// With its last station gone it has no domains and no upkeep, the money of the sale and perhaps a ship or two, which
/// trade on their own, and it starts again from the bottom: it can buy a station like anyone.
///
/// A price falls with time. Anyone can buy: an organisation with money to spare by its ambition, a pilot at a market's
/// kiosk. A station nobody takes even at its floor is abandoned: it stops working until someone buys it, at that price.
/// </summary>
public static class Solvency
{
    /// <summary>The owner of an abandoned station.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>The value of a ship of that model: what it would fetch used.</summary>
    public static long Value(ShipModel model) => (long)(model.BuildFee * model.Resale);

    public static void Work(UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        foreach (string id in book.Stations.Select(s => CargoShips.OwnerOf(state, s)).Concat(state.Organisations.Keys).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList())
            if (id != Abandoned && account(id) is OrganisationRecord or PersonRecord)
                Look(account(id), id, state, book);
        // A station nobody runs is on sale at its floor, for whoever wants it
        foreach (StationBook.Station s in book.Stations)
            if (state.Stations.TryGetValue(s.Id, out StationRecord r) && r.Abandoned && !state.Sales.ContainsKey($"{SaleRecord.Station}:{s.Id}"))
            {
                Put(SaleRecord.Station, s.Id, "", s.Worth, Ordinary, state);
                state.Sales[$"{SaleRecord.Station}:{s.Id}"].Asking = state.Sales[$"{SaleRecord.Station}:{s.Id}"].Floor;
            }
        foreach (SaleRecord sale in state.Sales.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList())
            Market(sale, state, book, account);
    }

    /// <summary>What running everything it owns costs an owner an hour.</summary>
    public static double Upkeep(string owner, UniverseState state, StationBook book) =>
        book.Stations.Where(s => CargoShips.OwnerOf(state, s) == owner).Sum(s => s.Upkeep);

    // An organisation's or a person's: the same for both
    private static double BrokeSince(IAccount owner) => owner switch { OrganisationRecord o => o.BrokeSince, PersonRecord p => p.BrokeSince, _ => -1 };

    private static void SetBroke(IAccount owner, double since)
    {
        if (owner is OrganisationRecord o)
            o.BrokeSince = since;
        else if (owner is PersonRecord p)
            p.BrokeSince = since;
    }

    private static void Look(IAccount owner, string id, UniverseState state, StationBook book)
    {
        Personality nature = owner.Nature ?? Ordinary;
        double upkeep = Upkeep(id, state, book);
        if (upkeep <= 0 || owner.Credits >= upkeep * nature.ReserveHours)
        {
            if (upkeep > 0 || owner is OrganisationRecord)
                SetBroke(owner, -1);
            foreach (SaleRecord mine in state.Sales.Values.Where(s => s.Seller == id).ToList())
                state.Sales.Remove(mine.Id);
            return;
        }
        if (BrokeSince(owner) < 0)
            SetBroke(owner, state.Clock);
        if (state.Clock - BrokeSince(owner) >= nature.GraceHours * 3600 && !state.Sales.Values.Any(s => s.Seller == id))
            List(id, nature, state, book);
    }

    private static readonly Personality Ordinary = new();

    private static ShipModel ModelOf(CargoShipRecord ship) => ResourceLoader.Exists(ship.Model) ? Xpace.World.ResourceCache.Load<ShipModel>(ship.Model) : null;

    // What it can best do without: what earns nothing, then what earns least, and last its stations
    private static void List(string owner, Personality nature, UniverseState state, StationBook book)
    {
        // a person's own ship is how they live: never theirs to sell
        string own = state.People.TryGetValue(owner, out PersonRecord person) ? People.OwnShip(person) : null;
        List<CargoShipRecord> ships = state.CargoShips.Values.Where(s => s.Owner == owner && s.Id != own).ToList();
        List<CargoShipRecord> working = ships.Where(s => ModelOf(s)?.Escort == false).OrderBy(s => s.Delivered).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        CargoShipRecord spare = ships.Where(s => ModelOf(s)?.Escort == true).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? (working.Count > 1 ? working[0] : null);
        if (spare != null && ModelOf(spare) is ShipModel model)
        {
            Put(SaleRecord.Ship, spare.Id, owner, Value(model), nature, state);
            return;
        }
        if (book.Stations.Where(s => CargoShips.OwnerOf(state, s) == owner).OrderBy(s => s.Worth).FirstOrDefault() is StationBook.Station station)
            Put(SaleRecord.Station, station.Id, owner, station.Worth, nature, state);
    }

    /// <summary>A pilot puts a station of theirs up for sale, for what it is worth; it falls as anyone's does. Whether it could.</summary>
    public static bool Offer(string station, string pilotOwner, StationBook book, UniverseState state)
    {
        if (book.Find(station) is not StationBook.Station s || CargoShips.OwnerOf(state, s) != pilotOwner || state.Sales.ContainsKey($"{SaleRecord.Station}:{station}"))
            return false;
        Put(SaleRecord.Station, station, pilotOwner, s.Worth, Ordinary, state);
        return true;
    }

    private static void Put(string kind, string item, string seller, long value, Personality nature, UniverseState state) =>
        state.Sales[$"{kind}:{item}"] = new SaleRecord
        {
            Id = $"{kind}:{item}", Kind = kind, Item = item, Seller = seller, Asking = value, Floor = (long)(value * nature.SaleFloor),
            Since = state.Clock, Falls = nature.SaleFallHours * 3600,
        };

    private static void Market(SaleRecord sale, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        long price = sale.PriceAt(state.Clock);
        // Organisations and free people alike: a person who saved enough for a station founds a company of their own
        string buyer = state.Organisations.Values.Select(o => (Id: o.Id, Account: (IAccount)o))
            .Concat(state.People.Values.Select(p => (Id: p.Id, Account: (IAccount)p)))
            .Where(b => b.Id != sale.Seller && b.Id != Abandoned && Wants(b.Id, b.Account, sale, price, state, book))
            .OrderByDescending(b => b.Account.Nature?.Ambition ?? 0).ThenBy(b => b.Id, StringComparer.Ordinal).Select(b => b.Id).FirstOrDefault();
        if (buyer != null)
            Buy(sale, buyer, state, book, account);
        else if (sale.Kind == SaleRecord.Station && sale.Seller != "" && !sale.Seller.StartsWith(PilotRecord.OwnerPrefix) && state.Clock >= sale.Since + sale.Falls)
            Abandon(sale, state, book, account);
    }

    // It has to have the money with some left, as much of it as it dares; a ship needs a station to work for
    private static bool Wants(string id, IAccount who, SaleRecord sale, long price, UniverseState state, StationBook book)
    {
        Personality nature = who.Nature ?? Ordinary;
        if (price > who.Credits * nature.Ambition || BrokeSince(who) >= 0)
            return false;
        return sale.Kind == SaleRecord.Station || book.Stations.Any(s => CargoShips.OwnerOf(state, s) == id);
    }

    /// <summary>Someone buys what is on sale, if they have the money; whether they did.</summary>
    public static bool Buy(SaleRecord sale, string buyer, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        long price = sale.PriceAt(state.Clock);
        if (account(buyer) is not IAccount money || money.Credits < price)
            return false;
        money.Credits -= price;
        if (account(sale.Seller) is IAccount seller)
            seller.Credits += price;
        if (account(sale.Seller) is IAccount broke)
            SetBroke(broke, -1);
        state.Sales.Remove(sale.Id);
        if (sale.Kind == SaleRecord.Ship)
        {
            if (state.CargoShips.TryGetValue(sale.Item, out CargoShipRecord ship))
            {
                ship.Owner = buyer;
                ship.Home = book.Stations.Where(s => CargoShips.OwnerOf(state, s) == buyer).Select(s => s.Id).FirstOrDefault() ?? ship.Home;
            }
            return true;
        }
        Hand(sale.Item, buyer, state);
        // A person's first station is their home from now on: their ships work for it
        if (account(buyer) is PersonRecord && !book.Stations.Any(s => s.Id != sale.Item && CargoShips.OwnerOf(state, s) == buyer))
            foreach (CargoShipRecord ship in state.CargoShips.Values.Where(s => s.Owner == buyer))
                ship.Home = sale.Item;
        return true;
    }

    // The ships stay with whoever owned them: they are what it has left. What it had ordered for the station is off
    private static void Hand(string station, string owner, UniverseState state)
    {
        StationRecord record = state.Station(station);
        foreach (ShipOrderRecord order in state.ShipOrders.Values.Where(o => o.Home == station && o.Owner == record.Owner && o.Ready == 0).ToList())
            state.ShipOrders.Remove(order.Id);
        record.Owner = owner;
        record.Abandoned = false;
    }

    /// <summary>
    /// Someone who owned stations is gone: nobody runs them now. They stand abandoned until someone buys them
    /// (<see cref="Work"/> puts them up at their floor).
    /// </summary>
    public static void Orphan(UniverseState state, string owner)
    {
        foreach ((string id, StationRecord record) in state.Stations.Where(kv => kv.Value.Owner == owner).ToList())
        {
            state.Sales.Remove($"{SaleRecord.Station}:{id}");
            Hand(id, Abandoned, state);
            record.Abandoned = true;
        }
        foreach (SaleRecord sale in state.Sales.Values.Where(s => s.Seller == owner).ToList())
            state.Sales.Remove(sale.Id);
    }

    private static void Abandon(SaleRecord sale, UniverseState state, StationBook book, Func<string, IAccount> account)
    {
        if (account(sale.Seller) is IAccount seller)
            SetBroke(seller, -1);
        Hand(sale.Item, Abandoned, state);
        state.Station(sale.Item).Abandoned = true;
        sale.Seller = "";
        sale.Asking = sale.Floor;
    }
}
