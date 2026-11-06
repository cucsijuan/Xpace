using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// What ships use up is paid for: a ship on a record counts what it fires, comes back with no more than it had left, and
/// buys it again where it is sold; a hull is mended with a market's metals and electronics, on landing, and stays damaged
/// between one showing and the next; what police fire comes out of their station's stock.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/supplies_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SuppliesTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        UniverseState state = universe.State;
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        var model = GD.Load<ShipModel>("res://assets/ship/models/free_fighter.tres");
        CargoShips.Muster(state, book, universe.Account);
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e15;
        StationBook.Station alpha = book.Find("ALPHA"), quartz = book.Find("QUARTZ");
        OutfitLine line = alpha.Outfits.First(o => o.Item is MissileSpec);
        var spec = (MissileSpec)line.Item;
        string owner = CargoShips.OwnerOf(state, quartz);
        CargoShips.Enlist(owner, "guard", model, quartz, state, book);
        CargoShipRecord ship = state.CargoShips["guard"];
        ship.NextLook = 1e15;

        var body = new ShipBody { RecordId = "guard" };
        universe.Fired(body, spec);
        universe.Fired(body, spec);
        Check($"a ship on a record counts what it fires ({ship.Spent.GetValueOrDefault(spec.Id)})", ship.Spent.GetValueOrDefault(spec.Id) == 2);
        body.Free();

        var police = new ShipBody { Base = "ALPHA" };
        StationRecord stores = CargoShips.Bring(state, alpha, universe.Account);
        double stock = stores.Stock[spec.Id];
        universe.Fired(police, spec);
        Check("what police fire comes out of their station's stock", stores.Stock[spec.Id] == stock - 1);
        police.Free();

        // It lands at a station that sells them and is hurt
        ship.Hull = 0.5f;
        var way = book.Departure(quartz, null, alpha, model, 0, out Vector3 contact);
        ship.Phase = CargoShipRecord.Landing;
        ship.From = quartz.Id;
        ship.Going = alpha.Id;
        ship.At = quartz.Id;
        ship.State = CargoShipRecord.Returning;
        ship.Home = alpha.Id;
        ship.Dock = ship.Pad = 0;
        ship.Arrives = state.Clock;
        long money = universe.Account(owner).Credits;
        stock = stores.Stock[spec.Id];
        double metals = stores.Stock["metals"];
        CargoShips.Tick(state, book, universe.Account);
        Check("landing where they are sold, it buys back what it fired", ship.Spent.Count == 0 && stores.Stock[spec.Id] == stock - 2);
        Check($"and its owner pays ({money - universe.Account(owner).Credits})", universe.Account(owner).Credits < money);
        Check("and its hull is mended with the market's metals", ship.Hull == 1 && stores.Stock["metals"] < metals);

        Growth(book);
        Yard(book);
        Leaving(book);
        Offering(book);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // A yard's fee pays for the wait since it last sold a ship, up to what anyone would pay
    private void Yard(StationBook book)
    {
        var state = new UniverseState();
        var model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        StationBook.Station yard = book.Stations.First(s => s.Shipyard?.Contains(model) == true);
        long fresh = CargoShips.YardFee(model, yard, state);
        state.Clock = 3600 * 10;
        long later = CargoShips.YardFee(model, yard, state);
        Check($"a yard that sold one just now asks its fee ({fresh}), one that waited more ({later})", fresh == model.BuildFee && later > fresh);
        state.Clock = 3600 * 100_000;
        Check($"but not without limit ({CargoShips.YardFee(model, yard, state)})", CargoShips.YardFee(model, yard, state) <= model.BuildFee * 3);
    }

    // Someone broke for long enough gives up: their ship goes, and they leave
    private void Leaving(StationBook book)
    {
        var state = new UniverseState();
        var population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        IAccount Account(string owner) => string.IsNullOrEmpty(owner) ? null : owner.StartsWith(PersonRecord.OwnerPrefix) ? state.People.GetValueOrDefault(owner) : state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 5_000_000 });
        CargoShips.Population = population;
        CargoShips.Muster(state, book, Account);
        People.Muster(state, population, book, Account);
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e15;
        PersonRecord poor = state.People.Values.First(p => p.Livelihood == "Pirate");
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Owner == poor.Id);
        Check($"a person's ship goes by their name ({ship.Pilot})", ship.Pilot == poor.Name);
        poor.Credits = 10;
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check("broke, they go on for a while", state.People.ContainsKey(poor.Id) && poor.BrokeSince >= 0);
        state.Clock += population.GiveUpHours * 3600 + 1;
        ship.Phase = CargoShipRecord.Parked;
        ship.State = CargoShipRecord.Waiting;
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check("then they give up and leave", !state.People.ContainsKey(poor.Id) && !state.CargoShips.ContainsKey(ship.Id));
    }

    // A pilot's own station, up for sale
    private void Offering(StationBook book)
    {
        var state = new UniverseState();
        state.Station("QUARTZ").Owner = "pilot:me";
        Check("a pilot can put their station up for sale", Solvency.Offer("QUARTZ", "pilot:me", book, state) && state.Sales.ContainsKey("station:QUARTZ"));
        Check("not twice, and not someone else's", !Solvency.Offer("QUARTZ", "pilot:me", book, state) && !Solvency.Offer("ALPHA", "pilot:me", book, state));
    }

    // The owner grows for deep and lasting demand, with its ships busy and money to spare, and no further than it should
    private void Growth(StationBook book)
    {
        var state = new UniverseState();
        IAccount Account(string owner) => string.IsNullOrEmpty(owner) ? null : state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 50_000_000 });
        CargoShips.Muster(state, book, Account);
        StationBook.Station alpha = book.Find("ALPHA");
        StationRecord record = CargoShips.Bring(state, alpha, Account);
        string owner = CargoShips.OwnerOf(state, alpha);
        Personality nature = OrganisationCatalog.Find(owner).Personality;
        void Hours(double hours, bool busy, bool shortOfWater)
        {
            for (int i = 0; i < hours; i++)
            {
                state.Clock += 3600;
                foreach (CargoShipRecord ship in state.CargoShips.Values)
                {
                    ship.NextLook = 1e15;
                    ship.Phase = busy ? CargoShipRecord.Flying : CargoShipRecord.Parked;
                    ship.Going = "ALPHA";
                    ship.Arrives = 1e15;
                }
                CargoShips.Bring(state, alpha, Account);
                foreach (MarketGood line in alpha.Goods)
                    record.Stock[line.Good.Id] = line.Stock;
                record.Stock["water"] = shortOfWater ? 1 : 400;
                state.ShipOrders.Clear();
                CargoShips.Tick(state, book, Account);
            }
        }
        Hours(nature.GrowAfterHours + 5, false, true);
        Check("with a ship standing idle it doesn't grow", record.ExtraShips == 0);
        Hours(nature.GrowAfterHours + 5, true, false);
        Check("with nothing short it doesn't grow", record.ExtraShips == 0);
        Hours(nature.GrowAfterHours / 2, true, true);
        Check("a shortage for a while leaves room for free traders", record.ExtraShips == 0);
        Hours(nature.GrowAfterHours, true, true);
        Check($"deep and lasting, with its ships busy, it adds a ship ({record.ExtraShips})", record.ExtraShips == 1);
        Check("and orders it", state.ShipOrders.Values.Any(o => o.Home == "ALPHA"));
        Hours(nature.GrowAfterHours * 5, true, true);
        Check($"up to its limit ({record.ExtraShips} of {nature.MostExtraShips})", record.ExtraShips == nature.MostExtraShips);
        record.ExtraShips = 0;
        ((OrganisationRecord)Account(owner)).Credits = 1000;
        Hours(nature.GrowAfterHours * 2, true, true);
        Check("without money to spare it doesn't", record.ExtraShips == 0);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
