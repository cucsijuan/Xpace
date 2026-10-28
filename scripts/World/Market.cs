using System;

namespace Xpace.World;

/// <summary>
/// How a station's market works, on its record (<see cref="StationRecord.Stock"/>): plain rules, the same for every
/// station, so that later a simulation can run them for stations nobody is at.
///
/// Stock moves with universe time: what the station makes piles up, what it uses runs down, from the time it was
/// last brought up to date (<see cref="Advance"/>), between none and <see cref="Full"/> times what it wants.
/// A tonne's price is the good's base price where the market has the stock it wants, dearer as it runs short and
/// cheaper as it piles up (as the square root of wanted over had, from <see cref="Cheapest"/> to <see cref="Dearest"/>
/// times the base). Each tonne traded moves the stock, so the next tonne's price moves too: a pilot buys at the price
/// of the tonne taken, and sells at <see cref="SellShare"/> of the price of the tonne added, so buying and selling
/// back at once always loses.
/// </summary>
public static class Market
{
    public const double Full = 3, Cheapest = 0.5, Dearest = 3, SellShare = 0.9;

    /// <summary>Brings the station's stock up to universe time <paramref name="clock"/>.</summary>
    public static void Advance(StationRecord station, MarketGood[] goods, double clock)
    {
        double hours = Math.Max(0, clock - station.UpdatedAt) / 3600;
        foreach (MarketGood line in goods)
        {
            if (line?.Good == null)
                continue;
            string id = line.Good.Id;
            double stock = station.Stock.TryGetValue(id, out double s) ? s + line.Rate * hours : line.Stock;
            station.Stock[id] = Math.Clamp(stock, 0, Full * line.Stock);
        }
        station.UpdatedAt = Math.Max(station.UpdatedAt, clock);
    }

    /// <summary>A tonne's price with <paramref name="stock"/> tonnes in the market.</summary>
    public static long Price(MarketGood line, double stock)
    {
        double ratio = Math.Sqrt(line.Stock / Math.Max(stock, 1.0));
        return (long)Math.Round(line.Good.BasePrice * Math.Clamp(ratio, Cheapest, Dearest));
    }

    /// <summary>What the next tonne costs a pilot (none in stock: -1).</summary>
    public static long BuyPrice(MarketGood line, double stock) => stock >= 1 ? Price(line, stock - 1) : -1;

    /// <summary>What the market pays a pilot for the next tonne (full: -1).</summary>
    public static long SellPrice(MarketGood line, double stock) =>
        stock + 1 <= Full * line.Stock ? (long)Math.Floor(Price(line, stock + 1) * SellShare) : -1;

    /// <summary>Why a trade didn't happen (or <see cref="Done"/>).</summary>
    public enum Result { Done, NoStock, MarketFull, NoMoney, HoldFull, NotHeld, NotTraded }

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
