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
/// A ship going into a gate is followed by whoever was after it and saw it go, when that is worth the tolls to them:
/// first the reckoning alone (a poor pirate, a quarry, a hunter and a price, a hold read empty or full), then real ships
/// where no player is: a pirate after a freighter that goes into a gate follows it through, pays the toll, and over there
/// goes for it again; then the player, with the pirate after them, goes through a gate, and the pirate follows.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/gate_chase_test.tscn
/// </summary>
public partial class GateChaseTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Reckoning();

        Universe.Current.Forget();
        Universe.Current.Population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        var view = main.GetNode<CargoView>("CargoView");
        view.MeetEverySeconds = 1;
        main.FightQuietSeconds = 5;
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == nameof(Livelihood.Pirate));
        foreach (PersonRecord other in state.People.Values.Where(p => p != pirate))
            other.Memories.Clear();
        pirate.Memories.Clear();
        CargoShipRecord attacker = state.CargoShips.Values.First(s => s.Owner == pirate.Id);
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        CargoShipRecord prey = state.CargoShips.Values.First(s => s.Model == freighter.ResourcePath && s.Owner != pirate.Id);

        // The freighter on the way from Kestrel to Alpha, a little short of the gate; the pirate beside it, bound for
        // Drydock on this side of the gate: going through is its own choice
        StationBook.Station kestrel = book.Find("KESTREL"), alpha = book.Find("ALPHA");
        Set(prey, kestrel, alpha, 25, state, book);
        Vector3 near = book.FixOf(prey, freighter, state.Clock).Value.At + new Vector3(0, 300, 0);
        attacker.Phase = CargoShipRecord.Flying;
        attacker.From = "";
        attacker.FromSector = kestrel.Sector;
        (attacker.FromX, attacker.FromY, attacker.FromZ) = (near.X, near.Y, near.Z);
        attacker.Going = "DRYDOCK";
        attacker.LeftDock = attacker.LeftPad = attacker.Dock = attacker.Pad = 0;
        attacker.Left = attacker.WorkedTo = state.Clock;
        attacker.Legs = null;
        attacker.Arrives = state.Clock + book.Way(attacker, GD.Load<ShipModel>(attacker.Model), out Vector3 drydock).Sum(l => l.Seconds + l.Transit);
        attacker.ContactPoint = drydock;
        prey.Tonnes = 10;
        prey.Good = "iron";
        pirate.Credits = 5000;
        long before = pirate.Credits, hunted = Reactions.Hunted(state, pirate.Id);
        var follows = new List<(string, string)>();
        view.Followed += (a, b) => follows.Add((a, b));

        Check("the pirate goes after the freighter", await Until(() => attacker.Phase == CargoShipRecord.Loose, 30));
        bool went = await Until(() => Sector(prey, book, state) != "sector_02", 90);
        Check($"the freighter goes into the gate ({Sector(prey, book, state)})", went);
        bool followed = await Until(() => follows.Any(f => f.Item1 == attacker.Id && f.Item2 == prey.Id), 5);
        Check($"the pirate saw it go and follows it through ({follows.Count})", followed);
        Check($"on its way to where the freighter goes ({attacker.Going})", attacker.Going == prey.Going && attacker.Phase == CargoShipRecord.Flying);
        Check($"paying the toll ({before - pirate.Credits} credits), or wanted by the gate's owner going through unpaid (price on its head {hunted} -> {Reactions.Hunted(state, pirate.Id)})", before - pirate.Credits == CargoShips.TollsBetween(book, kestrel, alpha) || before == pirate.Credits && Reactions.Hunted(state, pirate.Id) > hunted);
        bool through = await Until(() => Sector(attacker, book, state) == "sector_01", 120);
        Check($"it goes through the gate too ({Sector(attacker, book, state)})", through);
        bool again = await Until(() => attacker.Phase == CargoShipRecord.Loose && view.Shown.GetValueOrDefault(attacker.Id) is ShipBody hunter && IsInstanceValid(hunter)
            && hunter.Find<FighterAi>()?.Engagement is EscortDuty duty && duty.Has(view.Shown.GetValueOrDefault(prey.Id)), 240);
        Check("and over there goes for that freighter again", again);

        // The freighter docked out of the way: the pirate is after nobody but the player now
        prey.Phase = CargoShipRecord.Parked;
        prey.At = "ALPHA";
        prey.Dock = prey.Pad = 0;
        view.Shown.GetValueOrDefault(prey.Id)?.QueueFree();
        await Player(main, view, pirate, attacker, follows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // The player near a gate with the pirate after them: the player goes through, the pirate saw it and follows
    private async Task Player(SectorHost main, CargoView view, PersonRecord pirate, CargoShipRecord attacker, List<(string, string)> follows)
    {
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        ShipBody player = main.Ship;
        string pilot = Universe.Current.PilotIdOf(player);
        Check($"the player's ship is a pilot's ({pilot})", pilot != null);
        if (pilot == null)
            return;
        var gate = main.Sector.GetNode<JumpGate>("Gate2");
        Vector3 axis = gate.GlobalBasis.Z.Normalized();
        player.State = new ShipState { Position = gate.GlobalPosition + axis * 4000, Orientation = Basis.LookingAt(-axis, Vector3.Up).GetRotationQuaternion() };
        await Seconds(0.5);
        view.Shown.GetValueOrDefault(attacker.Id)?.QueueFree();
        pirate.Quarry = pilot;
        pirate.Credits = 5000;
        long before = pirate.Credits, hunted = Reactions.Hunted(state, pirate.Id);
        Vector3 near = ToSector(player) + gate.GlobalBasis.X.Normalized() * 3000;
        attacker.Phase = CargoShipRecord.Flying;
        attacker.From = "";
        attacker.FromSector = "sector_01";
        (attacker.FromX, attacker.FromY, attacker.FromZ) = (near.X, near.Y, near.Z);
        attacker.Going = "ALPHA";
        attacker.Left = attacker.WorkedTo = state.Clock;
        attacker.Legs = null;
        attacker.Arrives = state.Clock + book.Way(attacker, GD.Load<ShipModel>(attacker.Model), out Vector3 contact).Sum(l => l.Seconds + l.Transit);
        attacker.ContactPoint = contact;
        bool found = await Until(() => attacker.Phase == CargoShipRecord.Loose && view.Shown.GetValueOrDefault(attacker.Id) is ShipBody hunter && IsInstanceValid(hunter)
            && hunter.Find<FighterAi>()?.Engagement is EscortDuty duty && duty.Has(player), 30);
        Check("the pirate finds the player it is after", found);
        await Seconds(8);                               // its radar on them

        // Through the ring at 150 m/s, along its axis
        FloatingOrigin here = FloatingOrigin.Of(player);
        for (int i = 0; i < 60 * 60 && FloatingOrigin.Of(player) == here && player.Holder == null; i++)
        {
            Vector3 at = gate.GlobalPosition + axis * (4000 - 150 * (i + 1) / 60f);
            player.State = new ShipState { Position = at, Orientation = Basis.LookingAt(-axis, Vector3.Up).GetRotationQuaternion(), Velocity = -axis * 150 };
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        Check("the player goes through the gate", FloatingOrigin.Of(player) != here);
        bool followed = await Until(() => follows.Any(f => f.Item1 == attacker.Id && f.Item2 == pilot), 5);
        Check($"the pirate saw the player go and follows ({attacker.Going})", followed && attacker.Phase == CargoShipRecord.Flying);
        StationBook.Gate exit = book.Gates.First(g => g.Sector == gate.Destination && g.Name == gate.ExitGate);
        string nearest = book.Stations.Where(st => st.Sector == exit.Sector).OrderBy(st => st.At.DistanceTo(exit.At)).First().Id;
        Check($"to the station nearest the gate the player came out of ({nearest})", attacker.Going == nearest);
        Check($"paying the toll ({before - pirate.Credits} credits), or wanted by the gate's owner going through unpaid (price on its head {hunted} -> {Reactions.Hunted(state, pirate.Id)})", before - pirate.Credits == exit.Toll || before - pirate.Credits == 100 || before == pirate.Credits && Reactions.Hunted(state, pirate.Id) > hunted);
        bool through = await Until(() => Sector(attacker, book, state) == gate.Destination, 120);
        Check($"it goes through the gate too ({Sector(attacker, book, state)})", through);
        bool again = await Until(() => attacker.Phase == CargoShipRecord.Loose && view.Shown.GetValueOrDefault(attacker.Id) is ShipBody hunter && IsInstanceValid(hunter)
            && hunter.Find<FighterAi>()?.Engagement is EscortDuty duty && duty.Has(player), 240);
        Check("and over there goes for the player again", again);
    }

    private static Vector3 ToSector(Node3D ship)
    {
        SectorPoint at = FloatingOrigin.Of(ship).Origin + ship.Position;
        return new Vector3((float)at.X, (float)at.Y, (float)at.Z);
    }

    private const string Crew = "res://assets/personalities/steady.tres";

    // Who follows, and for what
    private void Reckoning()
    {
        var state = new UniverseState();
        // A crew of the defaults: one tonne is worth going for, three times the tolls worth following
        var pirate = new PersonRecord { Id = "person-1", Livelihood = nameof(Livelihood.Pirate), Credits = 1000, Character = Crew };
        var hunter = new PersonRecord { Id = "person-2", Livelihood = nameof(Livelihood.Hunter), Credits = 1000, Character = Crew };
        var trader = new CargoShipRecord { Owner = "org" };
        var outlaw = new CargoShipRecord { Owner = "person-3" };
        Check("a pirate that can't pay the tolls doesn't follow", !People.Follows(new PersonRecord { Livelihood = nameof(Livelihood.Pirate), Credits = 50, Character = Crew }, trader.Owner, 100, null, state));
        Check("before it has looked, a pirate follows for one toll", People.Follows(pirate, trader.Owner, 100, null, state));
        Check("but not for three", !People.Follows(pirate, trader.Owner, 300, null, state));
        Check("a hold read empty isn't worth a toll", !People.Follows(pirate, trader.Owner, 100, 0, state));
        Check("a hold read full is worth several", People.Follows(pirate, trader.Owner, 300, 1500, state));
        Check("a hunter follows no one without a price on them", !People.Follows(hunter, outlaw.Owner, 100, null, state));
        state.Organisations["org"] = new OrganisationRecord { Id = "org" };
        state.Organisations["org"].Bounties["person-3"] = 2000;
        Check("and one with a price on them it does", People.Follows(hunter, outlaw.Owner, 300, null, state));
        Check("a quarry is followed whatever it costs", People.Follows(new PersonRecord { Livelihood = nameof(Livelihood.Trader), Credits = 1000, Quarry = "person-3", Character = Crew }, outlaw.Owner, 900, null, state));
    }

    // On the way from one station to another, so many seconds short of the end of its first gate's leg
    private static void Set(CargoShipRecord s, StationBook.Station from, StationBook.Station to, double short_, UniverseState state, StationBook book)
    {
        ShipModel m = GD.Load<ShipModel>(s.Model);
        var way = book.Departure(from, null, to, m, 0, out Vector3 contact);
        double gate = 0;
        foreach (StationBook.Leg leg in way)
        {
            gate += leg.Seconds;
            if (leg.Transit > 0)
                break;
        }
        s.Phase = CargoShipRecord.Flying;
        s.At = s.From = from.Id;
        s.Going = to.Id;
        s.FromSector = "";
        s.LeftDock = s.LeftPad = s.Dock = s.Pad = 0;
        s.ContactPoint = contact;
        s.Left = state.Clock - (gate - short_);
        s.Arrives = s.Left + way.Sum(l => l.Seconds + l.Transit);
        s.WorkedTo = state.Clock;
        s.Legs = null;
    }

    // Where its record has it now (in a gate's tunnel: none)
    private static string Sector(CargoShipRecord s, StationBook book, UniverseState state) =>
        book.FixOf(s, GD.Load<ShipModel>(s.Model), state.Clock)?.Sector ?? "tunnel";

    private async Task Seconds(double s)
    {
        for (int i = 0; i < s * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return done();
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
