using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>What a pilot asks a station's market (<see cref="StationMarket"/>), as JSON on the "market" topic.</summary>
public sealed class MarketRequest
{
    public const string Look = "look", Buy = "buy", Sell = "sell", Pay = "pay", Surrender = "surrender", Factor = "factor";

    /// <summary>The station, by its callsign.</summary>
    public string Station { get; set; }
    public string Do { get; set; } = Look;
    /// <summary>The good to trade; for the factor, the station whose debts to settle.</summary>
    public string Good { get; set; }
    public int Tonnes { get; set; }
}

/// <summary>What the market shows a pilot: its goods and prices, and the pilot's money, hold and fines here.</summary>
public sealed class MarketView
{
    public string Station { get; set; }
    public bool Docked { get; set; }
    /// <summary>The pilot's ship is in one of the station's hangars: what is owed can be paid.</summary>
    public bool Present { get; set; }
    public long Credits { get; set; }
    public int Capacity { get; set; }
    public int Carried { get; set; }
    public long Fines { get; set; }
    public long Bounty { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long Owed => Fines + Bounty;
    /// <summary>The pilot's own ship is impounded here.</summary>
    public bool Impounded { get; set; }
    /// <summary>What came of the request, as the kiosk says it (empty for a look).</summary>
    public string Message { get; set; } = "";
    public List<MarketLine> Lines { get; set; } = new();
    /// <summary>A station with a factor: what the pilot owes other stations, and what the factor asks to settle it.</summary>
    public List<DebtLine> Elsewhere { get; set; } = new();
}

public sealed class DebtLine
{
    public string Station { get; set; }
    public long Owed { get; set; }
    public long Cost { get; set; }
}

public sealed class MarketLine
{
    public string Good { get; set; }
    public string Name { get; set; }
    /// <summary>What the next tonne costs the pilot (-1: none to sell).</summary>
    public long Buy { get; set; }
    /// <summary>What the market pays for the next tonne (-1: it takes no more).</summary>
    public long Sell { get; set; }
    public int Stock { get; set; }
    public int Held { get; set; }
}

/// <summary>
/// A station's market, beside its radio (<see cref="StationRadio"/>) in the station's scene: the goods it deals in
/// (<see cref="Goods"/>: what it wants to have, what it makes and uses) and the desk that serves pilots. The rules
/// are <see cref="Market"/>'s, on the station's record in the universe; only the universe's owner (the host) keeps
/// them, so a guest's kiosk asks the host (<see cref="Universe.Ask"/>, topic <see cref="Topic"/>) and shows the
/// view that comes back. Pilots trade only docked here, from their ship's hold. What they owe the station (fines,
/// bounties) is paid here too, by anyone whose ship is in a hangar, cleared or not (a notorious pilot who slipped in
/// pays as anyone); a pilot who can't pay a bounty surrenders here (<see cref="Justice.Surrender"/>), and paying
/// it all gets an impounded ship back (<see cref="Justice.Release"/>). A station with a <see cref="Factor"/> also
/// settles what pilots owe other stations, for <see cref="FactorSurcharge"/> more and no questions; the other way
/// out is to pay there in person (slipping in if they must), at face value. Neither takes infamy away.
/// </summary>
public partial class StationMarket : Node
{
    public const string Topic = "market";

    [Export] public MarketGood[] Goods = System.Array.Empty<MarketGood>();

    /// <summary>A disreputable factor works here: it pays other stations' fines and bounties for the pilot.</summary>
    [Export] public bool Factor;
    /// <summary>What the factor asks on top of what it pays (0.5: half as much again).</summary>
    [Export] public double FactorSurcharge = 0.5;

