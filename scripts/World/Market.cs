using System;
using System.Collections.Generic;

namespace Xpace.World;

/// <summary>
/// How a station's market works, on its record (<see cref="StationRecord.Stock"/>): plain rules, the same for every
/// station, so that later a simulation can run them for stations nobody is at.
///
/// Stock moves with universe time: what the station makes piles up, what it uses runs down, from the time it was
/// last brought up to date (<see cref="Advance"/>), between none and <see cref="Full"/> times what it wants. The
/// stations were at work long before any pilot came (<see cref="HoursBeforeStart"/>) and go on working whether anyone
/// has dealt with them or not, so a market first seen late stands where it would have anyway.
/// A tonne's price is the good's base price where the market has the stock it wants, dearer as it runs short and
/// cheaper as it piles up (as the square root of wanted over had, from <see cref="Cheapest"/> to <see cref="Dearest"/>
/// times the base). Each tonne traded moves the stock, so the next tonne's price moves too: a pilot buys at the price
/// of the tonne taken, and sells at <see cref="SellShare"/> of the price of the tonne added, so buying and selling
/// back at once always loses.
/// </summary>
public static class Market
{
    public const double Full = 3, Cheapest = 0.5, Dearest = 3, SellShare = 0.9;

    /// <summary>How long the stations have been making and using things when a universe's clock starts.</summary>
    public const double HoursBeforeStart = 4;

    /// <summary>Brings the station's stock up to universe time <paramref name="clock"/>.</summary>
    public static void Advance(StationRecord station, MarketGood[] goods, double clock, OutfitLine[] outfits = null)
    {
        double hours = Math.Max(0, clock - station.UpdatedAt) / 3600;
        foreach (MarketGood line in goods)
        {
            if (line?.Good == null)
                continue;
            string id = line.Good.Id;
            double stock = station.Stock.TryGetValue(id, out double s) ? s + line.Rate * hours : line.Stock + line.Rate * (HoursBeforeStart + clock / 3600);
            station.Stock[id] = Math.Clamp(stock, 0, Full * line.Stock);
        }
        foreach (OutfitLine line in outfits ?? Array.Empty<OutfitLine>())
            Make(station, line, hours);
        station.UpdatedAt = Math.Max(station.UpdatedAt, clock);
    }

    // What the station's factory makes of an outfit in so many hours: up to the stock it wants, and only as far as the
    // input it has on the market lasts
    private static void Make(StationRecord station, OutfitLine line, double hours)
    {
        if (line?.Item == null)
            return;
        if (!station.Stock.TryGetValue(line.Id, out double stock))
        {
            station.Stock[line.Id] = line.Stock;
            return;
        }
        double make = Math.Min(line.PerHour * hours, Math.Max(0, line.Stock - stock));
        if (line.Input != null && line.InputPerUnit > 0)
        {
            double input = station.Stock.GetValueOrDefault(line.Input.Id);
            make = Math.Min(make, input / line.InputPerUnit);
            station.Stock[line.Input.Id] = input - make * line.InputPerUnit;
        }
        station.Stock[line.Id] = stock + make;
    }

    /// <summary>A price with <paramref name="stock"/> in the market, where <paramref name="wanted"/> gives the base price.</summary>
    public static long Price(long basePrice, double wanted, double stock)
    {
        double ratio = Math.Sqrt(wanted / Math.Max(stock, 1.0));
        return (long)Math.Round(basePrice * Math.Clamp(ratio, Cheapest, Dearest));
    }

    /// <summary>A tonne's price with <paramref name="stock"/> tonnes in the market.</summary>
    public static long Price(MarketGood line, double stock) => Price(line.Good.BasePrice, line.Stock, stock);

    /// <summary>What the next one of an outfit costs a pilot (none in stock: -1).</summary>
    public static long OutfitPrice(OutfitLine line, double stock) => stock >= 1 ? Price(line.Item.Price, line.Stock, stock - 1) : -1;

    /// <summary>What the dealer pays a pilot for a piece of equipment (it takes no more: -1), as the market pays for a tonne.</summary>
    public static long PieceSellPrice(OutfitLine line, double stock) =>
        stock + 1 <= Full * line.Stock ? (long)Math.Floor(Price(line.Item.Price, line.Stock, stock + 1) * SellShare) : -1;

    /// <summary>What the next tonne costs a pilot (none in stock: -1).</summary>
    public static long BuyPrice(MarketGood line, double stock) => stock >= 1 ? Price(line, stock - 1) : -1;

    /// <summary>What the market pays a pilot for the next tonne (full: -1).</summary>
    public static long SellPrice(MarketGood line, double stock) =>
        stock + 1 <= Full * line.Stock ? (long)Math.Floor(Price(line, stock + 1) * SellShare) : -1;

