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
    public const string Look = "look", Buy = "buy", Sell = "sell", Pay = "pay", Surrender = "surrender", Factor = "factor", Acquire = "acquire", Refuel = "refuel", Repair = "repair", Offer = "offer";

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
    /// <summary>The station's name as people read it.</summary>
    public string Title { get; set; } = "";
    /// <summary>Who owns the station, by name.</summary>
    public string Owner { get; set; } = "";
    public bool Docked { get; set; }
    /// <summary>The station makes nothing for now.</summary>
    public bool Idle { get; set; }
    /// <summary>The pilot's ship is in one of the station's hangars: what is owed can be paid.</summary>
    public bool Present { get; set; }
    public long Credits { get; set; }
    public int Capacity { get; set; }
    public int Carried { get; set; }
    /// <summary>Hydrogen in the ship's tank and what it holds, tonnes, and what a tonne costs here (below 0: none sold here).</summary>
    public double Fuel { get; set; }
    public double Tank { get; set; }
    public long FuelPrice { get; set; } = -1;
    /// <summary>The pilot's hull was mended just now: the machine that flies the ship does it.</summary>
    public bool Repaired { get; set; }
    /// <summary>The station is the pilot's, and not up for sale yet.</summary>
    public bool CanOffer { get; set; }
    public long Fines { get; set; }
    public long Bounty { get; set; }
    /// <summary>What organisations offer for the pilot's head.</summary>
    public long Hunted { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long Owed => Fines + Bounty;
    /// <summary>The pilot's own ship is impounded here.</summary>
    public bool Impounded { get; set; }
    /// <summary>What came of the request, as the kiosk says it (empty for a look).</summary>
    public string Message { get; set; } = "";
    public List<MarketLine> Lines { get; set; } = new();
    /// <summary>A station with a factor: what the pilot owes other stations, and what the factor asks to settle it.</summary>
    public List<DebtLine> Elsewhere { get; set; } = new();
    /// <summary>Stations on sale anywhere: they are the talk of every market.</summary>
    public List<SaleLine> Sales { get; set; } = new();
    /// <summary>What the pilot knows of other stations' markets, newest first.</summary>
    public List<NewsLine> News { get; set; } = new();
}

/// <summary>A station on sale, and what it costs now.</summary>
public sealed class SaleLine
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Seller { get; set; } = "";
    public long Price { get; set; }
}

/// <summary>A good at another station, as the pilot last heard.</summary>
public sealed class NewsLine
{
    public string Title { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>How old the news is, in hours.</summary>
    public double Hours { get; set; }
    public int Stock { get; set; }
    public long Buy { get; set; }
    public long Sell { get; set; }
}

public sealed class DebtLine
{
    public string Station { get; set; }
    /// <summary>The station's name as people read it.</summary>
    public string Title { get; set; } = "";
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

/// <summary>What a station's arms dealer shows a pilot.</summary>
public sealed class ArmsView
{
    public string Station { get; set; }
    /// <summary>The station's name as people read it.</summary>
    public string Title { get; set; } = "";
    public bool Docked { get; set; }
    /// <summary>The station makes nothing for now.</summary>
    public bool Idle { get; set; }
    public long Credits { get; set; }
    /// <summary>What came of the request (empty for a look; with nothing on sale, says so).</summary>
    public string Message { get; set; } = "";
    public List<ArmsOffer> Offers { get; set; } = new();
}

public sealed class ArmsOffer
{
    public string Id { get; set; }
    public string Name { get; set; }
    /// <summary>What it is for, and what it can do in figures (<see cref="Outfit.About"/>, <see cref="Outfit.Details"/>).</summary>
    public string About { get; set; } = "";
    public string Details { get; set; } = "";
    public int Stock { get; set; }
    /// <summary>What the next one costs (-1: none to sell).</summary>
    public long Price { get; set; }
    public int Aboard { get; set; }
    public int Rack { get; set; }
    /// <summary>How many one Buy takes (<see cref="Outfit.Lot"/>).</summary>
    public int Lot { get; set; } = 1;
    /// <summary>The good its factory makes it of, and the tonnes of it the station has.</summary>
    public string MadeOf { get; set; } = "";
    public int MadeOfStock { get; set; }
    /// <summary>The dealer is short of it and the factory has nothing to make more with.</summary>
    public bool Starved { get; set; }
    /// <summary>A piece for a hardpoint (a laser, a turret): bought into the locker here, not loaded.</summary>
    public bool Piece { get; set; }
    /// <summary>A piece: how many are fitted to the ship, and how many wait in the locker here.</summary>
    public int Fitted { get; set; }
    public int Kept { get; set; }
    /// <summary>A piece: what the dealer pays for one from the locker (-1: it takes no more).</summary>
    public long SellPrice { get; set; } = -1;
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
    public const string Topic = "market", ArmsTopic = "arms";

