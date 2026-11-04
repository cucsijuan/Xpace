using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// What trading pays, measured from the game's own data and market rules: the goods, each station's market, the
/// hulls, the generator, the tolls and where the sector's stations, gates and masses are. Change any of them and run
/// this again: nothing here is a copy of a value, only the targets below are this test's own.
///
/// It prints:
///   - each good's price on both ends, in a new universe and once the markets have settled;
///   - the best run in each direction as the universe ages, with a new pilot's money and with a full hold;
///   - careers: a pilot who always carries what pays best on this leg, and waits when nothing pays (credits against
///     hours played, when the first paying run comes, what an hour earns at the end);
///   - what a settled market pays before a pilot wears the difference out.
///
/// It fails when the economy breaks the targets: buying and selling back gains at some stock; a good with no maker
/// or no user across the gate; a good not worth carrying once settled; a new pilot waiting too long to earn, or
/// earning too little or far too much; a market that can be milked for ever with no time passing.
///
/// A leg's time: the flight at the top speed the hull and the generator give at each point of the way (the medium
/// thickening near masses, the speed limit near stations, the cargo's weight), plus <see cref="HandlingSeconds"/> for
/// what the pilot does by hand. No session, no scene in the tree, no fixed fps needed.
/// Run: Godot --headless --path . res://tests/trade_balance_test.tscn
/// Exits with 0 if every target is met.
/// </summary>
public partial class TradeBalanceTest : Node
{
    // The targets. Move them when the design moves: they say what the economy is meant to be
    private const double MaxHoursToFirstPayingRun = 0.5;
    private const double MinCreditsPerHour = 2_000, MaxCreditsPerHour = 100_000;
    private const int MaxLegsToWearOut = 2_000;

    /// <summary>A leg's time at the pilot's hands: launch and door, spooling, approach, docking, the walk to the kiosk and back.</summary>
    private const double HandlingSeconds = 240;
    private const double WaitSeconds = 600;
    private const double SettledHours = 24;
    private const double CareerHours = 20;
    private const float CruiseCap = 4096;

    private int _failures;
    private End _a, _b, _works;
    private ShipSpec _fighter, _courtesy;
    private GeneratorSpec _generator;
    private float _engineThrust;

    /// <summary>One end of the route: a station's market and the way from it to its sector's gate.</summary>
    private sealed class End
    {
        public string Name;
        public MarketGood[] Goods;
        public FactoryLine[] Factories;
        public StationRecord Record;
        public Vector3 Station, Gate;
        public List<(Vector3 At, float Reach)> Masses;
        public float SpeedRadius, SpeedLimit;
        public long Toll;       // to go through this end's gate
    }

    private sealed class Run
    {
        public string Good;
        public int Tonnes;
        public long Paid, Earned, Profit;
    }

