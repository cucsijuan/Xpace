using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The contract boards, on records only (no scenes):
///   - a new pilot is offered the story's first step at its station, meant for them, its reward set aside by the giver;
///   - it is taken only there; a haul's cargo is collected at its pick-up station, bought by the giver, consigned
///     (not the pilot's to sell), and handed over at the other, where the reward is paid and the story moves on;
///   - a station short of what it uses asks for it, paying its own price and a premium, from its owner's money;
///   - offers run out and their money goes back; given up, consigned cargo stays aboard, stolen, and the giver remembers;
///   - a giver let down often enough offers that pilot nothing more;
///   - the story's second step: the wreck of a freighter the story names for this pilot, the giver's cargo scooped there
///     consigned to the taker (to anyone else, stolen), handed over a part at a time; the third: reporting the callsign
///     of a Drydock freighter, the one the story settled, at the giver's station, any way it is typed.
///
/// Run: Godot --headless --path . res://tests/contracts_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ContractsTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        Story();
        Board();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private (UniverseState, StationBook, Func<string, IAccount>) World()
    {
        var map = GD.Load<SectorMap>("res://assets/sectors/map.tres");
        StationBook book = StationBook.Read(map);
        var state = new UniverseState { Clock = 100 };
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        Contracts.Rules = map.Contracts ?? new ContractRules();
        Contracts.Story = map.Story;
        Contracts.Touched.Clear();
        return (state, book, Account);
    }

    private void Story()
    {
        (UniverseState state, StationBook book, Func<string, IAccount> Account) = World();
        PilotRecord pilot = state.Pilot("tester", "Tester");
        long start = pilot.Credits;
        IAccount miltec = Account("miltec");
        long before = miltec.Credits;
        Contracts.Work(state, book, Account, id => state.Pilots.GetValueOrDefault(id)?.Ship);
        ContractRecord first = state.Contracts.Values.FirstOrDefault(c => c.Story == "borrowed_signature");
        Check($"a new pilot is offered the story's first step at Alpha ({first?.Title})", first != null && first.For == pilot.Id && first.Station == "ALPHA" && first.State == ContractRecord.Offered);
        Check("its reward set aside from the giver's money", first != null && first.Escrow == first.Reward && miltec.Credits == before - first.Reward);
        Check("nobody else sees it", !Contracts.Offers(state, "ALPHA", "somebody", Account).Contains(first));
        pilot.Journal = Contracts.Journal(state, book, pilot, Account);
        Check("the journal has it, with where to get it", pilot.Journal.Count == 1 && pilot.Journal[0].Help.Contains("contracts terminal") && pilot.Journal[0].HelpKey.EndsWith(":lead"));
        Contracts.Work(state, book, Account, id => state.Pilots.GetValueOrDefault(id)?.Ship);
        Check("offered once", state.Contracts.Values.Count(c => c.Story != "") == 1);

        Check("taken only where it is offered", Contracts.Take(state, first.Id, pilot.Id, "QUARTZ", Account) != null && first.State == ContractRecord.Offered);
        Check("there it is taken", Contracts.Take(state, first.Id, pilot.Id, "ALPHA", Account) == null && first.State == ContractRecord.Taken);
        pilot.Journal = Contracts.Journal(state, book, pilot, Account);
        Check($"the journal now guides it ({pilot.Journal[0].Objective})", pilot.Journal[0].HelpKey.EndsWith(":guide") && pilot.Journal[0].Objective.Contains("Collect"));

        Check("the cargo isn't collected at Alpha", Contracts.Collect(state, book, first.Id, pilot.Id, pilot.Ship, 10, "ALPHA", Account) != null && pilot.Ship.Tonnes == 0);
        StationBook.Station quartz = book.Find("QUARTZ");
        Market.Advance(state.Station("QUARTZ"), quartz.Goods, state.Clock, quartz.Outfits, quartz.Factories, Account(quartz.Owner), quartz.Upkeep);
        double chips = state.Station("QUARTZ").Stock["chips"];
        long giverBefore = miltec.Credits, quartzBefore = Account(quartz.Owner).Credits;
        string why = Contracts.Collect(state, book, first.Id, pilot.Id, pilot.Ship, 10, "QUARTZ", Account);
        Check($"collected at Quartz: {pilot.Ship.ConsignedOf("chips")} t consigned ({why})", why == null && pilot.Ship.ConsignedOf("chips") == first.Tonnes && pilot.Ship.Clean("chips") == 0);
        Check($"bought by the giver from Quartz's market ({giverBefore - miltec.Credits} cr)", miltec.Credits < giverBefore && Account(quartz.Owner).Credits - quartzBefore == giverBefore - miltec.Credits && state.Station("QUARTZ").Stock["chips"] == chips - first.Tonnes);
        MarketGood chipsLine = quartz.Goods.First(g => g.Good.Id == "chips");
        Check("it isn't the pilot's to sell", Market.Sell(state.Station("QUARTZ"), chipsLine, pilot, 1, out _, out _) == Market.Result.Consigned && pilot.Ship.ConsignedOf("chips") == first.Tonnes);
        Check("nor is it handed over at Quartz", Contracts.HandOver(state, book, first.Id, pilot.Id, pilot.Ship, pilot, "QUARTZ", Account) != null);

        double alphaChips = state.Station("ALPHA").Stock.GetValueOrDefault("chips");
        why = Contracts.HandOver(state, book, first.Id, pilot.Id, pilot.Ship, pilot, "ALPHA", Account);
        Check($"handed over at Alpha: done, paid ({pilot.Credits - start} cr)", why == null && first.State == ContractRecord.Done && pilot.Credits == start + first.Reward && pilot.Ship.Tonnes == 0);
        Check("the chips are Alpha's now", state.Station("ALPHA").Stock.GetValueOrDefault("chips") == alphaChips + first.Tonnes);
        Check("and the story moves on", pilot.Story.GetValueOrDefault("borrowed_signature") == 1);
        Check("the giver remembers the work", Memories.Feeling((IRemembers)miltec, pilot.Id, state.Clock) > 0);
        Check("the pilot's journal was marked for sending", Contracts.Touched.Contains(pilot.Id));

        // Step 2: the freighter that didn't arrive
        foreach (string id in new[] { "dry-b", "dry-a", "dry-scout" })
            state.CargoShips[id] = new CargoShipRecord { Id = id, Owner = "drydock_yards", Model = id == "dry-scout" ? "res://assets/ship/models/scout_s3.tres" : "res://assets/ship/models/freighter.tres" };
        Contracts.Work(state, book, Account, id => state.Pilots.GetValueOrDefault(id)?.Ship);
        ContractRecord lost = state.Contracts.Values.FirstOrDefault(c => c.Story != "" && c.Step == 1);
        string wreck = pilot.Facts.GetValueOrDefault("borrowed_signature.wreck");
        Check($"the second step: recover at the wreck of {wreck} ({lost?.Title})", lost is { Kind: ContractRecord.Recover, State: ContractRecord.Offered } && wreck != null &&
              lost.Wreck == wreck && lost.Briefing.Contains(wreck) && !lost.Briefing.Contains('{') && lost.Item?.Name == $"Black box {wreck}");
        string log = lost?.Item?.Files.Values.FirstOrDefault() ?? "";
        string borrowed = pilot.Facts.GetValueOrDefault("borrowed_signature.borrowed");
        Check($"its flight recorder names a Drydock freighter, {borrowed}", log.Contains(borrowed ?? "?") &&
              new[] { "dry-a", "dry-b" }.Select(CargoShips.CallsignOf).Contains(borrowed));
        Contracts.Take(state, lost.Id, pilot.Id, "ALPHA", Account);
        Check("scooped by someone else, the cargo is no recovery", Contracts.Scooped(state, "somebody", "miltec", "chips", 2) == 0 && lost.Loaded == 0);
        Check("nor is someone else's cargo", Contracts.Scooped(state, pilot.Id, "quartz_works", "chips", 2) == 0);
        int got = Contracts.Scooped(state, pilot.Id, "miltec", "chips", 2);
        pilot.Ship.Consign("chips", got);
        pilot.Journal = Contracts.Journal(state, book, pilot, Account);
        Check($"the giver's cargo scooped by the taker is consigned ({pilot.Journal[0].Objective})", got == 2 && lost.Loaded == 2 && pilot.Journal[0].Objective.Contains("Scoop 6 t") && pilot.Journal[0].Wreck == wreck);
        Check("a part handed over at Alpha", Contracts.HandOver(state, book, lost.Id, pilot.Id, pilot.Ship, pilot, "ALPHA", Account) == null && lost.Delivered == 2 && lost.State == ContractRecord.Taken);
        got = Contracts.Scooped(state, pilot.Id, "miltec", "chips", 7);
        pilot.Ship.Consign("chips", got);
        Check($"no more is consigned than the contract asks ({got} of 7)", got == 6 && lost.Loaded == lost.Tonnes);
        long credits = pilot.Credits;
        Contracts.HandOver(state, book, lost.Id, pilot.Id, pilot.Ship, pilot, "ALPHA", Account);
        Check("the rest handed over: done and paid", lost.State == ContractRecord.Done && pilot.Credits == credits + lost.Reward && pilot.Story["borrowed_signature"] == 2);

        // Step 3: the black box
        Contracts.Work(state, book, Account, id => state.Pilots.GetValueOrDefault(id)?.Ship);
        ContractRecord box = state.Contracts.Values.FirstOrDefault(c => c.Story != "" && c.Step == 2);
        Check($"the third step: report, the same wreck ({box?.Title})", box is { Kind: ContractRecord.Report } && box.Wreck == wreck && box.Answer == borrowed);
        Contracts.Take(state, box.Id, pilot.Id, "ALPHA", Account);
        Check("reported where it isn't asked: no", Contracts.Tell(state, box.Id, pilot.Id, borrowed, "QUARTZ", pilot, Account) != null && box.State == ContractRecord.Taken);
        Check("the wrong callsign: no", Contracts.Tell(state, box.Id, pilot.Id, "FR-0000", "ALPHA", pilot, Account) != null && box.State == ContractRecord.Taken);
        credits = pilot.Credits;
        Check("the right one, however typed: done and paid", Contracts.Tell(state, box.Id, pilot.Id, $" {borrowed.ToLowerInvariant().Replace("-", " ")} ", "ALPHA", pilot, Account) == null &&
              box.State == ContractRecord.Done && pilot.Credits == credits + box.Reward && pilot.Story["borrowed_signature"] == 3);
    }

    private void Board()
    {
        (UniverseState state, StationBook book, Func<string, IAccount> Account) = World();
        Contracts.Story = Array.Empty<StoryChain>();
        StationBook.Station alpha = book.Find("ALPHA");
        Market.Advance(state.Station("ALPHA"), alpha.Goods, state.Clock, alpha.Outfits, alpha.Factories, Account(alpha.Owner), alpha.Upkeep);
        MarketGood food = alpha.Goods.First(g => g.Good.Id == "food");
        state.Station("ALPHA").Stock["food"] = food.Stock * 0.1;
        IAccount miltec = Account(alpha.Owner);
        long before = miltec.Credits;
        Contracts.Work(state, book, Account);
        ContractRecord ask = state.Contracts.Values.FirstOrDefault(c => c.Station == "ALPHA" && c.Good == "food");
        long market = Market.BuyPrice(state.Station("ALPHA"), food) * (ask?.Tonnes ?? 0);
        Check($"a station short of food asks for it ({ask?.Tonnes} t for {ask?.Reward} cr, its market {market} cr)", ask != null && ask.Kind == ContractRecord.Deliver && ask.Reward > market && ask.For == "");
        Check("paid from its owner's money, set aside", miltec.Credits == before - state.Contracts.Values.Where(c => c.Giver == alpha.Owner).Sum(c => c.Escrow));
        Contracts.Work(state, book, Account);
        Check("asked once", state.Contracts.Values.Count(c => c.Station == "ALPHA" && c.Good == "food" && c.Open) == 1);

        PilotRecord trader = state.Pilot("trader", "Trader");
        Check("anyone may take it there", Contracts.Take(state, ask.Id, trader.Id, "ALPHA", Account) == null);
        Check("with nothing of theirs aboard, nothing to hand over", Contracts.HandOver(state, book, ask.Id, trader.Id, trader.Ship, trader, "ALPHA", Account) != null);
        trader.Ship.Load("food", ask.Tonnes - 1);
        long credits = trader.Credits;
        Contracts.HandOver(state, book, ask.Id, trader.Id, trader.Ship, trader, "ALPHA", Account);
        Check($"handed over in part: {ask.Delivered} of {ask.Tonnes} t, not paid yet", ask.Delivered == ask.Tonnes - 1 && ask.State == ContractRecord.Taken && trader.Credits == credits);
        trader.Ship.Load("food", 1);
        Contracts.HandOver(state, book, ask.Id, trader.Id, trader.Ship, trader, "ALPHA", Account);
        Check("the last tonne, and it is paid", ask.State == ContractRecord.Done && trader.Credits == credits + ask.Reward);

        // A haul given up: its cargo stays aboard, stolen; the giver has its money back and remembers
        ContractRecord haul = Contracts.Post(state, new ContractRecord { Kind = ContractRecord.Haul, Giver = alpha.Owner, Station = "ALPHA", Good = "chips", Tonnes = 3, From = "QUARTZ", To = "ALPHA", Reward = 900 }, miltec);
        Contracts.Take(state, haul.Id, trader.Id, "ALPHA", Account);
        Contracts.Collect(state, book, haul.Id, trader.Id, trader.Ship, 10, "QUARTZ", Account);
        long giverBefore = miltec.Credits;
        Contracts.Abandon(state, haul.Id, trader.Id, trader.Ship, Account);
        Check("given up, the haul's cargo is the pilot's now, stolen", haul.State == ContractRecord.Failed && trader.Ship.StolenOf("chips") == 3 && trader.Ship.ConsignedOf("chips") == 0);
        Check("and the giver has its money back", miltec.Credits == giverBefore + 900);
        Check("and holds it against the pilot", Memories.Feeling((IRemembers)miltec, trader.Id, state.Clock) < 0);

        // Cargo lost on the way: the haul fails by itself
        ContractRecord lost = Contracts.Post(state, new ContractRecord { Kind = ContractRecord.Haul, Giver = alpha.Owner, Station = "ALPHA", Good = "chips", Tonnes = 2, From = "QUARTZ", To = "ALPHA", Reward = 600 }, miltec);
        PilotRecord unlucky = state.Pilot("unlucky", "Unlucky");
        Contracts.Take(state, lost.Id, unlucky.Id, "ALPHA", Account);
        Contracts.Collect(state, book, lost.Id, unlucky.Id, unlucky.Ship, 10, "QUARTZ", Account);
        unlucky.Ship.Empty();
        Contracts.Work(state, book, Account, id => state.Pilots.GetValueOrDefault(id)?.Ship);
        Check("cargo lost on the way, the haul fails", lost.State == ContractRecord.Failed);

        // Offers run out
        ContractRecord stale = Contracts.Post(state, new ContractRecord { Kind = ContractRecord.Deliver, Giver = alpha.Owner, Station = "ALPHA", Good = "water", Tonnes = 2, To = "ALPHA", Reward = 300, Expires = state.Clock + 60 }, miltec);
        long held = miltec.Credits;
        state.Clock += 120;
        Contracts.Work(state, book, Account);
        Check("an offer nobody took runs out, and its money goes back", stale.State == ContractRecord.Expired && miltec.Credits >= held + 300 - state.Contracts.Values.Where(c => c.Open && c.Posted > stale.Posted).Sum(c => c.Escrow));

        // Let down twice: nothing more for that pilot
        ContractRecord again = Contracts.Post(state, new ContractRecord { Kind = ContractRecord.Haul, Giver = alpha.Owner, Station = "ALPHA", Good = "chips", Tonnes = 1, From = "QUARTZ", To = "ALPHA", Reward = 100 }, miltec);
        Contracts.Take(state, again.Id, trader.Id, "ALPHA", Account);
        Contracts.Abandon(state, again.Id, trader.Id, trader.Ship, Account);
        Check($"let down twice, the giver offers that pilot nothing ({Memories.Feeling((IRemembers)miltec, trader.Id, state.Clock):0.0})",
            !Contracts.Offers(state, "ALPHA", trader.Id, Account).Any() && Contracts.Offers(state, "ALPHA", "fresh", Account).Any());
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