    [Export] public MarketGood[] Goods = System.Array.Empty<MarketGood>();

    /// <summary>What the station's arms dealer sells, from its own factory (none: no arms dealer here).</summary>
    [Export] public OutfitLine[] Outfits = System.Array.Empty<OutfitLine>();

    /// <summary>The station's factories and mines: what it makes, from what, and how fast.</summary>
    [Export] public FactoryLine[] Factories = System.Array.Empty<FactoryLine>();

    /// <summary>The ships working for this station when a universe starts.</summary>
    [Export] public FleetLine[] Fleet = System.Array.Empty<FleetLine>();

    /// <summary>The ships its yard can build (none: no shipyard here).</summary>
    [Export] public ShipModel[] Shipyard = System.Array.Empty<ShipModel>();

    /// <summary>What running the station costs its owner an hour.</summary>
    [Export] public double UpkeepPerHour;

    /// <summary>What the station is worth, if it is ever sold.</summary>
    [Export] public long Worth = 2_000_000;

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
    [Export] public string NoCashText = "The station has no money to pay for it.";
    [Export] public string HoldFullText = "The hold is full.";
    [Export] public string NotHeldText = "None of it in the hold.";
    [Export] public string NotTradedText = "Not traded here.";
    [Export] public string NotPresentText = "You must be at this station: on foot here, or your ship in a hangar here.";
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
    [Export] public string OfferedText = "The station is up for sale.";
    /// <summary>{0} credits.</summary>
    [Export] public string RepairedText = "Hull mended, for {0} credits.";
    [Export] public string NoRepairText = "It can't be mended here: no metals or electronics to be had, or you can't pay for them.";
    /// <summary>{0} tonnes, {1} credits.</summary>
    [Export] public string RefuelledText = "{0} t of hydrogen in the tank, for {1} credits.";
    [Export] public string FuelNoneText = "No fuel to be had: the tank is full, there is none here, or you can't pay for it.";
    /// <summary>{0} the station.</summary>
    [Export] public string AcquiredText = "{0} is yours. Its upkeep is too.";
    [Export] public string GoneText = "It isn't for sale any more.";
    [Export] public string NoArmsText = "No arms are sold at this station.";
    /// <summary>{0} how many, {1} the model, {2} credits.</summary>
    [Export] public string LoadedText = "Loaded {0} {1} for {2} credits.";
    [Export] public string RackFullText = "The rack is full.";
    /// <summary>{0} the model, {1} credits.</summary>
    [Export] public string LockedText = "{0} bought for {1} credits. It waits in your locker, to be fitted.";
    [Export] public string RefitDockText = "Dock here to refit.";
    /// <summary>{0} the model, {1} credits.</summary>
    [Export] public string PieceSoldText = "{0} sold from your locker for {1} credits.";
    [Export] public string NoPieceText = "None of it in your locker here.";
    [Export] public string NoSuchText = "No such hardpoint.";
    [Export] public string EmptyText = "Nothing is fitted there.";
    [Export] public string NothingText = "Nothing there in your locker.";
    [Export] public string NotFitText = "It does not fit there.";
    [Export] public string OneTurretText = "The ship has one turret: take the other off first.";
    /// <summary>{0} missiles aboard, {1} rails there would be.</summary>
    [Export] public string RailsFullText = "The ship has {0} missiles aboard and would have rails for {1}: fire some first.";
    /// <summary>{0} the model, {1} the hardpoint.</summary>
    [Export] public string FittedText = "{0} fitted on {1}. The ship's computer restarts.";
    [Export] public string RemovedText = "{0} taken off {1}, into your locker. The ship's computer restarts.";

    public StationRadio Radio { get; private set; }

    private static readonly List<StationMarket> _all = new();