    public override void _Ready()
    {
        _fighter = GD.Load<ShipSpec>(ShipRecord.StarterHull);
        _courtesy = GD.Load<ShipSpec>(ShipRecord.CourtesyHull);
        _generator = GD.Load<GeneratorSpec>("res://assets/ship/generators/generator_g1.tres");
        _engineThrust = GD.Load<EngineSpec>("res://assets/ship/engines/engine_d50.tres").RatedThrust;
        _a = Read("res://scenes/sectors/sector_01.tscn", "Station", "Gate2");
        _b = Read("res://scenes/sectors/sector_02.tscn", "Kestrel", "Gate1");
        _works = Read("res://scenes/sectors/sector_01.tscn", "Quartz", "Gate2");

        Route();
        Prices();
        Ageing();
        Careers();
        NoArbitrage();
        Complementary();
        WorthCarrying();
        WearsOut();
        ChipsChain();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- The measures ----

    private void Route()
    {
        GD.Print($"\nThe route: {_a.Name} to {_b.Name}, toll {_a.Toll} there and {_b.Toll} back, handling {HandlingSeconds:0} s a leg");
        foreach (ShipSpec hull in new[] { _fighter, _courtesy })
        {
            GD.Print($"  {hull.ResourcePath.GetFile().GetBaseName()}: {hull.CargoCapacity} t, " +
                     $"empty {LegSeconds(_a, _b, hull, 0) / 60:0.0} min, full {LegSeconds(_a, _b, hull, hull.CargoCapacity) / 60:0.0} min a leg " +
                     $"(flying {(LegSeconds(_a, _b, hull, hull.CargoCapacity) - HandlingSeconds) / 60:0.0})");
        }
    }

    private void Prices()
    {
        foreach (double hours in new[] { 0, SettledHours })
        {
            Advance(hours * 3600);
            GD.Print($"\nPrices at {hours:0} h (buy here / the other end pays, for one tonne, a stock left by nobody trading):");
            GD.Print($"  {"good",-12}{_a.Name + " buy",12}{_b.Name + " pays",14}{"margin",8}   {_b.Name + " buy",12}{_a.Name + " pays",14}{"margin",8}");
            foreach (string good in Goods())
            {
                long ab = Market.BuyPrice(Line(_a, good), Stock(_a, good)), bp = Market.SellPrice(Line(_b, good), Stock(_b, good));
                long ba = Market.BuyPrice(Line(_b, good), Stock(_b, good)), ap = Market.SellPrice(Line(_a, good), Stock(_a, good));
                GD.Print($"  {good,-12}{Shown(ab),12}{Shown(bp),14}{Margin(ab, bp),8}   {Shown(ba),12}{Shown(ap),14}{Margin(ba, ap),8}");
            }
        }
        Reset();
    }

    private static string Shown(long price) => price < 0 ? "none" : price.ToString();

    private static string Margin(long buy, long sell) => buy < 0 || sell < 0 ? "-" : (sell - buy).ToString();

    private void Ageing()
    {
        GD.Print($"\nThe best run as the universe ages, nobody trading (profit after the toll; {_fighter.CargoCapacity} t hold):");
        GD.Print($"  {"hours",5}  {"a new pilot's " + PilotRecord.StartingCredits,-34}{"money no object",-34}");
        foreach (double hours in new double[] { 0, 0.5, 1, 2, 4, 8, 12, 24 })
        {
            Advance(hours * 3600);
            string poor = Describe(Best(_a, _b, _fighter, PilotRecord.StartingCredits, hours * 3600), Best(_b, _a, _fighter, PilotRecord.StartingCredits, hours * 3600));
            string rich = Describe(Best(_a, _b, _fighter, long.MaxValue / 2, hours * 3600), Best(_b, _a, _fighter, long.MaxValue / 2, hours * 3600));
            GD.Print($"  {hours,5:0.0}  {poor,-34}{rich,-34}");
        }
        Reset();
    }

    private static string Describe(Run there, Run back)
    {
        Run best = there == null || (back != null && back.Profit > there.Profit) ? back : there;
        return best == null ? "nothing" : $"{best.Good} {best.Tonnes} t: {best.Profit:+0;-0}";
    }

    private void Careers()
    {
        GD.Print($"\nCareers, {CareerHours:0} hours played, from a new universe:");
        Career career = Live(_fighter, PilotRecord.StartingCredits, 0, "fighter, new pilot");
        Check($"a new pilot's first paying run comes within {MaxHoursToFirstPayingRun:0} h ({career.FirstPaying:0.00} h)",
              career.FirstPaying >= 0 && career.FirstPaying <= MaxHoursToFirstPayingRun);
        Check($"a career earns between {MinCreditsPerHour:0} and {MaxCreditsPerHour:0} credits an hour by its end ({career.LastHour:0})",
              career.LastHour >= MinCreditsPerHour && career.LastHour <= MaxCreditsPerHour);
        Live(_courtesy, PilotRecord.StartingCredits, 0, "courtesy ship, new pilot's money");
        Live(_courtesy, 200, 0, "courtesy ship, 200 credits left");
        Live(_fighter, PilotRecord.StartingCredits, SettledHours * 3600, $"fighter, new pilot, universe {SettledHours:0} h old");
        Reset();
    }

    private sealed class Career
    {
        public double FirstPaying = -1, LastHour;
    }

    private Career Live(ShipSpec hull, long credits, double startClock, string title)
    {
        double clock = startClock;
        Advance(clock);
        var pilot = new PilotRecord { Credits = credits };
        var career = new Career();
        End here = _a, there = _b;
        int runs = 0;
        double waited = 0, end = startClock + CareerHours * 3600, nextMark = startClock + 3600;
        long lastMark = credits, before = credits;
        var marks = new List<string>();
        while (clock < end)
        {
            Run run = Best(here, there, hull, pilot.Credits, clock);
            if (run != null && run.Profit > 0)
            {
                Market.Buy(here.Record, Line(here, run.Good), pilot, run.Tonnes, run.Tonnes, out int bought, out _);
                pilot.Credits -= here.Toll;
                clock += LegSeconds(here, there, hull, bought);
                AdvanceBoth(clock);
                Market.Sell(there.Record, Line(there, run.Good), pilot, bought, out _, out _);
                pilot.Ship.Cargo.Clear();
                runs++;
                if (career.FirstPaying < 0)
                    career.FirstPaying = (clock - startClock) / 3600;
                (here, there) = (there, here);
            }
            else if (Best(there, here, hull, pilot.Credits - here.Toll, clock + LegSeconds(here, there, hull, 0)) is { Profit: > 0 } back &&
                     back.Profit > here.Toll && pilot.Credits >= here.Toll)
            {
                // Nothing pays from here, but something does from the other end: worth flying over empty
                pilot.Credits -= here.Toll;
                clock += LegSeconds(here, there, hull, 0);
                AdvanceBoth(clock);
                (here, there) = (there, here);
            }
            else
            {
                clock += WaitSeconds;
                waited += WaitSeconds;
                AdvanceBoth(clock);
            }
            while (clock >= nextMark && nextMark <= end)
            {
                double hour = (nextMark - startClock) / 3600;
                career.LastHour = pilot.Credits - lastMark;
                lastMark = pilot.Credits;
                if (hour is 1 or 2 or 5 or 10 or 20)
                    marks.Add($"{hour:0} h {pilot.Credits}");
                nextMark += 3600;
            }
        }
        GD.Print($"  {title}: {string.Join(", ", marks)}; {runs} runs, {waited / 3600:0.0} h waiting, first paying run at " +
                 $"{(career.FirstPaying < 0 ? "never" : $"{career.FirstPaying:0.00} h")}, last hour {career.LastHour:+0;-0} (started with {before})");
        Reset();
        return career;
    }

    private void NoArbitrage()
    {
        // At any stock, a tonne bought and sold straight back loses
        bool safe = true;
        foreach (End end in new[] { _a, _b })
            foreach (MarketGood line in end.Goods)
                for (double stock = 1; stock <= Market.Full * line.Stock; stock += 1)
                {
                    long buy = Market.BuyPrice(line, stock);
                    long sell = Market.SellPrice(line, stock - 1);
                    if (buy >= 0 && sell >= buy)
                    {
                        safe = false;
                        GD.Print($"  {end.Name} {line.Good.Id} at {stock}: buy {buy}, sell back {sell}");
                    }
                }
        Check("buying a tonne and selling it straight back always loses, at any stock", safe);
    }

    private void Complementary()
    {
        var alone = new List<string>();
        foreach (string good in Goods())
        {
            MarketGood a = Line(_a, good), b = Line(_b, good);
            if (a == null || b == null || Math.Sign(Market.Net(good, _a.Goods, _a.Factories)) == Math.Sign(Market.Net(good, _b.Goods, _b.Factories)))
                alone.Add(good);
        }
        Check($"every good is made at one end and used at the other ({(alone.Count == 0 ? "all" : "not: " + string.Join(", ", alone))})", alone.Count == 0);
    }

    private void WorthCarrying()
    {
        double clock = SettledHours * 3600;
        Advance(clock);
        var dull = new List<string>();
        foreach (string good in Goods())
        {
            Run there = Try(_a, _b, good, _fighter, long.MaxValue / 2, clock), back = Try(_b, _a, good, _fighter, long.MaxValue / 2, clock);
            if ((there?.Profit ?? 0) <= 0 && (back?.Profit ?? 0) <= 0)
                dull.Add(good);
        }
        Reset();
        Check($"once settled, a full hold of every good pays its toll ({(dull.Count == 0 ? "all do" : "not: " + string.Join(", ", dull))})", dull.Count == 0);
    }

    private void WearsOut()
    {
        double clock = SettledHours * 3600;
        Advance(clock);
        var pilot = new PilotRecord { Credits = long.MaxValue / 2 };
        long start = pilot.Credits, first = 0;
        End here = _a, there = _b;
        int legs = 0;
        while (legs < MaxLegsToWearOut + 1)
        {
            Run run = Best(here, there, _fighter, pilot.Credits, clock);
            if (run == null || run.Profit <= 0)
                break;
            Market.Buy(here.Record, Line(here, run.Good), pilot, run.Tonnes, run.Tonnes, out int bought, out _);
            pilot.Credits -= here.Toll;
            Market.Sell(there.Record, Line(there, run.Good), pilot, bought, out _, out _);
            pilot.Ship.Cargo.Clear();
            if (legs == 0)
                first = run.Profit;
            legs++;
            (here, there) = (there, here);
        }
        Reset();
        GD.Print($"\nA settled market, no time passing: {legs} legs from {first} a leg, {pilot.Credits - start} credits in all before nothing pays");
        Check($"it can't be milked for ever with no time passing ({legs} legs, at most {MaxLegsToWearOut})", legs <= MaxLegsToWearOut);
    }

    // The goods that go by way of the chip works, none of them dealt in at both ends of the route
    private void ChipsChain()
    {
        var works = new StationRecord { Callsign = _works.Name };
        Market.Advance(works, _works.Goods, SettledHours * 3600, null, _works.Factories);
        Advance(SettledHours * 3600);
        foreach (var (from, fromGoods, fromRecord, to, toGoods, toRecord, good) in new[]
        {
            (_b.Name, _b.Goods, _b.Record, _works.Name, _works.Goods, works, "silicon"),
            (_b.Name, _b.Goods, _b.Record, _works.Name, _works.Goods, works, "copper"),
            (_works.Name, _works.Goods, works, _a.Name, _a.Goods, _a.Record, "chips"),
        })
        {
            MarketGood seller = fromGoods.First(g => g.Good.Id == good), buyer = toGoods.First(g => g.Good.Id == good);
            long buy = Market.BuyPrice(seller, fromRecord.Stock[good]), sell = Market.SellPrice(buyer, toRecord.Stock[good]);
            Check($"{good} from {from} ({fromRecord.Stock[good]:0} t, {buy}) sells at {to} ({toRecord.Stock[good]:0} t, {sell})", buy >= 0 && sell > buy);
        }
        Reset();
    }

    // ---- A run ----

    private Run Best(End from, End to, ShipSpec hull, long credits, double clock)
    {
        Run best = null;
        foreach (string good in Goods())
        {
            Run run = Try(from, to, good, hull, credits, clock);
            if (run != null && (best == null || run.Profit > best.Profit))
                best = run;
        }
        return best;
    }

    // A hold of the good from one end to the other, on copies of the records: the toll is set aside first, since it
    // is paid before the cargo can be sold
    private Run Try(End from, End to, string good, ShipSpec hull, long credits, double clock)
    {
        if (credits < from.Toll)
            return null;
        var seller = Copy(from.Record);
        var buyer = Copy(to.Record);
        var pilot = new PilotRecord { Credits = credits - from.Toll };
        int hold = hull.CargoCapacity;
        Market.Buy(seller, Line(from, good), pilot, hold, hold, out int bought, out long paid);
        if (bought == 0)
            return null;
        Market.Advance(buyer, to.Goods, clock + LegSeconds(from, to, hull, bought), null, to.Factories);
        Market.Sell(buyer, Line(to, good), pilot, bought, out _, out long earned);
        return new Run { Good = good, Tonnes = bought, Paid = paid, Earned = earned, Profit = earned - paid - from.Toll };
    }

    private double LegSeconds(End from, End to, ShipSpec hull, int tonnes)
    {
        float mass = hull.Mass + tonnes * CargoHold.KilogramsPerTonne;
        return HandlingSeconds + Flight(from, from.Station, from.Gate, hull, mass) + Flight(to, to.Gate, to.Station, hull, mass);
    }

    // At each point of the way the ship flies at the top its hull and generator give there
    private double Flight(End end, Vector3 a, Vector3 b, ShipSpec hull, float mass)
    {
        const float step = 200;
        double seconds = 0;
        float length = a.DistanceTo(b);
        for (float d = 0; d < length; d += step)
        {
            Vector3 at = a.Lerp(b, d / length);
            float density = 1;
            foreach ((Vector3 massAt, float reach) in end.Masses)
                density += reach * reach / Mathf.Max(massAt.DistanceSquaredTo(at), 1);
            float top = density < _generator.Thinning
                ? Mathf.Min(hull.TopSpeed(_generator.Push * hull.Mass / mass, hull.VeilDrag * density / _generator.Thinning), CruiseCap)
                : hull.TopSpeed(_engineThrust * hull.Mass / mass);
            if (end.Station.DistanceTo(at) < end.SpeedRadius)
                top = Mathf.Min(top, end.SpeedLimit);
            seconds += step / top;
        }
        return seconds;
    }

    // ---- The game's data ----

    private static End Read(string scene, string station, string gate)
    {
        Node root = GD.Load<PackedScene>(scene).Instantiate();
        var masses = new List<(Vector3, float)>();
        Masses(root, masses);
        Node stationNode = root.GetNode(station);
        StationLaws laws = stationNode.GetNode<StationRadio>("Radio").Laws;
        var end = new End
        {
            Name = station,
            Goods = stationNode.GetNode<StationMarket>("Market").Goods.Where(g => g?.Good != null).ToArray(),
            Factories = stationNode.GetNode<StationMarket>("Market").Factories,
            Station = At(stationNode as Node3D),
            Gate = At(root.GetNode<Node3D>(gate)),
            Masses = masses,
            SpeedRadius = laws.SpeedRadius,
            SpeedLimit = laws.SpeedLimit,
            Toll = root.GetNode(gate).GetNode<StationRadio>("Radio").Laws.Toll,
            Record = new StationRecord { Callsign = station },
        };
        root.Free();
        return end;
    }

    private static void Masses(Node node, List<(Vector3, float)> into)
    {
        if (node is MediumMass mass)
            into.Add((At(mass), mass.Reach));
        foreach (Node child in node.GetChildren())
            Masses(child, into);
    }

    // Out of the tree a node has no global position: its transforms up to the scene's root
    private static Vector3 At(Node3D node)
    {
        Transform3D t = node.Transform;
        for (Node up = node.GetParent(); up != null; up = up.GetParent())
            if (up is Node3D n)
                t = n.Transform * t;
        return t.Origin;
    }

    private IEnumerable<string> Goods() =>
        _a.Goods.Select(g => g.Good.Id).Union(_b.Goods.Select(g => g.Good.Id)).Where(g => Line(_a, g) != null && Line(_b, g) != null);

    private static MarketGood Line(End end, string good) => end.Goods.FirstOrDefault(g => g.Good.Id == good);

    private static double Stock(End end, string good) => end.Record.Stock.TryGetValue(good, out double s) ? s : 0;

    private static StationRecord Copy(StationRecord r) =>
        new() { Callsign = r.Callsign, UpdatedAt = r.UpdatedAt, Stock = new Dictionary<string, double>(r.Stock) };

    // Both markets as they stand in a new universe...
    private void Reset()
    {
        foreach (End end in new[] { _a, _b })
        {
            end.Record = new StationRecord { Callsign = end.Name };
            Market.Advance(end.Record, end.Goods, 0, null, end.Factories);
        }
    }

    // ...and as they stand so long after it, nobody trading
    private void Advance(double clock)
    {
        Reset();
        AdvanceBoth(clock);
    }

    private void AdvanceBoth(double clock)
    {
        Market.Advance(_a.Record, _a.Goods, clock, null, _a.Factories);
        Market.Advance(_b.Record, _b.Goods, clock, null, _b.Factories);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
