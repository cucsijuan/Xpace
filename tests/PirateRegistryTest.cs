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
///   - docked as someone else, they work the registry's terminal: whether its watch traces them is a throw of the dice
///     (they don't play its keys); not traced, it goes into their loot, between its least and its most, and the false
///     callsign still passes; with the loot they set off for the nearest fence; traced, the callsign is found out and
///     the bounty for it is on their head with the owner;
///   - nothing new to copy, or nothing to pass as: they dock as themselves and copy nothing;
///   - with no callsign that passes, they slip in unseen, as a pilot can: the station's hangar watches see none of the
///     way along the hull to beside a mouth, where they wait, radio off, until the door opens for someone else, and
///     then onto a free pad nobody cleared them to; the logbook never has them; traced there, nobody can name them;
///     they leave the same way, when the door opens again.
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
        alpha.RegistryTraceChance = 0;
        People.Plan(ship, model, pirate, population, state, book, Account);
        int tonnes = ship.Loot.GetValueOrDefault("registry_data");
        Check($"docked as {sign}, not traced: the registry copied into the loot ({tonnes} t, between {alpha.RegistryMin} and {alpha.RegistryMax})",
              tonnes >= alpha.RegistryMin && tonnes <= alpha.RegistryMax);
        Check("the false callsign still passes", !state.Forged[sign].Burned && Registries.Usable(state, pirate) != null);
        Check($"with the loot, off to the nearest fence ({ship.Going})", ship.Going == "REDHOLD" && ship.Sending == "");

        // Traced as someone else
        var board = (OrganisationRecord)Account(owner);
        long wanted = board.Bounties.GetValueOrDefault(pirate.Id);
        state.Station(alpha.Id).RegistryCopied.Remove(pirate.Id);
        ship.Loot.Clear();
        Park(ship, alpha);
        ship.Sending = sign;
        alpha.RegistryTraceChance = 1;
        People.Plan(ship, model, pirate, population, state, book, Account);
        Check($"traced as {sign}: no loot, the callsign found out, the bounty for it on their head ({wanted} -> {board.Bounties.GetValueOrDefault(pirate.Id)})",
              ship.Loot.Count == 0 && state.Forged[sign].Burned && board.Bounties.GetValueOrDefault(pirate.Id) == wanted + alpha.ForgeryBounty);

        // Nothing new, nothing to pass as
        Check("nothing new in Alpha's registry for them now", !CargoShips.Heist(alpha, pirate, state, Account));
        Check("and no callsign that passes: they dock as themselves", CargoShips.Disguise(ship, alpha, state, Account) == "");

        // The way in unseen
        StationBook.Pad door = alpha.Pads.First();
        StationBook.Slipway? slip = StationBook.Slip(alpha, door, null);
        Vector3 ahead = door.Door + (door.Door - door.Inside).Normalized() * 1000;
        Check($"Alpha's hangars have their watches ({alpha.Watches.Count}), and a way in that none of them sees",
              alpha.Watches.Count == 4 && slip is StationBook.Slipway way && StationBook.Unseen(alpha, (way.Lurk, way.Hug), (way.Hug, way.Front), (way.Front, door.Mouth), (door.Mouth, door.Inside)));
        Check("straight at the mouth, the watch sees it", !StationBook.Unseen(alpha, (ahead, door.Door)));

        // Slipping in: a shady pirate at a fence with no callsign that passes (the dealer has none left)
        foreach (OutfitLine line in redhold.Outfits.Where(o => o?.Item is CallsignSpec))
            state.Station(redhold.Id).Stock[line.Id] = 0;
        foreach (StationRecord record in state.Stations.Values)
            record.RegistryCopied.Remove(pirate.Id);
        ship.Loot.Clear();
        Park(ship, redhold);
        // Seen slipping in everywhere but Alpha lately: they keep away from there
        foreach (StationBook.Station other in book.Stations.Where(s => s != alpha))
            pirate.Spotted[other.Id] = state.Clock;
        People.Plan(ship, model, pirate, population, state, book, Account);
        StationBook.Station target = book.Find(ship.Going);
        Check($"no callsign that passes: off to slip into {ship.Going}'s hangar {ship.SlipDock}, radio off (the stations whose watches saw them lately kept away from)",
              target == alpha && target.RegistryMax > 0 && ship.SlipDock > 0 && ship.Unseen && ship.Phase == CargoShipRecord.Flying);
        target.RegistryTraceChance = 0;
        // Only this one works here: the other ships stand still
        foreach (CargoShipRecord other in state.CargoShips.Values.Where(s => s != ship))
            other.NextLook = double.MaxValue;
        state.Clock = ship.Arrives;
        CargoShips.Tick(state, book, Account);
        state.Clock += 1;
        CargoShips.Tick(state, book, Account);
        StationBook.Fix? waits = book.FixOf(ship, model, state.Clock);
        StationBook.Pad mouth = target.Pads.First(p => p.Dock == ship.SlipDock);
        Check($"it waits beside the mouth of hangar {ship.SlipDock}, out of its watches' sight, for the door ({waits?.At.DistanceTo(mouth.Mouth):0} m from it)",
              ship.Phase == CargoShipRecord.Holding && waits is StationBook.Fix w && w.At.DistanceTo(mouth.Mouth) < 100 &&
              target.Watches.All(watch => !watch.Sees(w.At)));
        // A ship of the records cleared into that hangar
        CargoShipRecord cleared = state.CargoShips.Values.First(s => s != ship && s.Owner != pirate.Id && !ResourceCache.Load<ShipModel>(s.Model).Escort);
        cleared.Phase = CargoShipRecord.Landing;
        cleared.Going = target.Id;
        cleared.Dock = ship.SlipDock;
        cleared.Pad = 99;
        cleared.Left = state.Clock;
        cleared.Arrives = state.Clock + 1e6;
        state.Clock += CargoShips.DoorLookSeconds + 0.1;
        CargoShips.Tick(state, book, Account);
        Check($"the door opens for another ship: in it goes, onto pad {ship.Pad}", ship.Phase == CargoShipRecord.Landing && ship.Pad > 0 && ship.Dock == mouth.Dock);
        int logged = state.Station(target.Id).Logbook.Count;
        state.Clock = ship.Arrives;
        CargoShips.Tick(state, book, Account);
        Check("down on the pad, stowed: nobody cleared it, so the logbook doesn't have it",
              ship.Phase == CargoShipRecord.Parked && ship.Stowed && state.Station(target.Id).Logbook.Count == logged);
        state.Clock = ship.NextLook;
        CargoShips.Tick(state, book, Account);
        tonnes = ship.Loot.GetValueOrDefault("registry_data");
        Check($"with the door open again, its crew has the registry ({tonnes} t) and slips out for a fence ({ship.Going}), radio still off",
              tonnes > 0 && ship.Phase == CargoShipRecord.Flying && ship.SlipOut && ship.Unseen && !ship.Stowed);
        List<StationBook.Leg> legs = book.Way(ship, model, out _);
        Check("the way out of the hangar is one its watches don't see",
              StationBook.Unseen(target, legs.Where(l => l.Sector == target.Sector).Take(7).Select(l => (l.From, l.To)).ToArray()));

        // Traced while stowed
        state.Station(target.Id).RegistryCopied.Remove(pirate.Id);
        ship.Loot.Clear();
        Park(ship, target);
        ship.Stowed = true;
        ship.Dock = mouth.Dock;
        ship.Pad = mouth.Number;
        target.RegistryTraceChance = 1;
        var targetBoard = Account(CargoShips.OwnerOf(state, target)) as OrganisationRecord;
        long before = targetBoard?.Bounties.GetValueOrDefault(pirate.Id) ?? 0;
        state.Clock = ship.NextLook = state.Clock + 1;
        CargoShips.Tick(state, book, Account);
        Check("traced on a pad it slipped onto: no loot, the service locked for them, and nobody can name them",
              ship.Loot.Count == 0 && !Registries.Fresh(state.Station(target.Id), pirate.Id, target.RegistryFreshHours, state.Clock) &&
              (targetBoard?.Bounties.GetValueOrDefault(pirate.Id) ?? 0) == before);

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