    [ExportGroup("Texts")]
    [Export] public string NotDockedText = "Dock here to trade.";
    [Export] public string RefusedText = "This station does not deal with you.";
    /// <summary>{0} tonnes, {1} the good, {2} credits.</summary>
    [Export] public string BoughtText = "Bought {0} t of {1} for {2} credits.";
    [Export] public string SoldText = "Sold {0} t of {1} for {2} credits.";
    [Export] public string NoStockText = "None left to sell.";
    [Export] public string MarketFullText = "The market takes no more of it.";
    [Export] public string NoMoneyText = "Not enough credits.";
    [Export] public string HoldFullText = "The hold is full.";
    [Export] public string NotHeldText = "None of it in the hold.";
    [Export] public string NotTradedText = "Not traded here.";
    [Export] public string NotPresentText = "Your ship must be in a hangar here.";
    /// <summary>{0} credits.</summary>
    [Export] public string PaidText = "Paid {0} credits. Nothing owed here.";
    [Export] public string ReleasedText = "Your ship is released.";
    [Export] public string NoFinesText = "Nothing owed here.";
    [Export] public string NotWantedText = "You are not wanted here.";
    /// <summary>{0} credits still owed.</summary>
    [Export] public string ImpoundedText = "Cargo seized and ship impounded until you pay {0} credits. Your insurer sends a courtesy ship.";
    [Export] public string SettledText = "Cargo seized. Your debt is paid.";
    [Export] public string NoFactorText = "No factor works here.";
    /// <summary>{0} the station, {1} credits.</summary>
    [Export] public string FactorText = "Your debts to {0} are settled, for {1} credits. Nobody asks where it came from.";

    public StationRadio Radio { get; private set; }

    private static readonly List<StationMarket> _all = new();

    public static StationMarket Of(string callsign) => _all.FirstOrDefault(m => m.Radio?.Sign == callsign);

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree()
    {
        _all.Add(this);
        Universe.Current?.Answer(Topic, Desk);
    }

    public override void _ExitTree() => _all.Remove(this);