    public static StationMarket Of(string id) => _all.FirstOrDefault(m => m.Radio?.Id == id);

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree()
    {
        _all.Add(this);
        Universe.Current?.Answer(Topic, Desk);
        Universe.Current?.Answer(ArmsTopic, ArmsDesk);
        Universe.Current?.Answer(FittingDesk.Topic, FitDesk);
    }

    public override void _ExitTree() => _all.Remove(this);

    /// <summary>The universe's owner: a pilot's request, answered by the market it names.</summary>
    private static float Tank(PilotRecord pilot) =>
        ResourceLoader.Exists(pilot.Ship.Hull) ? Xpace.World.ResourceCache.Load<ShipSpec>(pilot.Ship.Hull)?.FuelTank ?? 0 : 0;

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
        StationRecord station = state.Station(Radio.Id);
        PilotRecord pilot = state.Pilot(pilotId);
        Market.Advance(station, Goods, state.Clock, Outfits, Factories, Radio.Cash, UpkeepPerHour);
        Node3D ship = universe.ShipOf(pilotId);
        bool docked = Radio.DockedHere(ship), present = docked || Radio.Inside(ship) || universe.OnFootAt(pilotId) == Radio.Id;
        CrimeRecord crimes = station.CrimesOf(pilotId);
        string message = "";
        bool changed = false;

