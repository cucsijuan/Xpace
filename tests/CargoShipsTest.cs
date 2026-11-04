using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Cargo ships as records, on the game's own stations and fleets, with no scene loaded:
///
///   - the book reads every station and gate of the map, and finds the way across a gate;
///   - a voyage takes what the ship's numbers say, longer laden;
///   - the fleets are mustered once, with the owner of the station they work for;
///   - left alone for days the ships keep the chain going: they buy, they pay the sellers and the tolls, chips and
///     copper reach the first station, and the factories there still have what they need;
///   - the same universe run again comes out the same (no dice);
///   - a ship lost is gone, and its owner orders another from the yard, built of real materials;
///   - ships in the middle of a voyage are kept on disk, and a lost one stays lost.
///
/// Run: Godot --headless --path . res://tests/cargo_ships_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CargoShipsTest : Node
{
    private const double Hours = 200, StepSeconds = 10;

    private int _failures;

    public override void _Ready()
    {
        var map = GD.Load<SectorMap>("res://assets/sectors/map.tres");
        StationBook book = StationBook.Read(map);
        Check($"the book has the stations and the gates ({string.Join(", ", book.Stations.Select(s => s.Id))})", book.Stations.Count == 4 && book.Gates.Count == 2);
        StationBook.Station first = book.Find("ALPHA"), kestrel = book.Find("DRYDOCK"), quartz = book.Find("QUARTZ");
        Check("there is a way across the gate, and none is needed inside a sector",
              book.Way(first.Sector, kestrel.Sector)?.Count == 1 && book.Way(first.Sector, quartz.Sector)?.Count == 0);

        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        double empty = book.Seconds(first, kestrel, model, 0), laden = book.Seconds(first, kestrel, model, model.Hull.CargoCapacity);
        GD.Print($"Station to Drydock: {empty / 60:0.0} min empty, {laden / 60:0.0} laden; Station to Quartz {book.Seconds(first, quartz, model, 0) / 60:0.0} min");
        Check("a laden ship takes longer", laden > empty && empty > model.HandlingSeconds);

        (UniverseState state, Dictionary<string, OrganisationRecord> owners) = Run(book, 1);
        foreach (StationBook.Station station in book.Stations)
            GD.Print($"  {station.Id}: " + string.Join(", ", station.Goods.Where(g => Market.Net(g.Good.Id, station.Goods, station.Factories) < 0)
                .Select(g => $"{g.Good.Id} {state.Station(station.Id).Stock[g.Good.Id]:0}/{g.Stock}")));
        foreach (CargoShipRecord ship in state.CargoShips.Values)
            GD.Print($"  {ship.Id}: {ship.Delivered} t delivered");
        foreach (OrganisationRecord owner in owners.Values)
            GD.Print($"  {owner.Name}: {owner.Credits}");

        foreach (MarketGood line in first.Goods.Where(g => kestrel.Goods.Any(k => k.Good.Id == g.Good.Id)))
        {
            MarketGood there = kestrel.Goods.First(k => k.Good.Id == line.Good.Id);
            long here = Market.BuyPrice(state.Station("ALPHA"), line), away = Market.SellPrice(there, state.Station("DRYDOCK").Stock[line.Good.Id]);
            long fetch = Market.BuyPrice(state.Station("DRYDOCK"), there), home = Market.SellPrice(line, state.Station("ALPHA").Stock[line.Good.Id]);
            GD.Print($"  {line.Good.Id}: to Drydock {away - here:+0;-0}, to Station {home - fetch:+0;-0} a tonne");
        }
        foreach (string good in book.Stations.SelectMany(st => st.Goods).Select(g => g.Good.Id).Distinct())
        {
            double net = book.Stations.Sum(st => Market.Net(good, st.Goods, st.Factories));
            Check($"{good}: made as fast as it is used ({net:+0.0;-0.0} t/h)", net >= 0);
        }
        Check("all the owners gain, or at least hold, with their ships at work", owners.Values.All(o => o.Credits >= OrganisationCatalog.Find(o.Id).StartingCredits));
        Check($"the fleets are mustered ({state.CargoShips.Count} ships, owned as their stations)", state.CargoShips.Count == 8 && state.CargoShips.Values.All(s => s.Owner == book.Find(s.Home).Owner));
        Check("all of them have worked", state.CargoShips.Values.All(s => s.Delivered > 0));
        Check("the gates took their tolls", owners["gate_authority"].Credits > OrganisationCatalog.Find("gate_authority").StartingCredits);
        Check("nobody is ruined", owners.Values.All(o => o.Credits > 0));
        foreach (StationBook.Station station in book.Stations)
            foreach (MarketGood line in station.Goods.Where(g => Market.Net(g.Good.Id, station.Goods, station.Factories) < 0))
                Check($"{station.Id} isn't out of {line.Good.Id} ({state.Station(station.Id).Stock[line.Good.Id]:0})", state.Station(station.Id).Stock[line.Good.Id] >= 1);

        (UniverseState again, _) = Run(book, 1);
        Check("the same universe comes out the same", state.CargoShips.Values.All(s => again.CargoShips[s.Id].Delivered == s.Delivered));

        // A ship lost: gone with its cargo, its pad free, and its owner orders another from the yard
        CargoShipRecord lost = state.CargoShips["KESTREL-1-1"];
        int fleet = state.CargoShips.Count;
        CargoShips.Lose(state, lost.Id);
        Check("a ship lost is gone from the universe", !state.CargoShips.ContainsKey(lost.Id));
        CargoShips.Muster(state, book, id => Account(state, owners, id));
        Check("nothing gives it back for free", state.CargoShips.Count == fleet - 1);
        double started = state.Clock;
        for (; state.Clock < started + 2 * 3600; state.Clock += StepSeconds)
            CargoShips.Tick(state, book, id => Account(state, owners, id));
        ShipOrderRecord order = state.ShipOrders.Values.FirstOrDefault();
        Check($"its owner orders another from the yard ({order?.Id} at {order?.Yard}, ready in {(order?.Ready - state.Clock) / 3600:0.0} h)", order is { Yard: "DRYDOCK" } && order.Ready > state.Clock);
        Check($"what the buyer fits is bought from who makes it ({order?.Fittings.Count} pieces, the factory there at work)", order?.Fittings.Count > 0);
        string replacement = order?.Id;
        double hours = model.BuildHours;
        for (; state.Clock < started + (hours + 2) * 3600; state.Clock += StepSeconds)
            CargoShips.Tick(state, book, id => Account(state, owners, id));
        Check($"built, it is there ({state.CargoShips.Count} ships)", replacement != null && state.CargoShips.ContainsKey(replacement) && state.ShipOrders.Count == 0);
        for (; state.Clock < started + (hours + 5) * 3600; state.Clock += StepSeconds)
            CargoShips.Tick(state, book, id => Account(state, owners, id));
        Check($"and goes to work for its home ({state.CargoShips.GetValueOrDefault(replacement ?? "")?.Delivered} t)", state.CargoShips.GetValueOrDefault(replacement ?? "") is { Home: "KESTREL" } ship2 && (ship2.Delivered > 0 || ship2.At == "KESTREL"));

        string folder = Path.Combine(OS.GetUserDataDir(), "test_cargo");
        CargoShipRecord any = state.CargoShips.Values.First();
        UniverseStore.Save(state, folder);
        UniverseState back = UniverseStore.Load(folder);
        Check("ships are kept", back.CargoShips[any.Id].Delivered == any.Delivered && back.CargoShips[any.Id].Arrives == any.Arrives && back.CargoShips.Count == state.CargoShips.Count);
        CargoShips.Lose(state, any.Id);
        UniverseStore.Save(state, folder);
        Check("a lost ship stays lost after a load", !UniverseStore.Load(folder).CargoShips.ContainsKey(any.Id));
        Directory.Delete(folder, true);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static IAccount Account(UniverseState state, Dictionary<string, OrganisationRecord> owners, string id) => string.IsNullOrEmpty(id) ? null : state.Organisation(id, () =>
    {
        Organisation spec = OrganisationCatalog.Find(id);
        return owners[id] = new OrganisationRecord { Id = id, Name = spec.Name, Credits = spec.StartingCredits, Margin = spec.Personality?.Margin ?? 0.15 };
    });

    private (UniverseState, Dictionary<string, OrganisationRecord>) Run(StationBook book, int unused)
    {
        var state = new UniverseState();
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        foreach (StationBook.Gate gate in book.Gates)
            Account(gate.Owner);
        CargoShips.Muster(state, book, Account);
        CargoShips.Muster(state, book, Account);
        for (double t = 0; t < Hours * 3600; t += StepSeconds)
        {
            state.Clock = t;
            CargoShips.Tick(state, book, Account);
        }
        return (state, owners);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
