using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>
/// A piece of work an owner offers on a station's contract board (<see cref="Contracts"/>): who gives it and pays it,
/// who may take it (empty: anyone), what is to be done and what it pays. The reward is set aside from the giver's money
/// when it is posted (<see cref="Escrow"/>): it is paid to the taker when it is done and goes back to the giver if it
/// is never done.
/// Kinds: <see cref="Deliver"/> (bring tonnes of a good of one's own to a station), <see cref="Haul"/> (collect the
/// giver's cargo at one station, the giver buying it there, and hand it over at another; carried, it is consigned:
/// not the taker's to sell, <see cref="ShipRecord.Consigned"/>), <see cref="Visit"/> (be at a place), <see cref="Recover"/>
/// (scoop the giver's lost cargo out of space at a wreck, <see cref="Wreck"/>, and hand it over: scooped by the taker it
/// is consigned, not stolen), <see cref="Report"/> (tell the giver, at its terminal, what was found out: <see cref="Answer"/>).
/// </summary>
public sealed class ContractRecord
{
    public const string Deliver = "deliver", Haul = "haul", Visit = "visit", Recover = "recover", Report = "report";
    public const string Offered = "offered", Taken = "taken", Done = "done", Failed = "failed", Expired = "expired";

    public string Id { get; set; } = "";
    public string Kind { get; set; } = Deliver;
    /// <summary>Who offers and pays it: an owner's id (<see cref="StationRecord.Owner"/>).</summary>
    public string Giver { get; set; } = "";
    /// <summary>The station whose board offers it.</summary>
    public string Station { get; set; } = "";
    /// <summary>The only actor who may take it (empty: anyone).</summary>
    public string For { get; set; } = "";
    public string Title { get; set; } = "";
    public string Briefing { get; set; } = "";
    public string Good { get; set; } = "";
    public int Tonnes { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Sector { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Radius { get; set; }
    public long Reward { get; set; }
    /// <summary>The reward, set aside from the giver's money.</summary>
    public long Escrow { get; set; }
    public double Posted { get; set; }
    /// <summary>The offer goes from the board then (0: it stays).</summary>
    public double Expires { get; set; }
    /// <summary>Seconds a taker has to do it (0: no limit).</summary>
    public double Allowed { get; set; }
    public string State { get; set; } = Offered;
    public string TakenBy { get; set; } = "";
    public double TakenAt { get; set; }
    /// <summary>A haul's tonnes collected so far, and tonnes handed over.</summary>
    public int Loaded { get; set; }
    public int Delivered { get; set; }
    public double Ended { get; set; }
    /// <summary>The story chain and step it belongs to (empty: none).</summary>
    public string Story { get; set; } = "";
    public int Step { get; set; }
    public string Lead { get; set; } = "";
    public string Guide { get; set; } = "";
    /// <summary>The callsign of a wreck at the contract's place while it is taken (empty: none; <see cref="Xpace.Space.StorySites"/>).</summary>
    public string Wreck { get; set; } = "";
    /// <summary>A thing found at the wreck (a flight recorder's diskette); null: none.</summary>
    public ItemRecord Item { get; set; }
    /// <summary>A report's answer.</summary>
    public string Answer { get; set; } = "";

    /// <summary>Cargo of it is carried for the giver, consigned.</summary>
    public bool Carried => Kind is Haul or Recover;

    public bool Open => State is Offered or Taken;
    public double Due => Allowed > 0 && TakenAt > 0 ? TakenAt + Allowed : 0;
}

/// <summary>A contract as the pilot's journal shows it (<see cref="PilotRecord.Journal"/>).</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Giver { get; set; } = "";
    public string State { get; set; } = "";
    /// <summary>What there is to do now, in a line.</summary>
    public string Objective { get; set; } = "";
    public string Briefing { get; set; } = "";
    /// <summary>The help for where it stands now (the lead while offered, the guide once taken), and its key for <see cref="PilotRecord.GuidesRead"/>.</summary>
    public string Help { get; set; } = "";
    public string HelpKey { get; set; } = "";
    public long Reward { get; set; }
    /// <summary>Seconds left to do it (below 0: no limit).</summary>
    public double Left { get; set; } = -1;
    public bool Story { get; set; }
    /// <summary>The wreck to find, if any (its distress beacon's callsign).</summary>
    public string Wreck { get; set; } = "";
}

/// <summary>
/// The contract boards, the same rules for every giver and every taker (docs/universe.md "Contracts"): an owner posts
/// work and pays for it from its own money; anyone docked at the station may take an offer meant for anyone (or for
/// them); what counts as done is what happened: the cargo handed over at the station, the place reached. Stations short
/// of what they use post deliveries by <see cref="Rules"/>; story chains (<see cref="Story"/>) offer their steps to one
/// pilot each. Run by the universe's owner; a guest asks it (topic <see cref="Topic"/>).
/// </summary>
public static class Contracts
{
    public const string Topic = "contracts";

