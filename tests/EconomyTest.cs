using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The whole economy left to itself, free people included, for days: no base stops for good, ships keep working, free
/// traders make a living, owners stay in business, and what is wanted keeps arriving. It prints what each one earns.
/// A fuel shortage once stopped every base and ship here, and nothing else could see it.
///
/// Run: Godot --headless --path . res://tests/economy_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class EconomyTest : Node
{
    private const double SettleHours = 30, Hours = 100, Step = 30;
    private int _failures;

    public override void _Ready()
    {
        var population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        var state = new UniverseState();
        IAccount Account(string owner)
        {
            if (string.IsNullOrEmpty(owner))
                return null;
            if (owner.StartsWith(PersonRecord.OwnerPrefix))
                return state.People.GetValueOrDefault(owner);
            return state.Organisation(owner, () =>
            {
                Organisation spec = OrganisationCatalog.Find(owner);
                return new OrganisationRecord { Id = owner, Name = spec?.Name ?? owner, Credits = spec?.StartingCredits ?? 0, Margin = spec?.Personality?.Margin ?? 0.15 };
            });
        }
        CargoShips.Population = population;
        CargoShips.Muster(state, book, Account);
        People.Muster(state, population, book, Account);
        void Run(double hours)
        {
            for (double t = 0; t < hours * 3600; t += Step)
            {
                state.Clock += Step;
                CargoShips.Tick(state, book, Account);
            }
        }
        Run(SettleHours);
        var owners = state.Organisations.ToDictionary(o => o.Key, o => o.Value.Credits);
        var people = state.People.ToDictionary(p => p.Key, p => p.Value.Credits);
        var delivered = state.CargoShips.ToDictionary(s => s.Key, s => s.Value.Delivered);
        var idleHours = new Dictionary<string, int>();
        for (int hour = 0; hour < Hours; hour++)
        {
            Run(1);
            foreach (StationRecord station in state.Stations.Values.Where(s => s.Idle))
                idleHours[station.Id] = idleHours.GetValueOrDefault(station.Id) + 1;
        }
        foreach (OrganisationRecord o in state.Organisations.Values.OrderBy(o => o.Id).Where(o => owners.ContainsKey(o.Id)))
            GD.Print($"     {o.Id}: {(o.Credits - owners[o.Id]) / Hours:N0} an hour, has {o.Credits:N0}");
        foreach (PersonRecord p in state.People.Values.OrderBy(p => p.Id))
            GD.Print($"     {p.Name} ({p.Livelihood}): {(p.Credits - people.GetValueOrDefault(p.Id)) / Hours:N0} an hour, has {p.Credits:N0}");
        double tonnes = state.CargoShips.Values.Sum(s => s.Delivered - delivered.GetValueOrDefault(s.Id)) / Hours;
        int dry = state.CargoShips.Values.Count(s => s.Dry);
        GD.Print($"     {tonnes:0} t an hour delivered; idle hours: {string.Join(", ", idleHours.Select(kv => $"{kv.Key} {kv.Value}"))}; {dry} ships dry");

        foreach (StationBook.Station station in book.Stations)
            if (station.Goods.FirstOrDefault(g => g.Good.Id == Market.Fuel) is MarketGood fuel)
                GD.Print($"     {station.Id}: hydrogen {state.Station(station.Id).Stock[Market.Fuel]:0} of {fuel.Stock}, {Market.BuyPrice(state.Station(station.Id), fuel)} a tonne");
        Check($"no base spends most of its time stopped ({idleHours.Values.DefaultIfEmpty().Max()} of {Hours} h at worst)", idleHours.Values.DefaultIfEmpty().Max() < Hours / 2);
        Check("no base is stopped at the end", !state.Stations.Values.Any(s => s.Idle));
        Check($"goods keep arriving ({tonnes:0} t an hour)", tonnes > 150);
        List<PersonRecord> traders = state.People.Values.Where(p => p.Livelihood == nameof(Livelihood.Trader)).ToList();
        Check($"free traders make a living, as a whole ({traders.Sum(t => t.Credits - people.GetValueOrDefault(t.Id)) / Hours:N0} an hour)", traders.Sum(t => t.Credits) > traders.Sum(t => people.GetValueOrDefault(t.Id)));
        Check($"most ships have the fuel for their way ({dry} of {state.CargoShips.Count} dry)", dry < state.CargoShips.Count / 2);
        Check("no owner is going under", state.Organisations.Values.All(o => o.BrokeSince < 0));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
