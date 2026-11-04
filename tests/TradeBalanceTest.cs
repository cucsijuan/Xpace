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
/// The markets are the universe's own, with its cargo ships at work (<see cref="CargoShips"/>) and no scene loaded.
///
/// It prints:
///   - each good's price on both ends, in a new universe and once the markets have settled;
///   - the best run in each direction as the universe ages, with a new pilot's money and with a full hold;
///   - careers: a pilot who always carries what pays best on this leg, and waits when nothing pays (credits against
///     hours played, when the first paying run comes, what an hour earns at the end);
///   - what a settled market pays before a pilot wears the difference out.
///
/// It fails when the economy breaks the targets: buying and selling back gains at some stock; a good with no maker
/// or no user across the gate; too few goods worth carrying once settled; an owner losing money just working; a new pilot waiting too long to earn, or
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
    private const double MinPayingShare = 0.5;

    /// <summary>A leg's time at the pilot's hands: launch and door, spooling, approach, docking, the walk to the kiosk and back.</summary>
    private const double HandlingSeconds = 240;
    private const double WaitSeconds = 600;
    private const double SettledHours = 24;
    private const double CareerHours = 20;
    private const double OwnerHours = 100;
    private const double WorkStepSeconds = 30;

    private int _failures;
    private End _a, _b, _mines, _works;
    private StationBook _book;
    private UniverseState _state;
    private ShipSpec _fighter, _courtesy;
    private GeneratorSpec _generator;
    private float _engineThrust;

    /// <summary>One end of the route: a station's market and the way from it to its sector's gate.</summary>
    private sealed class End
    {
        public string Name;
        public string Sign;
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
        _a = Read("res://scenes/sectors/sector_01.tscn", "MiltecAlpha", "Gate2");
        _b = Read("res://scenes/sectors/sector_02.tscn", "Drydock", "Gate1");
        _mines = Read("res://scenes/sectors/sector_02.tscn", "Kestrel", "Gate1");
        _works = Read("res://scenes/sectors/sector_01.tscn", "Quartz", "Gate2");
        _book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        Reset();

        Route();
        Prices();
        Ageing();
        Careers();
        NoArbitrage();
        Complementary();
        WorthCarrying();
        WearsOut();
        ChipsChain();
        Owners();

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
                long ab = Market.BuyPrice(_a.Record, Line(_a, good)), bp = Market.SellPrice(Line(_b, good), Stock(_b, good));
                long ba = Market.BuyPrice(_b.Record, Line(_b, good)), ap = Market.SellPrice(Line(_a, good), Stock(_a, good));
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
        Check($"a career earns between {MinCreditsPerHour:0} and {MaxCreditsPerHour:0} credits an hour over its last 5 hours ({career.LastHour:0})",
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
        long before = credits;
        var marks = new List<string>();
        var hours = new List<long> { credits };
        while (clock < end)
        {
            there = Across(here).OrderByDescending(e => Best(here, e, hull, pilot.Credits, clock)?.Profit ?? long.MinValue).First();
            Run run = Best(here, there, hull, pilot.Credits, clock);
            if (run != null && run.Profit > 0)
            {
                Market.Buy(here.Record, Line(here, run.Good), pilot, run.Tonnes, run.Tonnes, out int bought, out _, Account(_state.Station(here.Sign).Owner));
                pilot.Credits -= Toll(here, there);
                clock += LegSeconds(here, there, hull, bought);
                AdvanceBoth(clock);
                Market.Sell(there.Record, Line(there, run.Good), pilot, bought, out _, out _, Account(_state.Station(there.Sign).Owner));
                pilot.Ship.Cargo.Clear();
                runs++;
                if (career.FirstPaying < 0)
                    career.FirstPaying = (clock - startClock) / 3600;
                (here, there) = (there, here);
            }
            else if (Best(there, here, hull, pilot.Credits - Toll(here, there), clock + LegSeconds(here, there, hull, 0)) is { Profit: > 0 } back &&
                     back.Profit > Toll(here, there) && pilot.Credits >= Toll(here, there))
            {
                // Nothing pays from here, but something does from the other end: worth flying over empty
                pilot.Credits -= Toll(here, there);
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
                hours.Add(pilot.Credits);
                career.LastHour = (hours[^1] - hours[Math.Max(0, hours.Count - 6)]) / (double)Math.Min(5, hours.Count - 1);
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
        foreach (string good in _book.Stations.SelectMany(s => s.Goods).Select(g => g.Good.Id).Distinct())
            if (!_book.Stations.Any(s => s.Factories.Any(f => Recipe.IdOf(f?.Recipe?.Output) == good)) ||
                !_book.Stations.Any(s => s.Goods.Any(g => g.Good.Id == good && g.Uses > 0) || s.Factories.Any(f => f?.Recipe?.Inputs.Any(p => Recipe.IdOf(p?.Item) == good) == true)))
                alone.Add(good);
        Check($"every good is made somewhere and used somewhere ({(alone.Count == 0 ? "all" : "not: " + string.Join(", ", alone))})", alone.Count == 0);
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
        int goods = Goods().Count();
        Check($"once settled, the cargo ships leave work for pilots: most goods pay a hold's toll ({goods - dull.Count} of {goods}{(dull.Count == 0 ? "" : "; not " + string.Join(", ", dull))})",
              goods - dull.Count >= goods * MinPayingShare);
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
        Advance(SettledHours * 3600);
        StationRecord works = _state.Station(_works.Sign);
        foreach (var (from, fromGoods, fromRecord, to, toGoods, toRecord, good) in new[]
        {
            (_mines.Name, _mines.Goods, _state.Station(_mines.Sign), _works.Name, _works.Goods, works, "silicon"),
            (_mines.Name, _mines.Goods, _state.Station(_mines.Sign), _works.Name, _works.Goods, works, "copper"),
            (_works.Name, _works.Goods, works, _a.Name, _a.Goods, _a.Record, "chips"),
        })
        {
            MarketGood seller = fromGoods.First(g => g.Good.Id == good), buyer = toGoods.First(g => g.Good.Id == good);
            long buy = Market.BuyPrice(fromRecord, seller), sell = Market.SellPrice(buyer, toRecord.Stock[good]);
            Check($"{good} from {from} ({fromRecord.Stock[good]:0} t, {buy}) sells at {to} ({toRecord.Stock[good]:0} t, {sell})", buy >= 0 && sell > buy);
        }
        Reset();
    }

    // What each owner has after so long with no pilot trading: its people pay, its ships buy, its upkeep costs
    private void Owners()
    {
        GD.Print($"\nOwners, no pilot trading (credits after {OwnerHours:0} h):");
        Advance(OwnerHours * 3600);
        foreach (OrganisationRecord owner in _state.Organisations.Values.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            long start = OrganisationCatalog.Find(owner.Id).StartingCredits;
            GD.Print($"  {owner.Name}: {start} to {owner.Credits}");
            Check($"{owner.Name} doesn't lose money just working", owner.Credits >= start);
        }
        Reset();
    }

    // ---- A run ----

    private Run Best(End from, End to, ShipSpec hull, long credits, double clock)
    {
        Run best = null;
        foreach (string good in Shared(from, to))
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
        long toll = Toll(from, to);
        if (credits < toll)
            return null;
        var seller = Copy(from.Record);
        var buyer = Copy(to.Record);
        var pilot = new PilotRecord { Credits = credits - toll };
        int hold = hull.CargoCapacity;
        Market.Buy(seller, Line(from, good), pilot, hold, hold, out int bought, out long paid);
        if (bought == 0)
            return null;
        Market.Advance(buyer, to.Goods, clock + LegSeconds(from, to, hull, bought), null, to.Factories);
        Market.Sell(buyer, Line(to, good), pilot, bought, out _, out long earned);
        return new Run { Good = good, Tonnes = bought, Paid = paid, Earned = earned, Profit = earned - paid - toll };
    }

    private double LegSeconds(End from, End to, ShipSpec hull, int tonnes)
    {
        float mass = hull.Mass + tonnes * CargoHold.KilogramsPerTonne;
        if (from.Gate == to.Gate)
            return HandlingSeconds + Flight(from, from.Station, to.Station, hull, mass);
        return HandlingSeconds + Flight(from, from.Station, from.Gate, hull, mass) + Flight(to, to.Gate, to.Station, hull, mass);
    }

    private double Flight(End end, Vector3 a, Vector3 b, ShipSpec hull, float mass)
    {
        var space = new Voyage.Space { Masses = end.Masses, Limits = { (end.Station, end.SpeedRadius, end.SpeedLimit) } };
        return Voyage.Seconds(space, a, b, hull, mass, _generator, _engineThrust);
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
            Sign = stationNode.GetNode<StationRadio>("Radio").Id,
            Goods = stationNode.GetNode<StationMarket>("Market").Goods.Where(g => g?.Good != null).ToArray(),
            Factories = stationNode.GetNode<StationMarket>("Market").Factories,
            Station = At(stationNode as Node3D),
            Gate = At(root.GetNode<Node3D>(gate)),
            Masses = masses,
            SpeedRadius = laws.SpeedRadius,
            SpeedLimit = laws.SpeedLimit,
            Toll = root.GetNode(gate).GetNode<StationRadio>("Radio").Laws.Toll,
            Record = new StationRecord { Id = station },
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

    private static IEnumerable<string> Shared(End from, End to) =>
        from.Goods.Select(g => g.Good.Id).Where(g => Line(to, g) != null);

    // The stations it can go to from this one
    private IEnumerable<End> Across(End here) => new[] { _a, _b, _mines }.Where(e => e != here);

    // In the same sector there is no gate to pay for
    private static long Toll(End from, End to) => from.Gate == to.Gate ? 0 : from.Toll;

    private IEnumerable<string> Goods() =>
        _a.Goods.Select(g => g.Good.Id).Union(_b.Goods.Select(g => g.Good.Id)).Where(g => Line(_a, g) != null && Line(_b, g) != null);

    private static MarketGood Line(End end, string good) => end.Goods.FirstOrDefault(g => g.Good.Id == good);

    private static double Stock(End end, string good) => end.Record.Stock.TryGetValue(good, out double s) ? s : 0;

    private static StationRecord Copy(StationRecord r) =>
        new() { Id = r.Id, UpdatedAt = r.UpdatedAt, Stock = new Dictionary<string, double>(r.Stock), Floors = new Dictionary<string, double>(r.Floors) };

    // The universe as it stands when new: the game's own stations, owners and cargo ships...
    private void Reset()
    {
        _state = new UniverseState();
        CargoShips.Muster(_state, _book, Account);
        CargoShips.Tick(_state, _book, Account);
        foreach (End end in new[] { _a, _b, _mines })
            end.Record = _state.Station(end.Sign);
    }

    private IAccount Account(string owner) => string.IsNullOrEmpty(owner) ? null : _state.Organisation(owner, () =>
    {
        Organisation spec = OrganisationCatalog.Find(owner);
        return new OrganisationRecord { Id = owner, Name = spec.Name, Credits = spec.StartingCredits, Margin = spec.Personality?.Margin ?? 0.15 };
    });

    // ...and as it stands so long after it, its ships at work and no pilot trading
    private void Advance(double clock)
    {
        Reset();
        AdvanceBoth(clock);
    }

    private void AdvanceBoth(double clock)
    {
        while (_state.Clock < clock)
        {
            _state.Clock = Math.Min(clock, _state.Clock + WorkStepSeconds);
            CargoShips.Tick(_state, _book, Account);
        }
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