    public static ContractRules Rules = new();
    public static StoryChain[] Story = Array.Empty<StoryChain>();

    /// <summary>Actors whose contracts changed since the universe last looked (it rebuilds their journals).</summary>
    public static readonly HashSet<string> Touched = new();

    private const int JournalDone = 12;
    public const string DidMyWork = "did my work", LetMeDown = "let me down";

    // --- Posting ---

    /// <summary>Posts a contract, setting its reward aside from the giver's money. Null if the giver hasn't it.</summary>
    public static ContractRecord Post(UniverseState state, ContractRecord contract, IAccount giver)
    {
        if (giver == null || contract.Reward < 0 || giver.Credits < contract.Reward)
            return null;
        giver.Credits -= contract.Reward;
        contract.Escrow = contract.Reward;
        contract.Id = $"C{++state.ContractsEver}";
        contract.Posted = state.Clock;
        contract.State = ContractRecord.Offered;
        state.Contracts[contract.Id] = contract;
        if (contract.For != "")
            Touched.Add(contract.For);
        return contract;
    }

    /// <summary>What the board at <paramref name="station"/> offers <paramref name="actor"/>.</summary>
    public static IEnumerable<ContractRecord> Offers(UniverseState state, string station, string actor, Func<string, IAccount> account) =>
        state.Contracts.Values.Where(c => c.State == ContractRecord.Offered && c.Station == station && (c.For == "" || c.For == actor) && !Refuses(account(c.Giver), actor, state.Clock))
            .OrderByDescending(c => c.For == actor).ThenBy(c => c.Posted);

    /// <summary>What the actor has taken and not finished.</summary>
    public static IEnumerable<ContractRecord> Taken(UniverseState state, string actor) =>
        state.Contracts.Values.Where(c => c.State == ContractRecord.Taken && c.TakenBy == actor);

    private static bool Refuses(IAccount giver, string actor, double clock) =>
        giver is IRemembers memory && Memories.Feeling(memory, actor, clock) < Rules.RefuseBelow;

    // --- Taking and doing ---

    /// <summary>The actor takes an offer at the station they are docked at. Why not, or null if taken.</summary>
    public static string Take(UniverseState state, string id, string actor, string dockedAt, Func<string, IAccount> account)
    {
        if (!state.Contracts.TryGetValue(id, out ContractRecord c) || c.State != ContractRecord.Offered || c.For != "" && c.For != actor || Refuses(account(c.Giver), actor, state.Clock))
            return "That offer is no longer on the board.";
        if (dockedAt != c.Station)
            return "Take it at the station that offers it, docked there.";
        c.State = ContractRecord.Taken;
        c.TakenBy = actor;
        c.TakenAt = state.Clock;
        Touched.Add(actor);
        return null;
    }

