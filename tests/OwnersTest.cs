using System;
using System.IO;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Owners and money:
///
///   - organisations and personalities are data: every station and gate in the sectors has an owner from the catalog;
///   - a pilot's money goes to the owner when they buy and comes from it when they sell, none made or lost;
///   - an owner with no money buys nothing;
///   - a station's people pay for what they use, its upkeep costs, and an owner can't fall below nothing;
///   - an owner won't sell under what a thing costs it, so dearer inputs make dearer goods;
///   - a pilot can own a station;
///   - organisations and owners are kept on disk.
///
/// Run: Godot --headless --path . res://tests/owners_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class OwnersTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        Organisation miltec = OrganisationCatalog.Find("miltec");
        Check("MilTec is in the catalog, a permanent corporation with a personality", miltec is { Permanent: true, Personality: not null });
        foreach (string path in new[] { "res://scenes/sectors/sector_01.tscn", "res://scenes/sectors/sector_02.tscn" })
        {
            Node sector = GD.Load<PackedScene>(path).Instantiate();
            StationRadio[] radios = sector.FindChildren("*", "", true, false).OfType<StationRadio>().ToArray();
            string[] none = radios.Where(r => r.OwnedBy == null || OrganisationCatalog.Find(r.OwnedBy.Id) == null).Select(r => r.GetParent().Name.ToString()).ToArray();
            Check($"{path.GetFile()}: all {radios.Length} have an owner in the catalog{(none.Length > 0 ? ": not " + string.Join(", ", none) : "")}", radios.Length > 0 && none.Length == 0);
            sector.Free();
        }

        var iron = GD.Load<Good>("res://assets/goods/iron.tres");
        var line = new MarketGood { Good = iron, Stock = 100 };
        var goods = new[] { line };
        var station = new StationRecord { Callsign = "TEST" };
        Market.Advance(station, goods, 0);
        var owner = new OrganisationRecord { Id = "o", Name = "O", Credits = 1000 };
        var pilot = new PilotRecord { Id = "p", Credits = 10_000 };
        long total = owner.Credits + pilot.Credits;
        Market.Buy(station, line, pilot, 10, 100, out int bought, out long paid, owner);
        Check($"bought {bought} t: the owner has what the pilot paid ({paid})", bought == 10 && owner.Credits == 1000 + paid && owner.Credits + pilot.Credits == total);
        Market.Sell(station, line, pilot, 5, out int sold, out long earned, owner);
        Check($"sold {sold} t: the owner paid it ({earned})", sold == 5 && owner.Credits + pilot.Credits == total);

        owner.Credits = 0;
        Market.Result result = Market.Sell(station, line, pilot, 5, out sold, out _, owner);
        Check("an owner with no money buys nothing", result == Market.Result.NoCash && sold == 0 && pilot.Ship.Holds("iron") == 5);

        var people = new MarketGood { Good = iron, Stock = 100, Uses = 10 };
        var town = new StationRecord { Callsign = "TOWN", UpdatedAt = 0, Stock = { ["iron"] = 100 } };
        var landlord = new OrganisationRecord { Credits = 0 };
        Market.Advance(town, new[] { people }, 3600, null, null, landlord, 0);
        Check($"its people pay for what they use ({landlord.Credits})", landlord.Credits == 10 * iron.BasePrice);
        Market.Advance(town, new[] { people }, 7200, null, null, landlord, 1_000_000);
        Check("upkeep can't take an owner below nothing", landlord.Credits == 0);

        var chips = GD.Load<Good>("res://assets/goods/chips.tres");
        var electronics = GD.Load<Good>("res://assets/goods/electronics.tres");
        var recipe = new Recipe { Output = electronics, Inputs = new[] { new RecipePart { Item = chips, Amount = 1 } } };
        var chipLine = new MarketGood { Good = chips, Stock = 100 };
        var outLine = new MarketGood { Good = electronics, Stock = 100 };
        var factory = new[] { new FactoryLine { Recipe = recipe, PerHour = 1 } };
        var maker = new OrganisationRecord { Credits = 1000, Margin = 0.2 };
        var plant = new StationRecord { Callsign = "PLANT", UpdatedAt = 0, Stock = { ["chips"] = 100, ["electronics"] = 300 } };
        Market.Advance(plant, new[] { chipLine, outLine }, 1, null, factory, maker, 0);
        long plenty = Market.BuyPrice(plant, outLine);
        Check($"it won't sell under cost and its margin, however much it has ({plenty}, chips at {chips.BasePrice})", plenty == (long)Math.Ceiling(chips.BasePrice * 1.2));
        plant.Stock["chips"] = 25;
        Market.Advance(plant, new[] { chipLine, outLine }, 2, null, factory, maker, 0);
        Check($"dearer inputs make what's made of them dearer ({plenty} to {Market.BuyPrice(plant, outLine)})", Market.BuyPrice(plant, outLine) > plenty);

        var state = new UniverseState();
        Universe universe = Universe.Current;
        universe.State.Pilot("somebody", "Somebody").Credits = 7;
        IAccount held = universe.Account(PilotRecord.OwnerPrefix + "somebody");
        Check("a pilot can be an owner", held is PilotRecord { Credits: 7 });
        IAccount corporation = universe.Account("miltec");
        Check($"an organisation's record starts as it was set up ({corporation?.Name})", corporation is OrganisationRecord { Name: "MilTec" } && corporation.Credits == miltec.StartingCredits);

        string folder = Path.Combine(OS.GetUserDataDir(), "test_owners");
        state.Organisation("x", () => new OrganisationRecord { Id = "x", Name = "X", Credits = 42, Parent = "miltec" });
        state.Station("TEST").Owner = "x";
        UniverseStore.Save(state, folder);
        UniverseState back = UniverseStore.Load(folder);
        Check("organisations and owners are kept", back.Organisations["x"].Credits == 42 && back.Organisations["x"].Parent == "miltec" && back.Stations["TEST"].Owner == "x");
        Directory.Delete(folder, true);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