    /// <summary>Why a trade didn't happen (or <see cref="Done"/>).</summary>
    public enum Result { Done, NoStock, MarketFull, NoMoney, HoldFull, NotHeld, NotTraded, RackFull }

    /// <summary>
    /// The pilot buys one piece of equipment (a laser, a turret: <see cref="Xpace.Ship.Fittings.IsPiece"/>), put in
    /// <paramref name="locker"/> at the station to be fitted. <paramref name="paid"/> is its price.
    /// </summary>
    public static Result BuyPiece(StationRecord station, OutfitLine line, PilotRecord pilot, List<ItemRecord> locker, out long paid)
    {
        double stock = station.Stock.GetValueOrDefault(line.Id);
        paid = OutfitPrice(line, stock);
        if (paid < 0)
            return Result.NoStock;
        if (!pilot.Spend(paid))
            return Result.NoMoney;
        station.Stock[line.Id] = stock - 1;
        locker.Add(ItemRecord.PieceOf(line.Item));
        return Result.Done;
    }

    /// <summary>
    /// The pilot sells one piece of equipment of <paramref name="line"/>'s model from <paramref name="locker"/> to the
    /// dealer, which puts it back in its stock. <paramref name="paid"/> is what the pilot gets.
    /// </summary>
    public static Result SellPiece(StationRecord station, OutfitLine line, PilotRecord pilot, List<ItemRecord> locker, out long paid)
    {
        double stock = station.Stock.GetValueOrDefault(line.Id);
        paid = PieceSellPrice(line, stock);
        int index = locker.FindIndex(i => i.Kind == ItemRecord.Piece && i.Data == line.Item.ResourcePath);
        if (index < 0)
            return Result.NotHeld;
        if (paid < 0)
            return Result.MarketFull;
        locker.RemoveAt(index);
        station.Stock[line.Id] = stock + 1;
        pilot.Credits += paid;
        return Result.Done;
    }

    /// <summary>
    /// The pilot buys <paramref name="count"/> of an outfit, loaded straight onto a ship whose rack takes
    /// <paramref name="rack"/> of them: as many as the stock, the money and the rack allow. With none of it in the
    /// pilot's record, the ship has <paramref name="factory"/> aboard (what a new ship comes with).
    /// </summary>
    public static Result BuyOutfit(StationRecord station, OutfitLine line, PilotRecord pilot, int count, int rack, int factory, out int bought, out long paid)
    {
        bought = 0;
        paid = 0;
        while (bought < count)
        {
            double stock = station.Stock.GetValueOrDefault(line.Id);
            long price = OutfitPrice(line, stock);
            int aboard = pilot.Ship.Rack(line.Id, factory);
            if (price < 0)
                return Result.NoStock;
            if (aboard >= rack)
                return Result.RackFull;
            if (!pilot.Spend(price))
                return Result.NoMoney;
            station.Stock[line.Id] = stock - 1;
            pilot.Ship.Magazine[line.Id] = aboard + 1;
            bought++;
            paid += price;
        }
        return Result.Done;
    }

    /// <summary>
    /// The pilot buys <paramref name="tonnes"/> of the good, a tonne at a time, into a hold that takes
    /// <paramref name="capacity"/> tonnes: as many as the stock, the money and the hold allow (the result says what
    /// stopped it short). <paramref name="paid"/> is the total.
    /// </summary>
    public static Result Buy(StationRecord station, MarketGood line, PilotRecord pilot, int tonnes, int capacity, out int bought, out long paid)
    {
        bought = 0;
        paid = 0;
        string id = line.Good.Id;
        while (bought < tonnes)
        {
            double stock = station.Stock.TryGetValue(id, out double s) ? s : 0;
            long price = BuyPrice(line, stock);
            if (price < 0)
                return Result.NoStock;
            if (pilot.Ship.Tonnes + 1 > capacity)
                return Result.HoldFull;
            if (!pilot.Spend(price))
                return Result.NoMoney;
            station.Stock[id] = stock - 1;
            pilot.Ship.Load(id, 1);
            bought++;
            paid += price;
        }
        return Result.Done;
    }

    /// <summary>The pilot sells <paramref name="tonnes"/> of the good from the hold, as many as the hold has and the market takes.</summary>
    public static Result Sell(StationRecord station, MarketGood line, PilotRecord pilot, int tonnes, out int sold, out long earned)
    {
        sold = 0;
        earned = 0;
        string id = line.Good.Id;
        while (sold < tonnes)
        {
            if (pilot.Ship.Holds(id) < 1)
                return Result.NotHeld;
            double stock = station.Stock.TryGetValue(id, out double s) ? s : 0;
            long price = SellPrice(line, stock);
            if (price < 0)
                return Result.MarketFull;
            station.Stock[id] = stock + 1;
            pilot.Ship.Load(id, -1);
            pilot.Credits += price;
            sold++;
            earned += price;
        }
        return Result.Done;
    }
}
