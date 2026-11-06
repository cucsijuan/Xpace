using System.Collections.Generic;
using System.Linq;

namespace Xpace.World;

/// <summary>What an actor knows of a station's market, and when it was true.</summary>
public sealed class StationNews
{
    /// <summary>The universe time it was true.</summary>
    public double AsOf { get; set; }
    public Dictionary<string, GoodNews> Goods { get; set; } = new();
}

/// <summary>A good at a station, as it stood.</summary>
public sealed class GoodNews
{
    public double Stock { get; set; }
    /// <summary>What it cost to buy a tonne there, and what the station paid for one (-1: it didn't).</summary>
    public long Buy { get; set; }
    public long Sell { get; set; }
}

/// <summary>
/// How actors come to know markets. Nobody reads a far station's stock: they know what they saw there, what a
/// commercial beacon told them, or nothing. Owners see their own stations as they are.
/// </summary>
public static class MarketNews
{
    /// <summary>The station's market as it is now.</summary>
    public static StationNews Report(StationBook.Station station, StationRecord record, double clock)
    {
        var news = new StationNews { AsOf = clock };
        foreach (MarketGood line in station.Goods.Where(g => g.Good.Traded))
        {
            double stock = record.Stock.GetValueOrDefault(line.Good.Id);
            news.Goods[line.Good.Id] = new GoodNews { Stock = stock, Buy = Market.BuyPrice(record, line), Sell = Market.SellPrice(line, stock) };
        }
        return news;
    }

    /// <summary>Keeps the news unless the actor already knows something newer.</summary>
    public static void Learn(IAccount actor, string station, StationNews news)
    {
        if (actor != null && (!actor.Known.TryGetValue(station, out StationNews old) || old.AsOf <= news.AsOf))
            actor.Known[station] = news;
    }

    /// <summary>The actor sees a station's market.</summary>
    public static void See(IAccount actor, StationBook.Station station, UniverseState state, System.Func<string, IAccount> account) =>
        Learn(actor, station.Id, Report(station, CargoShips.Bring(state, station, account), state.Clock));

    /// <summary>A beacon tells the actor what it has; how many stations that was.</summary>
    public static int Hear(IAccount actor, StationBook.Beacon beacon, UniverseState state, StationBook book, System.Func<string, IAccount> account)
    {
        List<StationBook.Station> told = book.ReportedBy(beacon).ToList();
        foreach (StationBook.Station station in told)
            See(actor, station, state, account);
        return told.Count;
    }
}