    /// <summary>
    /// A haul's taker, docked at its pick-up station, collects the giver's cargo: the giver buys it at that market and it
    /// goes into the hold, consigned, as far as the hold has room and the giver money. Why nothing, or null.
    /// </summary>
    public static string Collect(UniverseState state, StationBook book, string id, string actor, ShipRecord hold, int room, string dockedAt, Func<string, IAccount> account)
    {
        if (!state.Contracts.TryGetValue(id, out ContractRecord c) || c.State != ContractRecord.Taken || c.TakenBy != actor || c.Kind != ContractRecord.Haul)
            return "There is nothing to collect for that.";
        if (dockedAt != c.From)
            return $"The cargo is collected at {NameOf(state, book, c.From)}.";
        int wanted = Math.Min(c.Tonnes - c.Loaded, room);
        if (c.Tonnes - c.Loaded <= 0)
            return "All of it is aboard already.";
        if (wanted <= 0)
            return "The hold has no room for it.";
        if (book.Find(c.From) is not StationBook.Station from || from.Goods.FirstOrDefault(g => g.Good.Id == c.Good) is not MarketGood line)
            return "This market doesn't deal in it.";
        StationRecord market = state.Station(from.Id);
        Market.Advance(market, from.Goods, state.Clock, from.Outfits, from.Factories, account(CargoShips.OwnerOf(state, from)), from.Upkeep);
        int bought = Market.Take(market, line, account(c.Giver), wanted, account(CargoShips.OwnerOf(state, from)));
        if (bought <= 0)
            return "None to be had here for now.";
        hold.Consign(c.Good, bought);
        c.Loaded += bought;
        Touched.Add(actor);
        return bought < wanted ? $"Collected {bought} t: no more to be had for now." : null;
    }

    /// <summary>
    /// The taker, docked at the hand-over station, hands over what the contract asks: a haul's consigned cargo, or a
    /// delivery's tonnes of their own. The cargo goes into the station's stock (its owner pays the giver for it, if it
    /// is someone else); all of it handed over, the reward is the taker's. Why nothing, or null.
    /// </summary>
    public static string HandOver(UniverseState state, StationBook book, string id, string actor, ShipRecord hold, IAccount taker, string dockedAt, Func<string, IAccount> account)
    {
        if (!state.Contracts.TryGetValue(id, out ContractRecord c) || c.State != ContractRecord.Taken || c.TakenBy != actor || c.Kind is ContractRecord.Visit or ContractRecord.Report)
            return "There is nothing to hand over for that.";
        if (dockedAt != c.To)
            return $"It is handed over at {NameOf(state, book, c.To)}.";
        int have = c.Carried ? hold.ConsignedOf(c.Good) : hold.Clean(c.Good);
        int giving = Math.Min(have, (c.Carried ? c.Loaded : c.Tonnes) - c.Delivered);
        if (giving <= 0)
            return c.Carried ? "The cargo isn't aboard." : $"There is no {GoodName(c.Good)} of yours aboard.";
        if (c.Carried)
            hold.Consign(c.Good, -giving);
        else
            hold.Load(c.Good, -giving);
        if (book.Find(c.To) is StationBook.Station to && to.Goods.FirstOrDefault(g => g.Good.Id == c.Good) is MarketGood line)
            Market.Hand(state.Station(to.Id), line, giving, account(c.Giver), account(CargoShips.OwnerOf(state, to)));
        c.Delivered += giving;
        Touched.Add(actor);
        if (c.Delivered >= c.Tonnes)
            Finish(state, c, taker, account);
        return null;
    }

    /// <summary>
    /// The actor scooped tonnes of a good that were <paramref name="owner"/>'s out of space: as much of it as the actor
    /// is recovering for that owner is consigned (<see cref="Recover"/>); what of it that is, the rest being stolen.
    /// </summary>
    public static int Scooped(UniverseState state, string actor, string owner, string good, int tonnes)
    {
        int consigned = 0;
        foreach (ContractRecord c in Taken(state, actor).Where(c => c.Kind == ContractRecord.Recover && c.Giver == owner && c.Good == good))
        {
            int n = Math.Min(tonnes - consigned, c.Tonnes - c.Loaded);
            if (n <= 0)
                continue;
            c.Loaded += n;
            consigned += n;
            Touched.Add(actor);
        }
        return consigned;
    }

