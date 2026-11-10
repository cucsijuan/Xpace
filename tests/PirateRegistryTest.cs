using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Free people who go outside the law do with registries what a pilot does (records only, no scenes):
///
///   - at a pirate base, a shady person with money and no false callsign buys one; an aggressive one doesn't;
///   - a lawful station with a registry they haven't copied lately: they would dock there as someone else, and a station
///     whose owner has a price on their head lets them dock all the same, as long as the callsign passes;
///   - docked as someone else, they copy the registry into their loot, between its least and its most; the terminal
///     logs the false callsign, which is no good any more; with the loot they set off for the nearest fence;
///   - nothing new to copy, or nothing to pass as: they dock as themselves and copy nothing.
///
/// Run: Godot --headless --path . res://tests/pirate_registry_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PirateRegistryTest : Node
{
    private int _failures;

    public override void _Ready()
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
            OrganisationRecord made = state.Organisation(owner, () => new OrganisationRecord { Id = owner, Name = owner, Credits = 2_000_000 });
            return made;
        }
        CargoShips.Population = population;
        CargoShips.Muster(state, book, Account);
        People.Muster(state, population, book, Account);

        var shady = GD.Load<Personality>("res://assets/personalities/shady.tres");
        var aggressive = GD.Load<Personality>("res://assets/personalities/aggressive.tres");
        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == "Pirate");
        pirate.Character = shady.ResourcePath;
        pirate.Credits = 50_000;
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Owner == pirate.Id);
        ShipModel model = ResourceCache.Load<ShipModel>(ship.Model);
        ship.Loot.Clear();
        ship.Tonnes = 0;
        StationBook.Station redhold = book.Find("REDHOLD"), alpha = book.Find("ALPHA");
        Check("the book has Alpha's registry and Redhold's dealer", alpha.RegistryMax > 0 && redhold.Outfits.Any(o => o?.Item is CallsignSpec));

        // At the pirate base
        Park(ship, redhold);
        People.Plan(ship, model, pirate, population, state, book, Account);
        Check($"a shady pirate with money buys a false callsign at a fence ({string.Join(", ", pirate.Callsigns)})",
              pirate.Callsigns.Count == 1 && Registries.Usable(state, pirate.Callsigns[0]) != null && pirate.Credits < 50_000);
        string sign = pirate.Callsigns[0];

        PersonRecord brute = state.People.Values.First(p => p.Livelihood == "Pirate" && p != pirate);
        brute.Character = aggressive.ResourcePath;
        brute.Credits = 50_000;
        CargoShipRecord bruteShip = state.CargoShips.Values.First(s => s.Owner == brute.Id);
        Park(bruteShip, redhold);
        People.Plan(bruteShip, ResourceCache.Load<ShipModel>(bruteShip.Model), brute, population, state, book, Account);
        Check("an aggressive one doesn't", brute.Callsigns.Count == 0);

        // A lawful station
        Check("Alpha's registry would be worth a visit as someone else", CargoShips.Heist(alpha, pirate, state, Account) &&
              CargoShips.Disguise(ship, alpha, state, Account) == sign);
        string owner = CargoShips.OwnerOf(state, alpha);
        ((OrganisationRecord)Account(owner)).Bounties[pirate.Id] = 1000;
        Check("wanted there, still let in as someone else", !CargoShips.Welcomes(alpha, pirate.Id, state, Account) && CargoShips.Admits(alpha, pirate.Id, state, Account));

        // Docked there as someone else
        Park(ship, alpha);
        ship.Sending = sign;
        People.Plan(ship, model, pirate, population, state, book, Account);
        int tonnes = ship.Loot.GetValueOrDefault("registry_data");
        Check($"docked as {sign}: the registry copied into the loot ({tonnes} t, between {alpha.RegistryMin} and {alpha.RegistryMax})",
              tonnes >= alpha.RegistryMin && tonnes <= alpha.RegistryMax);
        Check("the terminal logged the false callsign: no good any more", state.Forged[sign].Burned && Registries.Usable(state, pirate) == null);
        Check($"with the loot, off to the nearest fence ({ship.Going})", ship.Going == "REDHOLD" && ship.Sending == "");

        // Nothing new, nothing to pass as
        Check("nothing new in Alpha's registry for them now", !CargoShips.Heist(alpha, pirate, state, Account));
        Check("and no callsign that passes: they dock as themselves", CargoShips.Disguise(ship, alpha, state, Account) == "");

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static void Park(CargoShipRecord ship, StationBook.Station at)
    {
        ship.At = ship.Going = at.Id;
        ship.Phase = CargoShipRecord.Parked;
        ship.State = CargoShipRecord.Waiting;
        ship.Sending = "";
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