    /// <summary>The universe's owner: a pilot's request, answered by the market it names.</summary>
    private static string Desk(string pilot, string text)
    {
        MarketRequest request;
        try
        {
            request = JsonSerializer.Deserialize<MarketRequest>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        return request != null && Of(request.Station) is StationMarket market
            ? JsonSerializer.Serialize(market.Serve(pilot, request))
            : null;
    }

    /// <summary>Does what the pilot asks, if it may be done, and shows them the market as it is after.</summary>
    public MarketView Serve(string pilotId, MarketRequest request)
    {
        Universe universe = Universe.Current;
        UniverseState state = universe.State;
        StationRecord station = state.Station(Radio.Sign);
        PilotRecord pilot = state.Pilot(pilotId);
        Market.Advance(station, Goods, state.Clock);
        Node3D ship = universe.ShipOf(pilotId);
        bool docked = Radio.DockedHere(ship), present = docked || Radio.Inside(ship);
        CrimeRecord crimes = station.CrimesOf(pilotId);
        string message = "";
        bool changed = false;

        if (request.Do != MarketRequest.Look)
        {
            MarketGood line = Goods.FirstOrDefault(g => g?.Good != null && g.Good.Id == request.Good);
            bool impounded = station.Impounded.ContainsKey(pilotId);
            if (request.Do is MarketRequest.Pay or MarketRequest.Surrender && !present)
                message = NotPresentText;
            else if (request.Do == MarketRequest.Pay)
            {
                long owed = crimes.Owed;
                if (owed == 0 && !impounded)
                    message = NoFinesText;
                else if (owed > 0 && !Justice.Pay(crimes, pilot))
                    message = NoMoneyText;
                else
                {
                    message = owed > 0 ? string.Format(PaidText, owed) : "";
                    if (Justice.Release(station, crimes, pilot))
                        message = (message + " " + ReleasedText).Trim();
                    changed = true;
                }
            }
            else if (request.Do == MarketRequest.Surrender)
            {
                if (!Justice.Wanted(crimes) || impounded)
                    message = NotWantedText;
                else
                {
                    impounded = Justice.Surrender(station, crimes, pilot, Seize);
                    message = impounded ? string.Format(ImpoundedText, crimes.Owed) : SettledText;
                    changed = true;
                }
            }
            else if (request.Do == MarketRequest.Factor)
                message = Settle(pilot, request.Good, present, ref changed);
            else if (!docked)
                message = NotDockedText;
            else if (Radio.Security?.Refuses(ship) == true)
                message = RefusedText;
            else if (line == null)
                message = NotTradedText;
            else if (request.Do == MarketRequest.Buy)
            {
                Market.Result result = Market.Buy(station, line, pilot, request.Tonnes, Capacity(ship, pilot), out int bought, out long paid);
                message = bought > 0 ? string.Format(BoughtText, bought, line.Good.Name, paid) : Why(result);
                changed = bought > 0;
            }
            else if (request.Do == MarketRequest.Sell)
            {
                Market.Result result = Market.Sell(station, line, pilot, request.Tonnes, out int sold, out long earned);
                message = sold > 0 ? string.Format(SoldText, sold, line.Good.Name, earned) : Why(result);
                changed = sold > 0;
            }
        }
        if (changed)
            universe.Touch(pilot);
        else
            universe.Touch();                       // the stock moved with time

        var view = new MarketView
        {
            Station = Radio.Sign, Docked = docked, Present = present, Credits = pilot.Credits, Capacity = Capacity(ship, pilot),
            Carried = pilot.Ship.Tonnes, Fines = crimes.Fines, Bounty = crimes.Bounty, Impounded = station.Impounded.ContainsKey(pilotId),
            Message = message,
        };
        if (Factor)
            foreach ((string callsign, StationRecord other) in state.Stations.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
                if (callsign != Radio.Sign && other.Crimes.TryGetValue(pilotId, out CrimeRecord owed) && owed.Owed > 0)
                    view.Elsewhere.Add(new DebtLine { Station = callsign, Owed = owed.Owed, Cost = FactorCost(owed.Owed) });
        foreach (MarketGood line in Goods.Where(g => g?.Good != null))
        {
            double stock = station.Stock.GetValueOrDefault(line.Good.Id);
            view.Lines.Add(new MarketLine
            {
                Good = line.Good.Id, Name = line.Good.Name, Buy = Market.BuyPrice(line, stock), Sell = Market.SellPrice(line, stock),
                Stock = (int)stock, Held = pilot.Ship.Holds(line.Good.Id),
            });
        }
        return view;
    }

    public long FactorCost(long owed) => (long)System.Math.Ceiling(owed * (1 + FactorSurcharge));

    // The factor settles what the pilot owes another station
    private string Settle(PilotRecord pilot, string station, bool present, ref bool changed)
    {
        if (!Factor)
            return NoFactorText;
        if (!present)
            return NotPresentText;
        if (station == Radio.Sign || !Universe.Current.State.Stations.TryGetValue(station ?? "", out StationRecord other) ||
            !other.Crimes.TryGetValue(pilot.Id, out CrimeRecord record) || record.Owed == 0)
            return NoFinesText;
        long cost = FactorCost(record.Owed);
        if (!pilot.Spend(cost))
            return NoMoneyText;
        record.Fines = record.Bounty = 0;
        changed = true;
        return string.Format(FactorText, station, cost);
    }

    /// <summary>Seized cargo: what the market buys of it at its prices, the rest at half its base price.</summary>
    private long Seize(ShipRecord ship)
    {
        StationRecord station = Universe.Current.State.Station(Radio.Sign);
        var holder = new PilotRecord { Ship = ship };
        foreach ((string id, int tonnes) in ship.Cargo.ToList())
            if (Goods.FirstOrDefault(g => g?.Good?.Id == id) is MarketGood line)
                Market.Sell(station, line, holder, tonnes, out _, out _);
        foreach ((string id, int tonnes) in ship.Cargo)
            holder.Credits += tonnes * (GoodsCatalog.Find(id)?.BasePrice ?? 0) / 2;
        ship.Cargo.Clear();
        return holder.Credits;
    }

    private string Why(Market.Result result) => result switch
    {
        Market.Result.NoStock => NoStockText,
        Market.Result.MarketFull => MarketFullText,
        Market.Result.NoMoney => NoMoneyText,
        Market.Result.HoldFull => HoldFullText,
        Market.Result.NotHeld => NotHeldText,
        _ => NotTradedText,
    };

    /// <summary>What the pilot's hold takes: the hull their record says (the ship here follows it).</summary>
    private static int Capacity(Node3D ship, PilotRecord pilot) =>
        ResourceLoader.Exists(pilot.Ship.Hull) ? GD.Load<ShipSpec>(pilot.Ship.Hull)?.CargoCapacity ?? 0
        : ship is ShipBody { Spec: ShipSpec spec } ? spec.CargoCapacity : 0;
}