    /// <summary>The taker tells the giver, at its station, what they found out: a report's answer. Why it isn't done, or null.</summary>
    public static string Tell(UniverseState state, string id, string actor, string text, string at, IAccount taker, Func<string, IAccount> account)
    {
        if (!state.Contracts.TryGetValue(id, out ContractRecord c) || c.State != ContractRecord.Taken || c.TakenBy != actor || c.Kind != ContractRecord.Report)
            return "There is nothing to report for that.";
        if (at != c.Station)
            return $"Report it at {NameOf(state, null, c.Station)}.";
        if (Plain(text) != Plain(c.Answer))
            return "That doesn't match anything we know. Look again.";
        Finish(state, c, taker, account);
        return null;
    }

    private static string Plain(string s) => new((s ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The actor's ship is at <paramref name="at"/> in <paramref name="sector"/>: a visit there is done.</summary>
    public static void Reached(UniverseState state, string actor, string sector, Vector3 at, IAccount taker, Func<string, IAccount> account)
    {
        foreach (ContractRecord c in Taken(state, actor).Where(c => c.Kind == ContractRecord.Visit && c.Sector == sector).ToList())
            if (new Vector3((float)c.X, (float)c.Y, (float)c.Z).DistanceTo(at) <= c.Radius)
                Finish(state, c, taker, account);
    }

    /// <summary>The actor gives it up: the giver has its money back and remembers; cargo carried for it is kept, stolen.</summary>
    public static string Abandon(UniverseState state, string id, string actor, ShipRecord hold, Func<string, IAccount> account)
    {
        if (!state.Contracts.TryGetValue(id, out ContractRecord c) || c.State != ContractRecord.Taken || c.TakenBy != actor)
            return "That isn't yours to give up.";
        if (c.Story != "")
        {
            c.State = ContractRecord.Offered;           // a story waits for the pilot: it goes back to their board
            c.TakenBy = "";
            c.TakenAt = 0;
            if (hold != null && c.Carried)
                hold.Keep(c.Good, c.Loaded - c.Delivered);
            c.Loaded = c.Delivered;
            Touched.Add(actor);
            return null;
        }
        if (hold != null && c.Carried)
            hold.Keep(c.Good, c.Loaded - c.Delivered);
        Fail(state, c, account);
        return null;
    }

    private static void Finish(UniverseState state, ContractRecord c, IAccount taker, Func<string, IAccount> account)
    {
        c.State = ContractRecord.Done;
        c.Ended = state.Clock;
        if (taker != null)
            taker.Credits += c.Escrow;
        c.Escrow = 0;
        if (account(c.Giver) is IRemembers memory)
            Memories.Note(memory, c.TakenBy, DidMyWork, 0.5, state.Clock);
        if (c.Story != "" && state.Pilots.TryGetValue(c.TakenBy, out PilotRecord pilot))
            pilot.Story[c.Story] = Math.Max(pilot.Story.GetValueOrDefault(c.Story), c.Step + 1);
        Touched.Add(c.TakenBy);
    }

    private static void Fail(UniverseState state, ContractRecord c, Func<string, IAccount> account)
    {
        c.State = ContractRecord.Failed;
        c.Ended = state.Clock;
        if (account(c.Giver) is IAccount giver)
            giver.Credits += c.Escrow;
        c.Escrow = 0;
        if (account(c.Giver) is IRemembers memory && c.TakenBy != "")
            Memories.Note(memory, c.TakenBy, LetMeDown, -1, state.Clock);
        Touched.Add(c.TakenBy);
    }

    // --- The board's own work ---

    /// <summary>
    /// What happens by itself: offers run out, taken work runs late or loses its cargo, stations ask for what they
    /// lack, and story steps are offered to their pilots.
    /// </summary>
    public static void Work(UniverseState state, StationBook book, Func<string, IAccount> account, Func<string, ShipRecord> holdOf = null)
    {
        foreach (ContractRecord c in state.Contracts.Values.Where(c => c.Open).ToList())
        {
            if (c.State == ContractRecord.Offered && c.Expires > 0 && state.Clock >= c.Expires)
            {
                c.State = ContractRecord.Expired;
                c.Ended = state.Clock;
                if (account(c.Giver) is IAccount giver)
                    giver.Credits += c.Escrow;
                c.Escrow = 0;
                if (c.For != "")
                    Touched.Add(c.For);
            }
            else if (c.State == ContractRecord.Taken)
            {
                ShipRecord hold = holdOf?.Invoke(c.TakenBy);
                bool late = c.Due > 0 && state.Clock >= c.Due;
                bool lost = c.Carried && hold != null && hold.ConsignedOf(c.Good) < c.Loaded - c.Delivered;
                if (late || lost)
                {
                    if (hold != null && c.Carried)
                        hold.Keep(c.Good, c.Loaded - c.Delivered);
                    if (c.Story != "")
                        Repost(state, c);
                    else
                        Fail(state, c, account);
                }
            }
        }
        foreach (StationBook.Station station in book.Stations)
            Ask(state, book, station, account);
        foreach (PilotRecord pilot in state.Pilots.Values)
            foreach (StoryChain chain in Story)
                OfferStep(state, chain, pilot, account);
        // Finished work is kept a while, for the journals
        foreach (ContractRecord c in state.Contracts.Values.Where(c => !c.Open && state.Clock - c.Ended > 48 * 3600).ToList())
            state.Contracts.Remove(c.Id);
    }

    // A story step that went wrong waits on the board again: the story never moves on without the pilot
    private static void Repost(UniverseState state, ContractRecord c)
    {
        Touched.Add(c.TakenBy);
        c.State = ContractRecord.Offered;
        c.TakenBy = "";
        c.TakenAt = 0;
        c.Loaded = c.Delivered;
    }

    // A station short of a good it uses asks for it, paying its market's price and a premium over, from its owner's money
    private static void Ask(UniverseState state, StationBook book, StationBook.Station station, Func<string, IAccount> account)
    {
        string owner = CargoShips.OwnerOf(state, station);
        if (string.IsNullOrEmpty(owner) || account(owner) is not IAccount giver || state.Station(station.Id).Abandoned)
            return;
        List<ContractRecord> open = state.Contracts.Values.Where(c => c.Open && c.Station == station.Id && c.Story == "").ToList();
        if (open.Count(c => c.State == ContractRecord.Offered) >= Rules.MostOffers)
            return;
        long setAside = state.Contracts.Values.Where(c => c.Open && c.Giver == owner).Sum(c => c.Escrow);
        StationRecord record = state.Station(station.Id);
        foreach (MarketGood line in station.Goods.Where(g => g.Good.Traded && Market.Net(g.Good.Id, station.Goods, station.Factories) < 0))
        {
            if (open.Any(c => c.Good == line.Good.Id && c.Kind == ContractRecord.Deliver))
                continue;
            double stock = record.Stock.GetValueOrDefault(line.Good.Id, line.Stock);
            if (stock >= line.Stock * Rules.AskBelow)
                continue;
            int tonnes = Math.Min(Rules.MostTonnes, (int)Math.Ceiling(line.Stock - stock));
            long price = Market.BuyPrice(record, line);
            if (tonnes < Rules.FewestTonnes || price <= 0)
                continue;
            long reward = (long)Math.Round(price * tonnes * (1 + Rules.Premium));
            if (setAside + reward > giver.Credits * Rules.MoneyShare)
                continue;
            if (Post(state, new ContractRecord
                {
                    Kind = ContractRecord.Deliver, Giver = owner, Station = station.Id, Good = line.Good.Id, Tonnes = tonnes, To = station.Id,
                    Reward = reward, Expires = state.Clock + Rules.OfferHours * 3600, Allowed = Rules.DueHours * 3600,
                    Title = $"Deliver {tonnes} t of {line.Good.Name}",
                    Briefing = $"{station.Title} is short of {line.Good.Name}. Bring {tonnes} t of your own here and hand it over at this terminal.",
                }, giver) is ContractRecord posted)
            {
                setAside += posted.Escrow;
                open.Add(posted);
                if (open.Count(c => c.State == ContractRecord.Offered) >= Rules.MostOffers)
                    return;
            }
        }
    }

    // The pilot's next step of a story, offered on its station's board, once the one before is done
    private static void OfferStep(UniverseState state, StoryChain chain, PilotRecord pilot, Func<string, IAccount> account)
    {
        int step = pilot.Story.GetValueOrDefault(chain.Id);
        if (step >= chain.Steps.Length || chain.Steps[step] is not StoryStep s)
            return;
        if (state.Contracts.Values.Any(c => c.Story == chain.Id && c.For == pilot.Id && c.Step == step && c.Open))
            return;
        string Say(string text) => Facts(state, chain, pilot, text);
        Post(state, new ContractRecord
        {
            Kind = s.Kind, Giver = s.Giver, Station = s.Station, For = pilot.Id, Title = Say(s.Title), Briefing = Say(s.Briefing),
            Good = s.Good?.Id ?? "", Tonnes = s.Tonnes, From = s.From, To = s.To, Sector = s.Sector, X = s.Point.X, Y = s.Point.Y, Z = s.Point.Z,
            Radius = s.Radius, Reward = s.Reward, Allowed = s.Hours * 3600, Story = chain.Id, Step = step, Lead = Say(s.Lead), Guide = Say(s.Guide),
            Wreck = Say(s.Wreck), Answer = Say(s.Answer),
            Item = s.ItemName == "" ? null : new ItemRecord
            {
                Kind = ItemRecord.Diskette, Name = Say(s.ItemName), Files = new Dictionary<string, string> { [s.ItemFile] = Say(s.ItemText) },
            },
        }, account(s.Giver));
    }

    /// <summary>
    /// A story's text with the facts it names ({name}) as they are for this pilot, settling each the first time
    /// (<see cref="StoryChain.Facts"/>).
    /// </summary>
    public static string Facts(UniverseState state, StoryChain chain, PilotRecord pilot, string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{'))
            return text ?? "";
        foreach (string line in chain.Facts)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0 || !text.Contains($"{{{line[..eq]}}}"))
                continue;
            string name = line[..eq].Trim(), key = $"{chain.Id}.{name}";
            if (!pilot.Facts.TryGetValue(key, out string value))
                pilot.Facts[key] = value = Settle(state, pilot, line[(eq + 1)..].Trim());
            text = text.Replace($"{{{name}}}", value);
        }
        return text;
    }

    // A fact as it falls for this pilot: by their id, never by dice, so it is the same wherever it is worked out
    private static string Settle(UniverseState state, PilotRecord pilot, string spec)
    {
        int colon = spec.IndexOf(':');
        string kind = colon < 0 ? spec : spec[..colon], arg = colon < 0 ? "" : spec[(colon + 1)..];
        if (kind == "callsign")
        {
            List<CargoShipRecord> fleet = state.CargoShips.Values
                .Where(r => r.Owner == arg && CargoShips.ModelOf(r) is ShipModel { Scout: false, Escort: false }).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            if (fleet.Count > 0)
                return CargoShips.CallsignOf(fleet[(int)(Hash(pilot.Id) % (uint)fleet.Count)].Id);
        }
        return CargoShips.CallsignOf($"{pilot.Id}:{arg}");
    }

    private static uint Hash(string s)
    {
        uint hash = 2166136261;
        foreach (char ch in s)
            hash = (hash ^ ch) * 16777619;
        return hash;
    }

    // --- The journal ---

    /// <summary>The pilot's journal as it stands: offers meant for them, work taken, and the last finished.</summary>
    public static List<JournalEntry> Journal(UniverseState state, StationBook book, PilotRecord pilot, Func<string, IAccount> account)
    {
        IEnumerable<ContractRecord> mine = state.Contracts.Values.Where(c => c.TakenBy == pilot.Id || c.State == ContractRecord.Offered && c.For == pilot.Id);
        return mine.OrderBy(c => c.Open ? 0 : 1).ThenByDescending(c => c.Open ? c.TakenAt : c.Ended).ThenByDescending(c => c.Posted)
            .Where((c, i) => c.Open || i < JournalDone + 20)
            .Select(c => Entry(state, book, c, account)).Take(32).ToList();
    }

    private static JournalEntry Entry(UniverseState state, StationBook book, ContractRecord c, Func<string, IAccount> account)
    {
        bool offered = c.State == ContractRecord.Offered;
        return new JournalEntry
        {
            Id = c.Id, Title = c.Title, Giver = account(c.Giver)?.Name ?? c.Giver, State = c.State, Briefing = c.Briefing, Reward = c.Reward, Story = c.Story != "",
            Wreck = c.State == ContractRecord.Taken ? c.Wreck : "",
            Objective = Objective(state, book, c),
            Help = offered ? c.Lead : c.State == ContractRecord.Taken ? c.Guide : "",
            HelpKey = offered ? $"{c.Story}:{c.Step}:lead" : $"{c.Story}:{c.Step}:guide",
            Left = c.State == ContractRecord.Taken && c.Due > 0 ? c.Due - state.Clock : -1,
        };
    }

    /// <summary>What there is to do now, in a line.</summary>
    public static string Objective(UniverseState state, StationBook book, ContractRecord c)
    {
        string good = GoodName(c.Good);
        return c.State switch
        {
            ContractRecord.Offered => $"Take it at the contracts terminal of {NameOf(state, book, c.Station)}.",
            ContractRecord.Taken => c.Kind switch
            {
                ContractRecord.Haul when c.Loaded < c.Tonnes && c.Loaded == c.Delivered => $"Collect {c.Tonnes - c.Loaded} t of {good} at {NameOf(state, book, c.From)}.",
                ContractRecord.Haul => $"Hand over {c.Loaded - c.Delivered} t of {good} at {NameOf(state, book, c.To)}.",
                ContractRecord.Deliver => $"Bring {c.Tonnes - c.Delivered} t of {good} to {NameOf(state, book, c.To)}.",
                ContractRecord.Visit => $"Fly to the marked place in {SectorName(book, c.Sector)}.",
                ContractRecord.Recover when c.Loaded < c.Tonnes => $"Scoop {c.Tonnes - c.Loaded} t of {good} at the wreck of {c.Wreck} in {SectorName(book, c.Sector)}" +
                    (c.Loaded > c.Delivered ? $"; hand over the {c.Loaded - c.Delivered} t aboard at {NameOf(state, book, c.To)}." : "."),
                ContractRecord.Recover => $"Hand over {c.Loaded - c.Delivered} t of {good} at {NameOf(state, book, c.To)}.",
                ContractRecord.Report => $"Report what you found at the contracts terminal of {NameOf(state, book, c.Station)}.",
                _ => "",
            },
            ContractRecord.Done => "Done.",
            ContractRecord.Failed => "Failed.",
            _ => "Withdrawn.",
        };
    }

    public static string NameOf(UniverseState state, StationBook book, string station) =>
        state.Stations.GetValueOrDefault(station ?? "")?.Title is { Length: > 0 } title ? title : book?.Find(station)?.Title ?? station;

    private static string SectorName(StationBook book, string sector) => book?.SectorTitle(sector) ?? sector;

    private static string GoodName(string good) => GoodsCatalog.Find(good)?.Name is { Length: > 0 } name ? name : good;
}
