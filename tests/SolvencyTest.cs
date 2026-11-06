using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// An owner with no money: it holds out for a while, sells what earns least first, never its last working ship, and its
/// station at the end; prices fall; organisations buy what they can afford; the ships stay with the seller and trade on
/// their own; money back stops the selling; a station nobody buys is abandoned and stands idle; a landless owner can
/// buy again; a pilot can buy a station. All of it kept on disk.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/solvency_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SolvencyTest : Node
{
    private int _failures;
    private UniverseState _state;
    private StationBook _book;

    private IAccount Account(string owner)
    {
        if (string.IsNullOrEmpty(owner))
            return null;
        if (owner.StartsWith(PilotRecord.OwnerPrefix))
            return _state.Pilot(owner[PilotRecord.OwnerPrefix.Length..]);
        return _state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 5_000_000 });
    }

    private void Fresh()
    {
        _state = new UniverseState();
        CargoShips.Muster(_state, _book, Account);
        foreach (StationBook.Station s in _book.Stations)
            Account(CargoShips.OwnerOf(_state, s));
        foreach (CargoShipRecord ship in _state.CargoShips.Values)
            ship.NextLook = 1e15;
    }

    private void Hours(double hours)
    {
        _state.Clock += hours * 3600;
        Solvency.Work(_state, _book, Account);
    }

    public override void _Ready()
    {
        _book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        Idling();
        Selling();
        Rescue();
        NobodyBuys();
        Landless();
        Disk();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Company() => CargoShips.OwnerOf(_state, _book.Find("ALPHA"));

    private void Bust(string owner) => ((OrganisationRecord)Account(owner)).Credits = 0;

    private List<string> OthersPoor(string except)
    {
        var others = _state.Organisations.Keys.Where(k => k != except).ToList();
        foreach (string o in others)
            ((OrganisationRecord)Account(o)).Credits = 0;
        return others;
    }

    private void Idling()
    {
        var ore = new MarketGood { Good = GD.Load<Good>("res://assets/goods/iron.tres"), Stock = 100 };
        var food = new MarketGood { Good = GD.Load<Good>("res://assets/goods/food.tres"), Stock = 100, Uses = 1 };
        var mine = new FactoryLine { Recipe = GD.Load<Recipe>("res://assets/recipes/iron_ore.tres"), PerHour = 5 };
        Check("a recipe to mine with", mine.Recipe != null);
        ore.Good = mine.Recipe.Output as Good;
        var goods = new[] { ore, food };
        var factories = new[] { mine };
        var station = new StationRecord { Id = "IDLE" };
        var pilot = new PilotRecord { Id = "p", Credits = 1000 };
        Market.Advance(station, goods, 0, null, factories, pilot, 100);
        station.Stock[ore.Good.Id] = 50;
        Market.Advance(station, goods, 3600, null, factories, pilot, 100);
        Check($"paid for and fed, it works ({station.Stock[ore.Good.Id]:0} t)", !station.Idle && station.Stock[ore.Good.Id] > 50);

        pilot.Credits = 0;
        station.Stock[food.Good.Id] = 100;
        double before = station.Stock[ore.Good.Id];
        Market.Advance(station, goods, 7200, null, factories, pilot, 1_000_000);
        Check($"with an owner who can't pay, it makes nothing ({station.Stock[ore.Good.Id]:0} t)", station.Idle && station.Stock[ore.Good.Id] <= before);
        var buyer = new PilotRecord { Id = "b", Credits = 10_000_000_000 };
        Market.Buy(station, ore, buyer, 2, 100, out int bought, out long _, pilot);
        Check($"what is in stock can still be sold ({bought} t)", bought == 2);

        pilot.Credits = 1_000_000_000;
        station.Stock[food.Good.Id] = 0;
        before = station.Stock[ore.Good.Id];
        Market.Advance(station, goods, 10800, null, factories, pilot, 100);
        Check("with its people short of what they need it makes nothing, money or not", station.Idle && station.Stock[ore.Good.Id] <= before);
        station.Stock[food.Good.Id] = 100;
        before = station.Stock[ore.Good.Id];
        Market.Advance(station, goods, 14400, null, factories, pilot, 100);
        Check("both put right, it goes back to work by itself", !station.Idle && station.Stock[ore.Good.Id] > before);

        var metals = new MarketGood { Good = GD.Load<Good>("res://assets/goods/metals.tres"), Stock = 100, Uses = 1 };
        var material = new StationRecord { Id = "MATERIAL" };
        Market.Advance(material, new[] { metals }, 0);
        material.Stock[metals.Good.Id] = 0;
        Market.Advance(material, new[] { metals }, 3600);
        Check("a material running out doesn't stop a base", !material.Idle);
        var fuel = new MarketGood { Good = GD.Load<Good>("res://assets/goods/energy.tres"), Stock = 100, Uses = 1 };
        var power = new StationRecord { Id = "POWER" };
        Market.Advance(power, new[] { fuel }, 0);
        power.Stock[fuel.Good.Id] = 0;
        Market.Advance(power, new[] { fuel }, 3600);
        Check("without energy it stops", power.Idle);
    }

    private void Selling()
    {
        Fresh();
        string owner = Company();
        var org = (OrganisationRecord)Account(owner);
        List<CargoShipRecord> ships = _state.CargoShips.Values.Where(s => s.Owner == owner).ToList();
        Check($"the company has ships to sell ({ships.Count})", ships.Count >= 2);
        ships[0].Delivered = 500;
        OthersPoor(owner);
        Bust(owner);
        Hours(1);
        Check("broke, it holds out at first", _state.Sales.Count == 0 && org.BrokeSince >= 0);
        Personality nature = OrganisationCatalog.Find(owner).Personality;
        Hours(nature.GraceHours + 1);
        SaleRecord first = _state.Sales.Values.SingleOrDefault(s => s.Seller == owner);
        Check($"after its grace it puts something up for sale ({first?.Id})", first != null);
        Check("what earned least goes first", first?.Item == ships.Skip(1).OrderBy(s => s.Delivered).ThenBy(s => s.Id, StringComparer.Ordinal).First().Id);

        // Who has money buys it
        string rich = _state.Organisations.Keys.First(k => k != owner && _book.Stations.Any(s => CargoShips.OwnerOf(_state, s) == k));
        ((OrganisationRecord)Account(rich)).Credits = 9_000_000;
        long price = first.PriceAt(_state.Clock);
        Hours(0);
        Check($"who can afford it buys it, and the seller gets the money ({price})", _state.CargoShips[first.Item].Owner != owner && org.Credits >= price * 0.99);
        Check("it works for its new owner now", _book.Stations.Any(s => s.Id == _state.CargoShips[first.Item].Home && CargoShips.OwnerOf(_state, s) == _state.CargoShips[first.Item].Owner));
    }

    private void Rescue()
    {
        Fresh();
        string owner = Company();
        var others = OthersPoor(owner);
        Bust(owner);
        Personality nature = OrganisationCatalog.Find(owner).Personality;
        Hours(1);
        Hours(nature.GraceHours + 1);
        SaleRecord sale = _state.Sales.Values.First(s => s.Seller == owner);
        long start = sale.PriceAt(_state.Clock);
        _state.Clock += nature.SaleFallHours * 1800;
        Check($"nobody buying, the price falls ({start} -> {sale.PriceAt(_state.Clock)})", sale.PriceAt(_state.Clock) < start);
        _state.Clock += nature.SaleFallHours * 3600;
        Check("but not below its floor", sale.PriceAt(_state.Clock) == sale.Floor);
        ((OrganisationRecord)Account(owner)).Credits = 9_000_000;
        Hours(0);
        Check("with money back it stops selling", !_state.Sales.Values.Any(s => s.Seller == owner));
    }

    private void NobodyBuys()
    {
        Fresh();
        string owner = Company();
        OthersPoor(owner);
        Bust(owner);
        Personality nature = OrganisationCatalog.Find(owner).Personality;
        int working = _state.CargoShips.Values.Count(s => s.Owner == owner);
        Hours(1);
        string station = null;
        for (int i = 0; i < 40 && station == null; i++)
        {
            Hours(nature.GraceHours + 1);
            station = _state.Sales.Values.FirstOrDefault(s => s.Kind == SaleRecord.Station && s.Seller == owner)?.Item;
            foreach (SaleRecord sale in _state.Sales.Values.Where(s => s.Kind == SaleRecord.Ship && s.Seller == owner).ToList())
                Solvency.Buy(sale, PilotRecord.OwnerPrefix + "scrap", _state, _book, id => id.StartsWith("pilot:") ? new OrganisationRecord { Credits = long.MaxValue / 2 } : Account(id));
        }
        Check($"its ships go first, all but one that can earn ({working} -> {_state.CargoShips.Values.Count(s => s.Owner == owner)})", _state.CargoShips.Values.Count(s => s.Owner == owner) == 1);
        Check($"at the end it sells its station ({station})", station == "ALPHA");
        Hours(nature.SaleFallHours + 1);
        StationRecord record = _state.Station("ALPHA");
        Check("nobody takes it: it is abandoned", record.Abandoned && record.Owner == Solvency.Abandoned);
        double stock = record.Stock.Values.Sum();
        double updated = record.UpdatedAt;
        _state.Clock += 3600 * 10;
        CargoShips.Bring(_state, _book.Find("ALPHA"), Account);
        Check("an abandoned station stands idle", record.Stock.Values.Sum() == stock && record.UpdatedAt > updated);
        SaleRecord left = _state.Sales.Values.First(s => s.Item == "ALPHA");
        _state.Pilot("me").Credits = left.Floor;
        Check("a pilot can buy it, at that price", Solvency.Buy(left, PilotRecord.OwnerPrefix + "me", _state, _book, Account) && record.Owner == "pilot:me" && !record.Abandoned && _state.Pilot("me").Credits == 0);
    }

    private void Landless()
    {
        Fresh();
        string owner = Company();
        var org = (OrganisationRecord)Account(owner);
        org.Credits = 500_000;
        foreach (StationBook.Station s in _book.Stations.Where(s => CargoShips.OwnerOf(_state, s) == owner))
            _state.Station(s.Id).Owner = "someone_else";
        CargoShipRecord ship = _state.CargoShips.Values.First(s => s.Owner == owner);
        ship.NextLook = 0;
        ship.Phase = CargoShipRecord.Parked;
        Hours(1);
        Check("with no stations it has no upkeep and isn't broke", Solvency.Upkeep(owner, _state, _book) == 0 && org.BrokeSince < 0);
        CargoShips.Tick(_state, _book, Account);
        Check($"its ship goes off on its own ({ship.Phase}, to {ship.Going})", ship.Phase == CargoShipRecord.Flying);
        var sale = new SaleRecord { Id = "station:QUARTZ", Kind = SaleRecord.Station, Item = "QUARTZ", Seller = "someone_else", Asking = 100_000, Floor = 100_000, Since = _state.Clock, Falls = 3600 };
        _state.Sales[sale.Id] = sale;
        org.Credits = 5_000_000;
        ((OrganisationRecord)Account("someone_else")).Credits = 0;
        foreach (string o in _state.Organisations.Keys.Where(k => k != owner && k != "someone_else"))
            ((OrganisationRecord)Account(o)).Credits = 0;
        Hours(0);
        Check("with money it can buy a station again", _state.Station("QUARTZ").Owner == owner);
    }

    private void Disk()
    {
        Fresh();
        _state.Sales["ship:x"] = new SaleRecord { Id = "ship:x", Item = "x", Asking = 5, Floor = 1 };
        _state.Station("ALPHA").Abandoned = true;
        ((OrganisationRecord)Account(Company())).BrokeSince = 7;
        string folder = Path.Combine(OS.GetUserDataDir(), "test", "solvency");
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        UniverseStore.Save(_state, folder);
        UniverseState again = UniverseStore.Load(folder);
        Check("sales, abandoned stations and who is broke are kept on disk", again.Sales.ContainsKey("ship:x") && again.Stations["ALPHA"].Abandoned && again.Organisations.Values.Any(o => o.BrokeSince == 7));
        Directory.Delete(folder, true);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
