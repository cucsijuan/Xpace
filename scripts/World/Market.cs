using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>
/// How a station's market works, on its record (<see cref="StationRecord.Stock"/>): plain rules, the same for every
/// station, so that later a simulation can run them for stations nobody is at.
///
/// Stock moves with universe time: its factories turn what they take into what they make (<see cref="Recipe"/>), its
/// mines add to it, its people use it up, from the time it was last brought up to date (<see cref="Advance"/>),
/// between none and <see cref="Full"/> times what it wants. Nothing comes from nowhere but out of a mine. The
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

    /// <summary>The longest step of the stations' work: what one factory makes reaches the next in time.</summary>
    private const double StepHours = 1;
    private const int MostSteps = 4096;

    /// <summary>Brings the station's stock up to universe time <paramref name="clock"/>.</summary>
    /// <param name="cash">Its owner's account, if it has one: what the station's people use up they pay for, at the market's price.</param>
    /// <param name="upkeep">What running the station costs its owner an hour.</param>
    public static void Advance(StationRecord station, MarketGood[] goods, double clock, OutfitLine[] outfits = null, FactoryLine[] factories = null, IAccount cash = null, double upkeep = 0)
    {
        if (station.Abandoned)
        {
            station.UpdatedAt = Math.Max(station.UpdatedAt, clock);
            return;                                     // nobody there: nothing made, used up or paid
        }
        bool first = station.Stock.Count == 0;
        double hours = first ? HoursBeforeStart + clock / 3600 : Math.Max(0, clock - station.UpdatedAt) / 3600;
        var most = new Dictionary<string, double>();
        foreach (MarketGood line in goods)
            if (line?.Good != null)
            {
                most[line.Good.Id] = Full * line.Stock;
                station.Stock.TryAdd(line.Good.Id, line.Stock);
            }
        foreach (OutfitLine line in outfits ?? Array.Empty<OutfitLine>())
            if (line?.Item != null)
            {
                most[line.Id] = line.Stock;
                station.Stock.TryAdd(line.Id, line.Stock);
            }
        int steps = Math.Min(MostSteps, (int)Math.Ceiling(hours / StepHours));
        for (int i = 0; i < steps; i++)
        {
            double step = hours / steps;
            // Without workforce or energy, or with an owner who can't pay for the place, nothing is made: what is in stock
            // can still be sold, and it starts again by itself when all is put right
            station.Idle = goods.Any(g => g?.Good != null && g.Uses > 0 && g.Good.Use != GoodUse.Production && station.Stock[g.Good.Id] <= 0) || cash != null && cash.Credits < upkeep * step;
            if (!station.Idle)
                foreach (FactoryLine line in factories ?? Array.Empty<FactoryLine>())
                    Run(station, line, most, step);
            double income = -upkeep * step;
            foreach (MarketGood line in goods)
                if (line?.Good != null)
                {
                    double had = station.Stock[line.Good.Id];
                    station.Stock[line.Good.Id] = Math.Clamp(had - line.Uses * step, 0, most[line.Good.Id]);
                    income += Math.Max(0, had - station.Stock[line.Good.Id]) * Price(line, had);
                }
            if (cash != null)
                Settle(station, cash, income);
        }
        station.UpdatedAt = Math.Max(station.UpdatedAt, clock);
        if (cash != null)
            Reckon(station, goods, outfits, factories, cash.Margin, upkeep);
    }

    // What its owner won't sell under. A thing costs what goes into it at the prices its inputs have here, and its share
    // of the upkeep by what it is worth: so a dearer input makes everything made of it dearer, down the chain
    private static void Reckon(StationRecord station, MarketGood[] goods, OutfitLine[] outfits, FactoryLine[] factories, double margin, double upkeep)
    {
        station.Floors.Clear();
        double worth = 0;
        foreach (FactoryLine line in factories ?? Array.Empty<FactoryLine>())
            worth += line?.Recipe != null ? line.PerHour * line.Recipe.OutputAmount * BaseOf(line.Recipe.Output) : 0;
        double overhead = worth > 0 ? upkeep / worth : 0;
        foreach (FactoryLine line in factories ?? Array.Empty<FactoryLine>())
        {
            if (line?.Recipe is not Recipe recipe || recipe.OutputAmount <= 0 || Recipe.IdOf(recipe.Output) is not string made)
                continue;
            double cost = 0;
            foreach (RecipePart part in recipe.Inputs)
                if (part?.Amount > 0)
                    cost += part.Amount * InputPrice(station, part.Item, goods, outfits);
            station.Floors[made] = (cost / recipe.OutputAmount + BaseOf(recipe.Output) * overhead) * (1 + margin);
        }
    }

    private static double BaseOf(Resource item) => item switch
    {
        Good good => good.BasePrice,
        Xpace.Space.Outfit outfit => outfit.Price,
        _ => 0,
    };

    private static double InputPrice(StationRecord station, Resource item, MarketGood[] goods, OutfitLine[] outfits)
    {
        string id = Recipe.IdOf(item);
        double stock = station.Stock.GetValueOrDefault(id);
        foreach (MarketGood line in goods)
            if (line?.Good?.Id == id)
                return Price(line, stock);
        return BaseOf(item);
    }

    // Credits are whole: what doesn't make one yet waits for the next time. An owner with nothing left owes nothing more
    private static void Settle(StationRecord station, IAccount cash, double amount)
    {
        station.CashCarry += amount;
        long whole = (long)Math.Truncate(station.CashCarry);
        station.CashCarry -= whole;
        cash.Credits = Math.Max(0, cash.Credits + whole);
    }

    // What a factory makes in so many hours: as many runs as its rate, its inputs in stock and the room for what it makes allow
    private static void Run(StationRecord station, FactoryLine line, Dictionary<string, double> most, double hours)
    {
        if (line?.Recipe is not Recipe recipe || Recipe.IdOf(recipe.Output) is not string made || !most.TryGetValue(made, out double room) || recipe.OutputAmount <= 0)
            return;
        double stock = station.Stock.GetValueOrDefault(made);
        double runs = Math.Min(line.PerHour * hours, Math.Max(0, room - stock) / recipe.OutputAmount);
        foreach (RecipePart part in recipe.Inputs)
            if (part?.Amount > 0)
                runs = Math.Min(runs, station.Stock.GetValueOrDefault(Recipe.IdOf(part.Item)) / part.Amount);
        foreach (RecipePart part in recipe.Inputs)
            if (part?.Amount > 0)
                station.Stock[Recipe.IdOf(part.Item)] -= runs * part.Amount;
        station.Stock[made] = stock + runs * recipe.OutputAmount;
    }

    /// <summary>What a station makes of <paramref name="item"/> an hour, less what its factories and people use, all running.</summary>
    public static double Net(string item, MarketGood[] goods, FactoryLine[] factories)
    {
        double net = 0;
        foreach (MarketGood line in goods)
            if (line?.Good?.Id == item)
                net -= line.Uses;
        foreach (FactoryLine line in factories ?? Array.Empty<FactoryLine>())
        {
            if (line?.Recipe == null)
                continue;
            if (Recipe.IdOf(line.Recipe.Output) == item)
                net += line.PerHour * line.Recipe.OutputAmount;
            foreach (RecipePart part in line.Recipe.Inputs)
                if (Recipe.IdOf(part?.Item) == item)
                    net -= line.PerHour * part.Amount;
        }
        return net;
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

    /// <summary>What the next tonne costs at this station now: never under what its owner makes it for.</summary>
    public static long BuyPrice(StationRecord station, MarketGood line) => Floored(station, line.Good.Id, BuyPrice(line, station.Stock.GetValueOrDefault(line.Good.Id)));

    public const string Fuel = "hydrogen";

    /// <summary>What a tonne of fuel costs here (below 0: none sold here).</summary>
    public static long FuelPrice(StationRecord station, MarketGood[] goods) =>
        goods.FirstOrDefault(g => g?.Good?.Id == Fuel) is MarketGood line ? BuyPrice(station, line) : -1;

    /// <summary>Fills a tank from the market, a tonne at a time, as far as the money and the stock go; how much went in (the last tonne's rest stays in the pump). </summary>
    public static int Refill(StationRecord station, MarketGood[] goods, IAccount buyer, ref double fuel, double tank, IAccount cash, long most, out long paid)
    {
        paid = 0;
        int tonnes = 0;
        if (goods.FirstOrDefault(g => g?.Good?.Id == Fuel) is not MarketGood line)
            return 0;
        while (fuel < tank)
        {
            long price = BuyPrice(station, line);
            if (price < 0 || buyer.Credits < price || paid + price > most)
                break;
            buyer.Credits -= price;
            Receive(cash, price);
            station.Stock[Fuel] = station.Stock[Fuel] - 1;
            fuel = Math.Min(tank, fuel + 1);
            paid += price;
            tonnes++;
        }
        return tonnes;
    }

    /// <summary>A pilot's ship fills up.</summary>
    public static int Refuel(StationRecord station, MarketGood[] goods, PilotRecord pilot, double tank, IAccount cash, out long paid)
    {
        double fuel = pilot.Ship.Fuel < 0 ? tank : pilot.Ship.Fuel;
        int tonnes = Refill(station, goods, pilot, ref fuel, tank, cash, long.MaxValue, out paid);
        pilot.Ship.Fuel = fuel;
        return tonnes;
    }

    /// <summary>What the next one of an outfit costs at this station now.</summary>
    public static long OutfitPrice(StationRecord station, OutfitLine line) => Floored(station, line.Id, OutfitPrice(line, station.Stock.GetValueOrDefault(line.Id)));

    private static long Floored(StationRecord station, string id, long price) =>
        price >= 0 && station.Floors.TryGetValue(id, out double floor) ? Math.Max(price, (long)Math.Ceiling(floor)) : price;

    /// <summary>What the market pays a pilot for the next tonne (full: -1).</summary>
    public static long SellPrice(MarketGood line, double stock) =>
        stock + 1 <= Full * line.Stock ? (long)Math.Floor(Price(line, stock + 1) * SellShare) : -1;

    /// <summary>Why a trade didn't happen (or <see cref="Done"/>).</summary>
    public enum Result { Done, NoStock, MarketFull, NoMoney, HoldFull, NotHeld, NotTraded, RackFull, NoCash }

    /// <summary>
    /// The pilot buys one piece of equipment (a laser, a turret: <see cref="Xpace.Ship.Fittings.IsPiece"/>), put in
    /// <paramref name="locker"/> at the station to be fitted. <paramref name="paid"/> is its price.
    /// </summary>
    public static Result BuyPiece(StationRecord station, OutfitLine line, PilotRecord pilot, List<ItemRecord> locker, out long paid, IAccount cash = null)
    {
        double stock = station.Stock.GetValueOrDefault(line.Id);
        paid = OutfitPrice(station, line);
        if (paid < 0)
            return Result.NoStock;
        if (!pilot.Spend(paid))
            return Result.NoMoney;
        Receive(cash, paid);
        station.Stock[line.Id] = stock - 1;
        locker.Add(ItemRecord.PieceOf(line.Item));
        return Result.Done;
    }

    /// <summary>
    /// The pilot sells one piece of equipment of <paramref name="line"/>'s model from <paramref name="locker"/> to the
    /// dealer, which puts it back in its stock. <paramref name="paid"/> is what the pilot gets.
    /// </summary>
    public static Result SellPiece(StationRecord station, OutfitLine line, PilotRecord pilot, List<ItemRecord> locker, out long paid, IAccount cash = null)
    {
        double stock = station.Stock.GetValueOrDefault(line.Id);
        paid = PieceSellPrice(line, stock);
        int index = locker.FindIndex(i => i.Kind == ItemRecord.Piece && i.Data == line.Item.ResourcePath);
        if (index < 0)
            return Result.NotHeld;
        if (paid < 0)
            return Result.MarketFull;
        if (!Pay(cash, paid))
            return Result.NoCash;
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
    public static Result BuyOutfit(StationRecord station, OutfitLine line, PilotRecord pilot, int count, int rack, int factory, out int bought, out long paid, IAccount cash = null)
    {
        bought = 0;
        paid = 0;
        while (bought < count)
        {
            double stock = station.Stock.GetValueOrDefault(line.Id);
            long price = OutfitPrice(station, line);
            int aboard = pilot.Ship.Rack(line.Id, factory);
            if (price < 0)
                return Result.NoStock;
            if (aboard >= rack)
                return Result.RackFull;
            if (!pilot.Spend(price))
                return Result.NoMoney;
            Receive(cash, price);
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
    public static Result Buy(StationRecord station, MarketGood line, PilotRecord pilot, int tonnes, int capacity, out int bought, out long paid, IAccount cash = null)
    {
        bought = 0;
        paid = 0;
        string id = line.Good.Id;
        while (bought < tonnes)
        {
            double stock = station.Stock.TryGetValue(id, out double s) ? s : 0;
            long price = BuyPrice(station, line);
            if (price < 0)
                return Result.NoStock;
            if (pilot.Ship.Tonnes + 1 > capacity)
                return Result.HoldFull;
            if (!pilot.Spend(price))
                return Result.NoMoney;
            Receive(cash, price);
            station.Stock[id] = stock - 1;
            pilot.Ship.Load(id, 1);
            bought++;
            paid += price;
        }
        return Result.Done;
    }

    /// <summary>The pilot sells <paramref name="tonnes"/> of the good from the hold, as many as the hold has and the market takes.</summary>
    public static Result Sell(StationRecord station, MarketGood line, PilotRecord pilot, int tonnes, out int sold, out long earned, IAccount cash = null)
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
            if (!Pay(cash, price))
                return Result.NoCash;
            station.Stock[id] = stock + 1;
            pilot.Ship.Load(id, -1);
            pilot.Credits += price;
            sold++;
            earned += price;
        }
        return Result.Done;
    }

    /// <summary>A ship of <paramref name="buyer"/>'s takes up to <paramref name="tonnes"/> at the market's prices, as far as the money goes. Returns the tonnes.</summary>
    public static int Take(StationRecord station, MarketGood line, IAccount buyer, int tonnes, IAccount cash)
    {
        string id = line.Good.Id;
        int taken = 0;
        while (taken < tonnes)
        {
            double stock = station.Stock.GetValueOrDefault(id);
            long price = BuyPrice(station, line);
            if (price < 0 || buyer == null || buyer.Credits < price)
                break;
            buyer.Credits -= price;
            Receive(cash, price);
            station.Stock[id] = stock - 1;
            taken++;
        }
        return taken;
    }

    /// <summary>A ship of <paramref name="seller"/>'s hands over tonnes: its own station's for nothing, another's at the market's prices. What there is no room or money for is lost.</summary>
    public static void Hand(StationRecord station, MarketGood line, int tonnes, IAccount seller, IAccount cash)
    {
        string id = line.Good.Id;
        for (int i = 0; i < tonnes; i++)
        {
            double stock = station.Stock.GetValueOrDefault(id);
            long price = SellPrice(line, stock);
            if (price < 0)
                return;
            if (!ReferenceEquals(seller, cash))
            {
                if (!Pay(cash, price))
                    return;
                Receive(seller, price);
            }
            station.Stock[id] = stock + 1;
        }
    }

    // A pilot's money goes to the station's owner, and its owner pays for what it buys, if it has the money
    private static void Receive(IAccount cash, long amount)
    {
        if (cash != null)
            cash.Credits += amount;
    }

    private static bool Pay(IAccount cash, long amount)
    {
        if (cash == null)
            return true;
        if (cash.Credits < amount)
            return false;
        cash.Credits -= amount;
        return true;
    }
}