        bool repaired = false;
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
                    Receive(owed);
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
                    long owed = crimes.Owed;
                    impounded = Justice.Surrender(station, crimes, pilot, Seize);
                    Receive(owed - crimes.Owed);
                    message = impounded ? string.Format(ImpoundedText, crimes.Owed) : SettledText;
                    changed = true;
                }
            }
            else if (request.Do == MarketRequest.Factor)
                message = Settle(pilot, request.Good, present, ref changed);
            else if (request.Do == MarketRequest.Refuel)
            {
                if (!docked)
                    message = NotDockedText;
                else if (Radio.Security?.Refuses(ship) == true)
                    message = RefusedText;
                else
                {
                    int tonnes = Market.Refuel(station, Goods, pilot, Tank(pilot), Radio.Cash, out long paid);
                    message = tonnes > 0 ? string.Format(RefuelledText, tonnes, paid) : FuelNoneText;
                    changed |= tonnes > 0;
                }
            }
            else if (request.Do == MarketRequest.Offer)
            {
                changed |= Solvency.Offer(Radio.Id, PilotRecord.OwnerPrefix + pilotId, universe.Book, state);
                message = changed ? OfferedText : "";
            }
            else if (request.Do == MarketRequest.Repair)
            {
                if (!docked)
                    message = NotDockedText;
                else if (Radio.Security?.Refuses(ship) == true)
                    message = RefusedText;
                else if (ResourceLoader.Exists(pilot.Ship.Hull) && Xpace.World.ResourceCache.Load<ShipSpec>(pilot.Ship.Hull) is ShipSpec hull)
                {
                    long cost = Market.Repair(station, Goods, hull, Mathf.Clamp(request.Tonnes, 0, 100) / 100f, pilot, Radio.Cash, long.MaxValue);
                    repaired = cost >= 0;
                    message = repaired ? string.Format(RepairedText, cost) : NoRepairText;
                    changed |= repaired;
                }
            }
            else if (request.Do == MarketRequest.Acquire)
            {
                if (!present)
                    message = NotPresentText;
                else if (!state.Sales.TryGetValue(request.Good ?? "", out SaleRecord sale) || sale.Kind != SaleRecord.Station)
                    message = GoneText;
                else if (!Solvency.Buy(sale, PilotRecord.OwnerPrefix + pilotId, state, universe.Book, universe.Account))
                    message = NoMoneyText;
                else
                {
                    message = string.Format(AcquiredText, universe.NameOf(sale.Item));
                    changed = true;
                }
            }
            else if (!docked)
                message = NotDockedText;
            else if (Radio.Security?.Refuses(ship) == true)
                message = RefusedText;
            else if (line == null)
                message = NotTradedText;
            else if (request.Do == MarketRequest.Buy)
            {
                Market.Result result = Market.Buy(station, line, pilot, request.Tonnes, Capacity(ship, pilot), out int bought, out long paid, Radio.Cash);
                message = bought > 0 ? string.Format(BoughtText, bought, line.Good.Name, paid) : Why(result);
                changed = bought > 0;
            }
            else if (request.Do == MarketRequest.Sell)
            {
                Market.Result result = Market.Sell(station, line, pilot, request.Tonnes, out int sold, out long earned, Radio.Cash);
                message = sold > 0 ? string.Format(SoldText, sold, line.Good.Name, earned) : Why(result);
                changed = sold > 0;
            }
        }
        if (universe.Book?.Find(Radio.Id) is StationBook.Station here)
            MarketNews.Learn(pilot, Radio.Id, MarketNews.Report(here, station, state.Clock));
        if (changed)
            universe.Touch(pilot);
        else
            universe.Touch();                       // the stock moved with time

        var view = new MarketView
        {
            Station = Radio.Id, Title = Radio.ShownName, Owner = Radio.Cash?.Name ?? "", Docked = docked, Idle = station.Idle, Repaired = repaired, CanOffer = station.Owner == PilotRecord.OwnerPrefix + pilotId && !state.Sales.ContainsKey($"{SaleRecord.Station}:{Radio.Id}"), Tank = Tank(pilot), Fuel = pilot.Ship.Fuel < 0 ? Tank(pilot) : pilot.Ship.Fuel, FuelPrice = Market.FuelPrice(station, Goods), Present = present, Credits = pilot.Credits, Capacity = Capacity(ship, pilot),
            Carried = pilot.Ship.Tonnes, Fines = crimes.Fines, Bounty = crimes.Bounty, Impounded = station.Impounded.ContainsKey(pilotId),
            Message = message, Hunted = Reactions.Hunted(state, pilotId),
        };
        foreach (SaleRecord sale in state.Sales.Values.Where(s => s.Kind == SaleRecord.Station).OrderBy(s => s.Id, System.StringComparer.Ordinal))
            view.Sales.Add(new SaleLine { Id = sale.Id, Title = universe.NameOf(sale.Item), Seller = universe.Account(sale.Seller)?.Name ?? "", Price = sale.PriceAt(state.Clock) });
        if (Factor)
            foreach ((string callsign, StationRecord other) in state.Stations.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
                if (callsign != Radio.Id && other.Crimes.TryGetValue(pilotId, out CrimeRecord owed) && owed.Owed > 0)
                    view.Elsewhere.Add(new DebtLine { Station = callsign, Title = Universe.Current.NameOf(callsign), Owed = owed.Owed, Cost = FactorCost(owed.Owed) });
        foreach ((string id, StationNews news) in pilot.Known.Where(kv => kv.Key != Radio.Id).OrderByDescending(kv => kv.Value.AsOf).ThenBy(kv => kv.Key, System.StringComparer.Ordinal))
            foreach (MarketGood line in universe.Book?.Find(id)?.Goods ?? System.Array.Empty<MarketGood>())
                if (news.Goods.TryGetValue(line.Good.Id, out GoodNews good))
                    view.News.Add(new NewsLine
                    {
                        Title = universe.NameOf(id), Name = line.Good.Name, Hours = (state.Clock - news.AsOf) / 3600, Stock = (int)good.Stock, Buy = good.Buy, Sell = good.Sell,
                    });
        foreach (MarketGood line in Goods.Where(g => g?.Good is { Traded: true }))
        {
            double stock = station.Stock.GetValueOrDefault(line.Good.Id);
            view.Lines.Add(new MarketLine
            {
                Good = line.Good.Id, Name = line.Good.Name, Buy = Market.BuyPrice(station, line), Sell = Market.SellPrice(station, line),
                Stock = (int)stock, Held = pilot.Ship.Holds(line.Good.Id),
            });
        }
        return view;
    }

    /// <summary>The universe's owner: a pilot's request at an arms kiosk, answered by the station it names.</summary>
    private static string ArmsDesk(string pilot, string text)
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
            ? JsonSerializer.Serialize(market.ServeArms(pilot, request))
            : null;
    }

    private static string FitDesk(string pilot, string text)
    {
        FittingRequest request;
        try
        {
            request = JsonSerializer.Deserialize<FittingRequest>(text);
        }
        catch (JsonException)
        {
            return null;
        }
        return request != null && Of(request.Station) is StationMarket market
            ? JsonSerializer.Serialize(FittingDesk.Serve(market, pilot, request))
            : null;
    }

    /// <summary>Does what the pilot asks of the arms dealer, if it may be done, and shows what it has after.</summary>
    public ArmsView ServeArms(string pilotId, MarketRequest request)
    {
        Universe universe = Universe.Current;
        UniverseState state = universe.State;
        StationRecord station = state.Station(Radio.Id);
        PilotRecord pilot = state.Pilot(pilotId);
        Market.Advance(station, Goods, state.Clock, Outfits, Factories, Radio.Cash, UpkeepPerHour);
        Node3D ship = universe.ShipOf(pilotId);
        bool docked = Radio.DockedHere(ship);
        string message = Outfits.Length == 0 ? NoArmsText : "";
        bool changed = false;
        if (request.Do is MarketRequest.Buy or MarketRequest.Sell)
        {
            OutfitLine line = Outfits.FirstOrDefault(o => o?.Item != null && o.Id == request.Good);
            if (line == null)
                message = NoArmsText;
            else if (!docked)
                message = NotDockedText;
            else if (Radio.Security?.Refuses(ship) == true)
                message = RefusedText;
            else if (Fittings.IsPiece(line.Item) && request.Do == MarketRequest.Sell)
            {
                Market.Result result = Market.SellPiece(station, line, pilot, station.LockerOf(pilotId), out long paid, Radio.Cash);
                message = result == Market.Result.Done ? string.Format(PieceSoldText, line.Item.Model, paid)
                    : result == Market.Result.NotHeld ? NoPieceText : Why(result);
                changed = result == Market.Result.Done;
            }
            else if (Fittings.IsPiece(line.Item))
            {
                Market.Result result = Market.BuyPiece(station, line, pilot, station.LockerOf(pilotId), out long paid, Radio.Cash);
                message = result == Market.Result.Done ? string.Format(LockedText, line.Item.Model, paid) : Why(result);
                changed = result == Market.Result.Done;
            }
            else
            {
                Market.Result result = Market.BuyOutfit(station, line, pilot, request.Tonnes, Universe.RackSize(pilot, line.Item), Universe.Factory(pilot, line.Item), out int bought, out long paid, Radio.Cash);
                message = bought > 0 ? string.Format(LoadedText, bought, line.Item.Model, paid) : Why(result);
                changed = bought > 0;
            }
        }
        if (changed)
            universe.Touch(pilot);
        else
            universe.Touch();

        var fitted = Fittings.Of(ResourceLoader.Exists(pilot.Ship.Hull) ? Xpace.World.ResourceCache.Load<ShipSpec>(pilot.Ship.Hull) : null, pilot.Ship);
        var view = new ArmsView { Station = Radio.Id, Title = Radio.ShownName, Docked = docked, Credits = pilot.Credits, Message = message };
        foreach (OutfitLine line in Outfits.Where(o => o?.Item != null))
        {
            double stock = station.Stock.GetValueOrDefault(line.Id);
            RecipePart scarcest = Scarcest(line, station);
            double input = scarcest != null ? station.Stock.GetValueOrDefault(Recipe.IdOf(scarcest.Item)) : 0;
            int rack = Universe.RackSize(pilot, line.Item);
            bool piece = Fittings.IsPiece(line.Item);
            view.Offers.Add(new ArmsOffer
            {
                Piece = piece,
                Fitted = piece ? fitted.Values.Count(m => m == line.Item || m.ResourcePath == line.Item.ResourcePath) : 0,
                Kept = piece ? station.LockerOf(pilotId).Count(i => i.Kind == ItemRecord.Piece && i.Data == line.Item.ResourcePath) : 0,
                SellPrice = piece ? Market.PieceSellPrice(station, line) : -1,
                Id = line.Id, Name = line.Item.Model, About = line.Item.About, Details = line.Item.Details,
                Stock = (int)stock, Price = Market.OutfitPrice(station, line), Aboard = Universe.Aboard(pilot, line.Item), Rack = rack,
                Lot = System.Math.Max(1, line.Item.Lot),
                MadeOf = Recipe.NameOf(scarcest?.Item), MadeOfStock = (int)input,
                Starved = scarcest != null && stock < line.Stock && input < scarcest.Amount,
            });
        }
        return view;
    }

    // Of what the factory makes this outfit from, what the station has the fewest runs of (none: no factory here, or a mine)
    private RecipePart Scarcest(OutfitLine line, StationRecord station) =>
        Factories.FirstOrDefault(f => Recipe.IdOf(f?.Recipe?.Output) == line.Id)?.Recipe.Inputs.Where(p => p?.Amount > 0)
                 .OrderBy(p => station.Stock.GetValueOrDefault(Recipe.IdOf(p.Item)) / p.Amount).FirstOrDefault();

    /// <summary>Whether its market deals in <paramref name="good"/>.</summary>
    public bool Deals(Good good) => good != null && Goods.Any(g => g?.Good?.Id == good.Id);

    /// <summary>The universe's owner: a ship of the station's own hands over tonnes of a good; what the market has no room for is lost.</summary>
    public void Deliver(Good good, double tonnes)
    {
        if (Universe.Current is not Universe universe || Goods.FirstOrDefault(g => g?.Good?.Id == good?.Id) is not MarketGood line)
            return;
        StationRecord station = universe.State.Station(Radio.Id);
        Market.Advance(station, Goods, universe.State.Clock, Outfits, Factories, Radio.Cash, UpkeepPerHour);
        station.Stock[line.Good.Id] = System.Math.Min(Market.Full * line.Stock, station.Stock.GetValueOrDefault(line.Good.Id) + tonnes);
        universe.Touch();
    }

    public long FactorCost(long owed) => (long)System.Math.Ceiling(owed * (1 + FactorSurcharge));

    // The factor settles what the pilot owes another station
    private string Settle(PilotRecord pilot, string station, bool present, ref bool changed)
    {
        if (!Factor)
            return NoFactorText;
        if (!present)
            return NotPresentText;
        if (station == Radio.Id || !Universe.Current.State.Stations.TryGetValue(station ?? "", out StationRecord other) ||
            !other.Crimes.TryGetValue(pilot.Id, out CrimeRecord record) || record.Owed == 0)
            return NoFinesText;
        long cost = FactorCost(record.Owed);
        if (!pilot.Spend(cost))
            return NoMoneyText;
        // The station owed gets what it was owed, the factor's house what it asks on top
        if (StationMarket.Of(station)?.Radio.Cash is IAccount owed)
            owed.Credits += record.Owed;
        Receive(cost - record.Owed);
        record.Fines = record.Bounty = 0;
        changed = true;
        return string.Format(FactorText, Universe.Current.NameOf(station), cost);
    }

    /// <summary>Seized cargo: what the market buys of it at its prices, the rest at half its base price.</summary>
    private long Seize(ShipRecord ship)
    {
        StationRecord station = Universe.Current.State.Station(Radio.Id);
        var holder = new PilotRecord { Ship = ship };
        foreach ((string id, int tonnes) in ship.Cargo.ToList())
            if (Goods.FirstOrDefault(g => g?.Good?.Id == id) is MarketGood line)
                Market.Sell(station, line, holder, tonnes, out _, out _, Radio.Cash);
        foreach ((string id, int tonnes) in ship.Cargo)
            holder.Credits += tonnes * (GoodsCatalog.Find(id)?.BasePrice ?? 0) / 2;
        ship.Cargo.Clear();
        return holder.Credits;
    }

    // Money paid to the station goes to its owner
    private void Receive(long credits)
    {
        if (Radio.Cash is IAccount cash)
            cash.Credits += credits;
    }

    private string Why(Market.Result result) => result switch
    {
        Market.Result.NoCash => NoCashText,
        Market.Result.NoStock => NoStockText,
        Market.Result.MarketFull => MarketFullText,
        Market.Result.NoMoney => NoMoneyText,
        Market.Result.HoldFull => HoldFullText,
        Market.Result.RackFull => RackFullText,
        Market.Result.NotHeld => NotHeldText,
        _ => NotTradedText,
    };

    /// <summary>What the pilot's hold takes: the hull their record says (the ship here follows it).</summary>
    private static int Capacity(Node3D ship, PilotRecord pilot) =>
        ResourceLoader.Exists(pilot.Ship.Hull) ? Xpace.World.ResourceCache.Load<ShipSpec>(pilot.Ship.Hull)?.CargoCapacity ?? 0
        : ship is ShipBody { Spec: ShipSpec spec } ? spec.CargoCapacity : 0;
}
