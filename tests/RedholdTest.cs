using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Redhold, Red Maw's base in sector 3, through the gate from sector 2: the universe finds it and its pads; its owner's
/// nature makes it deal cheaper than anyone (and buying there and selling back there still loses); only stations whose
/// owner goes far enough outside the law take stolen goods, so a pirate with loot aboard elsewhere sets off for it; its
/// own free pirate starts there; it is the only place with a factor, who settles debts elsewhere; it makes energy and
/// food and its people pay for what they use; and its door leaves
/// part and close.
///
/// Run: Godot --headless --path . res://tests/redhold_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RedholdTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe();
        await Door();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Universe()
    {
        var population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        var map = GD.Load<SectorMap>("res://assets/sectors/map.tres");
        var state = new UniverseState();
        StationBook book = StationBook.Read(map);
        IAccount Account(string owner)
        {
            if (string.IsNullOrEmpty(owner))
                return null;
            if (owner.StartsWith(PersonRecord.OwnerPrefix))
                return state.People.GetValueOrDefault(owner);
            return state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 2_000_000 });
        }
        CargoShips.Population = population;

        StationBook.Station redhold = book.Find("REDHOLD"), kestrel = book.Find("KESTREL"), alpha = book.Find("ALPHA");
        var freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        var fighter = GD.Load<ShipModel>("res://assets/ship/models/free_fighter.tres");
        Check("the universe finds Redhold in sector 3", redhold != null && redhold.Sector == "sector_03");
        if (redhold == null)
            return;
        Check($"its pads take freighters and fighters ({redhold.Pads.Count} pads)", redhold.Pads.Any(p => book.Fits(p, freighter)) && redhold.Pads.Any(p => book.Fits(p, fighter)));
        Check("a way runs to it from sectors 2 and 1", book.Way("sector_02", "sector_03") != null && book.Way("sector_01", "sector_03") != null);
        Check($"Red Maw owns it ({CargoShips.OwnerOf(state, redhold)})", CargoShips.OwnerOf(state, redhold) == "red_maw");
        var factors = new List<string>();
        foreach (Sector sector in GD.Load<SectorMap>("res://assets/sectors/map.tres").Sectors)
        {
            Node root = GD.Load<PackedScene>(sector.Scene).Instantiate();
            foreach (StationMarket market in root.FindChildren("*", "", true, false).OfType<StationMarket>().Where(m => m.Factor))
                factors.Add(market.GetParent().GetNodeOrNull<StationRadio>("Radio")?.StationId ?? market.GetParent().Name);
            root.Free();
        }
        Check($"Redhold is the only place with a factor ({string.Join(", ", factors)})", factors.SequenceEqual(new[] { "REDHOLD" }));

        // Who takes stolen goods
        Check("Redhold takes stolen goods", People.Fences(redhold, population, state, Account));
        Check("the lawful stations don't", !People.Fences(kestrel, population, state, Account) && !People.Fences(alpha, population, state, Account));

        // It deals cheaper, as its owner is
        StationRecord here = CargoShips.Bring(state, redhold, Account), there = CargoShips.Bring(state, kestrel, Account);
        Check($"its prices are its owner's share of the market's ({here.Share})", Math.Abs(here.Share - 0.85) < 1e-9 && there.Share == 1);
        MarketGood metalsHere = redhold.Goods.First(g => g.Good.Id == "metals"), metalsThere = kestrel.Goods.First(g => g.Good.Id == "metals");
        here.Stock["metals"] = metalsHere.Stock;
        there.Stock["metals"] = metalsThere.Stock;
        long cheap = Market.BuyPrice(here, metalsHere), dear = Market.BuyPrice(there, metalsThere);
        Check($"a tonne of metals at the stock its market wants costs less there ({cheap} against {dear})", cheap < dear);
        Check($"and buying there and selling back there loses ({cheap} against {Market.SellPrice(here, metalsHere)})", Market.SellPrice(here, metalsHere) < cheap);
        StationNews news = MarketNews.Report(redhold, here, state.Clock);
        Check("what people hear of it says so", news.Share == here.Share && news.Goods["metals"].Buy == cheap);

        // Energy and food: made there, used by its people, who pay the owner for what they eat
        var owner = (OrganisationRecord)Account("red_maw");
        here.Stock["water"] = 900;
        double food = here.Stock["food"];
        long purse = owner.Credits;
        state.Clock += 3 * 3600;
        CargoShips.Bring(state, redhold, Account);
        Check($"it is at work ({(here.Idle ? "idle" : "working")}), making energy and food", !here.Idle && here.Stock["energy"] > 0 && here.Stock["food"] != food);
        Check($"what its people use pays for its upkeep ({purse} -> {owner.Credits})", owner.Credits >= purse);

        // Its own free pirate starts there; a pirate with loot elsewhere sets off for it
        CargoShips.Muster(state, book, Account);
        People.Muster(state, population, book, Account);
        PersonRecord local = state.People.Values.FirstOrDefault(p => p.Line == 3);
        CargoShipRecord localShip = local == null ? null : state.CargoShips.Values.FirstOrDefault(s => s.Owner == local.Id);
        Check($"Redhold's pirate starts there ({localShip?.At})", localShip?.At == "REDHOLD" && local.Livelihood == "Pirate");
        Check("Redhold has its one ship", state.CargoShips.Values.Count(s => s.Owner == "red_maw") == 1);

        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == "Pirate" && p.Line != 3);
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Owner == pirate.Id);
        ship.At = "KESTREL";
        ship.Phase = CargoShipRecord.Parked;
        ship.State = CargoShipRecord.Waiting;
        ship.NextLook = 0;
        ship.Loot["electronics"] = 3;
        pirate.Credits = 5000;
        CargoShips.Tick(state, book, Account);
        Check($"lawful Kestrel doesn't buy the loot ({ship.Loot.GetValueOrDefault("electronics")} t still aboard)", ship.Loot.GetValueOrDefault("electronics") == 3);
        Check($"the pirate sets off for Redhold with it ({ship.Going})", ship.Going == "REDHOLD");

        long before = pirate.Credits;
        double had = here.Stock.GetValueOrDefault("electronics");
        ship.At = "REDHOLD";
        ship.Going = "";
        ship.Phase = CargoShipRecord.Parked;
        ship.State = CargoShipRecord.Waiting;
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"Redhold buys it, for less than its market pays ({pirate.Credits - before} credits)", ship.Loot.Count == 0 && pirate.Credits > before && here.Stock["electronics"] > had);

        // An owner with a price on someone's head shuts its stations to them: they end up where they are let in
        var kestrelMining = (OrganisationRecord)Account(kestrel.Owner);
        kestrelMining.Bounties[pirate.Id] = 5000;
        Check("Kestrel shuts its doors to a pirate its owner wants", !CargoShips.Welcomes(kestrel, pirate.Id, state, Account) && CargoShips.Welcomes(redhold, pirate.Id, state, Account));
        ship.At = "DRYDOCK";
        ship.Going = "KESTREL";
        ship.Phase = CargoShipRecord.Holding;
        ship.ContactPoint = kestrel.At + new Vector3(0, 0, 2000);
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"turned away at Kestrel, it goes on to a station that lets it in ({ship.Going})", ship.Phase == CargoShipRecord.Flying && ship.Going != "KESTREL" && CargoShips.Welcomes(book.Find(ship.Going), pirate.Id, state, Account));

        // A gate whose owner wants it gives no clearance: it goes through unpaid, and the price on its head goes up
        StationBook.Gate gate = book.Way("sector_02", "sector_03").First();
        var gates = (OrganisationRecord)Account(gate.Owner);
        gates.Bounties[pirate.Id] = 3000;
        long wanted = gates.Bounties[pirate.Id], tolls = gates.Credits, purse2 = pirate.Credits;
        ship.Phase = CargoShipRecord.Parked;
        ship.At = "KESTREL";
        CargoShips.Hop(ship, fighter, redhold, state, book, Account);
        Check($"it crosses without paying ({purse2 - pirate.Credits} paid), and the gate's owner wants it more ({wanted} -> {gates.Bounties[pirate.Id]})",
              gates.Credits == tolls && gates.Bounties[pirate.Id] == wanted + gate.Unpaid && gate.Unpaid > 0);
    }

    private async Task Door()
    {
        var station = GD.Load<PackedScene>("res://scenes/stations/redhold.tscn").Instantiate<Node3D>();
        AddChild(station);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var dock = station.GetNode<HangarDock>("Hangar/Dock");
        var left = station.GetNode("Model").FindChild("DoorLeft", true, false) as Node3D;
        var right = station.GetNode("Model").FindChild("DoorRight", true, false) as Node3D;
        Check($"the station has its dock, its pads numbered 1 to {dock?.Pads.Count} and both door leaves", dock != null && dock.Pads.Count == 3 && dock.Pads.Select(p => p.Number).SequenceEqual(new[] { 1, 2, 3 }) && left != null && right != null);
        if (left == null || right == null)
            return;
        Vector3 shutLeft = left.Position, shutRight = right.Position;
        dock.Door.Open = 1;
        await ToSignal(GetTree().CreateTimer(dock.Door.Seconds + 1), SceneTreeTimer.SignalName.Timeout);
        Check($"open, the leaves part ({left.Position.X - shutLeft.X:0} and {right.Position.X - shutRight.X:0} m)", left.Position.X - shutLeft.X < -39 && right.Position.X - shutRight.X > 39);
        dock.Door.Open = 0;
        await ToSignal(GetTree().CreateTimer(dock.Door.Seconds + 1), SceneTreeTimer.SignalName.Timeout);
        Check("shut, they meet again", left.Position.IsEqualApprox(shutLeft) && right.Position.IsEqualApprox(shutRight));
        station.QueueFree();
    }

    private void Check(string what, bool ok)
    {
        GD.Print(ok ? "  ok   " : "  FAIL ", what);
        if (!ok)
            _failures++;
    }
}
