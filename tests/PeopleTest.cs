using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The free people: they have names, ships and money of their own; memories fade and make room, and are one system for
/// all; heads have prices whoever wears them, paid to whoever gets them; traders make money on what they heard; a hunter
/// reads a station's board and goes where the quarry was seen, or trades with nothing on it; someone with a big enough
/// grudge goes after who wronged them; and a pirate who comes across a loaded ship where no player is starts a real
/// fight, in a sector run for it with nothing drawn.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/people_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PeopleTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Remembering();
        Records();
        await Fight();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Remembering()
    {
        var who = new PersonRecord { Id = "person-x", MemoryRoom = 3 };
        Memories.Note(who, "a", Memories.HitMe, -2, 0);
        Check("a grudge is below nothing", Memories.Feeling(who, "a", 0) == -2);
        Check($"after its half life it matters half ({Memories.Feeling(who, "a", 48 * 3600):0.00})", Math.Abs(Memories.Feeling(who, "a", 48 * 3600) + 1) < 0.001);
        Memories.Note(who, "b", "helped me", 0.1, 0);
        Memories.Note(who, "c", Memories.HitMe, -5, 0);
        Memories.Note(who, "d", Memories.HitMe, -3, 0);
        Check("with no room, what matters least goes", who.Memories.Count == 3 && who.Memories.All(m => m.About != "b"));
        Check("it knows who it hates most", Memories.WorstGrudge(who, 0).Actor == "c");
        Memories.Note(new OrganisationRecord(), "a", Memories.KilledMine, -3, 0);
    }

    private void Records()
    {
        var population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        Check("the population is data", population.Lines.Length == 3 && population.Lines.All(l => l.Model != null));
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
        CargoShips.Muster(state, book, Account);
        People.Muster(state, population, book, Account);
        int wanted = population.Lines.Sum(l => l.Count);
        Check($"{state.People.Count} people, as the population says", state.People.Count == wanted);
        Check("everyone has a first name and a surname", state.People.Values.All(p => p.First != "" && p.Last != ""));
        Check($"names differ ({string.Join(", ", state.People.Values.Select(p => p.Name))})", state.People.Values.Select(p => p.Name).Distinct().Count() == wanted);
        Check("everyone has a ship of their own", state.People.Keys.All(id => state.CargoShips.Values.Any(s => s.Owner == id)));
        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == "Pirate");
        Check("a pirate has a price on their head", Reactions.Hunted(state, pirate.Id) == 3000);

        // A hunter at a station reads its owner's board
        PersonRecord hunter = state.People.Values.First(p => p.Livelihood == "Hunter");
        CargoShipRecord hunterShip = state.CargoShips.Values.First(s => s.Owner == hunter.Id);
        string owner = CargoShips.OwnerOf(state, book.Find(hunterShip.At));
        StationBook.Station far = book.Stations.First(s => s.Id != hunterShip.At && s.Pads.Count > 0);
        ((OrganisationRecord)Account(owner)).Bounties["someone"] = 9000;
        People.Seen(state, "someone", far.Sector, far.At);
        hunterShip.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"a hunter takes the offer on the board and goes where the quarry was seen ({hunter.Quarry}, to {hunterShip.Going})", hunter.Quarry == "someone" && hunterShip.Going == far.Id);

        long start = state.People.Values.Where(p => p.Livelihood == "Trader").Sum(p => p.Credits);
        for (int i = 0; i < 40 * 60; i++)
        {
            state.Clock += 60;
            CargoShips.Tick(state, book, Account);
        }
        long end = state.People.Values.Where(p => p.Livelihood == "Trader").Sum(p => p.Credits);
        Check($"in 40 hours traders make a living ({start} -> {end})", end > start);

        // A grudge is reason enough, with no money in it
        PersonRecord trader = state.People.Values.First(p => p.Livelihood == "Trader");
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Owner == trader.Id);
        Memories.Note(trader, pirate.Id, Memories.HitMe, -50, state.Clock);
        StationBook.Station there = book.Stations.First(s => s.Pads.Count > 0 && s.Id != (ship.At ?? ""));
        People.Seen(state, pirate.Id, there.Sector, there.At);
        ship.Phase = CargoShipRecord.Parked;
        ship.State = CargoShipRecord.Waiting;
        ship.NextLook = 0;
        CargoShips.Tick(state, book, Account);
        Check($"a big grudge sends someone after who did it ({trader.Quarry})", trader.Quarry == pirate.Id);

        // Whoever gets them is paid, and a person's ship takes them with it
        long before = hunter.Credits;
        Check("what was on a head goes to a person who gets it", Reactions.Collect(state, pirate.Id, hunter) == 3000 && hunter.Credits == before + 3000);
        CargoShips.Lose(state, state.CargoShips.Values.First(s => s.Owner == pirate.Id).Id);
        Check("a person who loses their ship is gone", !state.People.ContainsKey(pirate.Id));
        People.Muster(state, population, book, Account);
        Check("nobody comes at once to take their place", state.People.Count == wanted - 1);
        state.Clock += population.Lines[2].ReplaceHours * 3600 + 1;
        People.Muster(state, population, book, Account);
        Check("in time a newcomer does, ordering a ship from a yard", state.People.Count == wanted && state.ShipOrders.Values.Any(o => o.Owner.StartsWith(PersonRecord.OwnerPrefix)));

        // Stolen goods go for less than a market pays
        PersonRecord fence = state.People.Values.First(p => p.Livelihood == "Hunter" && p != hunter);
        CargoShipRecord loaded = state.CargoShips.Values.First(s => s.Owner == fence.Id);
        StationBook.Station market = book.Stations.First(s => s.Goods.Any(g => g.Good.Id == "iron") && s.Pads.Count > 0);
        loaded.At = market.Id;
        loaded.Phase = CargoShipRecord.Parked;
        loaded.State = CargoShipRecord.Waiting;
        loaded.NextLook = 0;
        loaded.Loot["iron"] = 5;
        StationRecord stock = CargoShips.Bring(state, market, Account);
        long honest = 5 * Market.SellPrice(market.Goods.First(g => g.Good.Id == "iron"), stock.Stock["iron"]);
        long purse = fence.Credits;
        CargoShips.Tick(state, book, Account);
        long fenced = fence.Credits - purse;
        Check($"a fence pays less than the market would ({fenced} of about {honest})", loaded.Loot.Count == 0 && fenced > 0 && fenced < honest * 0.75);

        // The universe's work and its documents come a piece at a time
        Check($"the universe's work comes in pieces ({CargoShips.Work(state, book, Account).Count()})", CargoShips.Work(state, book, Account).Count() > book.Stations.Count);
        string folder = Path.Combine(OS.GetUserDataDir(), "test", "people");
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        UniverseStore.Prepare(folder);
        foreach ((string file, string text) in UniverseStore.Documents(state, folder).ToList())
            UniverseStore.Write(file, text);
        Check("documents made one by one load as a whole", UniverseStore.Load(folder).People.Count == wanted);
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        UniverseStore.Save(state, folder);
        UniverseState again = UniverseStore.Load(folder);
        Check("people, their memories and sightings are kept on disk", again.People.Count == wanted && again.People[trader.Id].Memories.Count == trader.Memories.Count && again.Sightings.ContainsKey(pirate.Id) == state.Sightings.ContainsKey(pirate.Id) && again.PeopleEver == state.PeopleEver);
        Directory.Delete(folder, true);
    }

    private async Task Fight()
    {
        Universe.Current.Forget();
        var population = GD.Load<PopulationSpec>("res://assets/people/population.tres");
        Universe.Current.Population = population;
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        CargoView view = main.GetNode<CargoView>("CargoView");
        view.MeetEverySeconds = 1;
        main.FightQuietSeconds = 5;
        await Seconds(3);
        UniverseState state = Universe.Current.State;
        StationBook book = Universe.Current.Book;
        Check($"the universe has its people ({state.People.Count})", state.People.Count > 0);
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        ShipModel freighter = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        ShipModel fighter = GD.Load<ShipModel>("res://assets/ship/models/free_fighter.tres");
        PersonRecord pirate = state.People.Values.First(p => p.Livelihood == "Pirate");
        CargoShipRecord attacker = state.CargoShips.Values.First(s => s.Owner == pirate.Id);
        CargoShipRecord victim = state.CargoShips.Values.First(s => s.Model == freighter.ResourcePath);

        // Both on the same way, in the sector no player is in
        StationBook.Station from = book.Find("KESTREL"), to = book.Find("DRYDOCK");
        foreach ((CargoShipRecord s, ShipModel m) in new[] { (victim, freighter), (attacker, fighter) })
        {
            var way = book.Departure(from, null, to, m, 0, out Vector3 contact);
            s.Phase = CargoShipRecord.Flying;
            s.At = from.Id;
            s.From = from.Id;
            s.Going = to.Id;
            s.LeftDock = s.LeftPad = s.Dock = s.Pad = 0;
            s.ContactPoint = contact;
            s.Left = state.Clock - way.Sum(l => l.Seconds) * 0.5;
            s.Arrives = s.Left + way.Sum(l => l.Seconds + l.Transit);
            s.WorkedTo = state.Clock;
        }
        victim.Tonnes = 10;
        victim.Good = "iron";
        Check("nobody is in that sector", main.Root(to.Sector) == null);
        double worst = 0;
        long last = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 30 * 60 && attacker.Phase != CargoShipRecord.Loose; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            worst = Math.Max(worst, (now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            last = now;
        }
        GD.Print($"     worst step while the sector came up: {worst:0.0} ms");
        Check($"the pirate comes across a loaded ship and goes for it ({attacker.Phase})", attacker.Phase == CargoShipRecord.Loose);
        Check("the sector is run for the fight, with nothing drawn", main.Root(to.Sector) != null && main.Here.Id != to.Sector);
        if (view.Shown.TryGetValue(attacker.Id, out ShipBody ship))
        {
            Check("real ships, knowing who flies them", ship.Actor == pirate.Id && ship.Holder == null);
            Check("its orders are the prey", ship.Find<FighterAi>().Engagement.Hostiles.Any());
        }
        else
            Check("the pirate's ship is shown", false);

        // The crew throws the cargo out; the pirate wants that, not the ship
        await Until(() => view.Shown.ContainsKey(victim.Id), 5);
        ShipBody prey = view.Shown.GetValueOrDefault(victim.Id);
        ShipBody raider = view.Shown.GetValueOrDefault(attacker.Id);
        Check("the ship it went for is there too", prey != null && raider != null);
        if (prey == null || raider == null)
            return;
        var crew = new Personality();
        Check("the bold hold out longer than the mild", GD.Load<Personality>("res://assets/personalities/aggressive.tres").JettisonBelowHull < GD.Load<Personality>("res://assets/personalities/mediator.tres").JettisonBelowHull);
        Check("the more the cargo is worth, the longer a crew holds on", crew.JettisonAt(50000, 1e6f, false) < crew.JettisonAt(100, 1e6f, false));
        Check("the same near a protected zone", crew.JettisonAt(100, 500, false) < crew.JettisonAt(100, 1e6f, false));
        Check("and with help coming", crew.JettisonAt(100, 1e6f, true) < crew.JettisonAt(100, 1e6f, false));
        Check($"with everything in its favour it holds on to the last ({crew.JettisonAt(1e6, 0, true):0.00})", Math.Abs(crew.JettisonAt(1e6, 0, true) - crew.JettisonLastHull) < 0.001f);
        Check($"with nothing to hold on for, it starts at the first limit ({crew.JettisonAt(0, 1e6f, false):0.00})", Math.Abs(crew.JettisonAt(0, 1e6f, false) - crew.JettisonBelowHull) < 0.001f);
        ShipHull hull = prey.Find<ShipHull>();
        Personality aboard = OrganisationCatalog.Find(victim.Owner).Personality;
        double chance = aboard.JettisonChance;
        aboard.JettisonChance = 0;
        hull.Burn(hull.Left - hull.Integrity * 0.4f);
        Combat.Struck(prey, raider);
        await Seconds(0.5);
        Check($"as long as its nerve holds, the crew keeps its cargo ({victim.Tonnes} t)", victim.Tonnes == 10);
        aboard.JettisonChance = chance;
        hull.Burn(hull.Left - hull.Integrity * 0.15f);
        Combat.Struck(prey, raider);
        await Seconds(0.5);
        Check($"at the last limit it has to let go ({victim.Tonnes} t left)", victim.Tonnes == 0);
        Check("it is drifting in space", Pods(prey).Sum(p => p.Tonnes) == 10);
        Check("the pirate leaves it alone and goes for the cargo", await Until(() => !raider.Find<FighterAi>().Engagement.Hostiles.Any(), 10));
        CargoPod pod = Pods(prey).First();
        await Until(() => pod.Clear, 7);
        raider.State = new ShipState { Position = pod.Position + new Vector3(0, 20, 0), Orientation = Quaternion.Identity, Velocity = pod.Velocity };
        await Until(() => attacker.Loot.Count > 0, 12);
        Check($"it takes it aboard ({string.Join(", ", attacker.Loot)})", attacker.Loot.GetValueOrDefault("iron") == 10);
        Check("and goes on its way with it", await Until(() => attacker.Phase == CargoShipRecord.Flying, 40));

        Check("the fight over, the sector goes", await Until(() => main.Root(to.Sector) == null, 30));

        // Hunters: someone at the player's heels; a pirate on the way
        ShipBody me = main.Ship;
        PersonRecord hunter = state.People.Values.First(p => p.Livelihood == "Hunter");
        CargoShipRecord chaser = state.CargoShips.Values.First(s => s.Owner == hunter.Id);
        StationBook.Station alpha = book.Find("ALPHA"), quartz = book.Find("QUARTZ");
        var line = book.Departure(alpha, null, quartz, fighter, 0, out Vector3 near);
        chaser.Phase = CargoShipRecord.Flying;
        chaser.At = chaser.From = alpha.Id;
        chaser.Going = quartz.Id;
        chaser.LeftDock = chaser.LeftPad = chaser.Dock = chaser.Pad = 0;
        chaser.ContactPoint = near;
        chaser.Left = state.Clock - line.Sum(l => l.Seconds) * 0.5;
        chaser.Arrives = chaser.Left + line.Sum(l => l.Seconds + l.Transit);
        chaser.WorkedTo = state.Clock;
        await Until(() => view.Shown.ContainsKey(chaser.Id), 5);
        Check("the hunter's ship is shown here", view.Shown.ContainsKey(chaser.Id));

        attacker.Phase = CargoShipRecord.Flying;
        attacker.Going = quartz.Id;
        attacker.From = alpha.Id;
        attacker.FromSector = "";
        attacker.ContactPoint = near;
        attacker.Left = chaser.Left;
        attacker.Arrives = chaser.Arrives;
        attacker.WorkedTo = state.Clock;
        Check("a hunter on the way goes for a pirate with a price on them", People.Meetings(state, book, population.MeetMeters).Any(m => m.Attacker == chaser && m.Victim == attacker));
        attacker.Phase = CargoShipRecord.Parked;

        hunter.Quarry = Universe.Current.LocalPilotId;
        if (view.Shown.TryGetValue(chaser.Id, out ShipBody hunting))
        {
            me.State = new ShipState { Position = hunting.State.Position + new Vector3(0, 800, 0), Orientation = Quaternion.Identity };
            await Until(() => chaser.Phase == CargoShipRecord.Loose, 8);
            Check($"a hunter who finds its quarry goes for them ({chaser.Phase})", chaser.Phase == CargoShipRecord.Loose && hunting.Find<FighterAi>().Engagement.Hostiles.Contains(me));

            // Hurt, someone who fights runs instead
            await Hurt(hunting, chaser, me);
            chaser.Phase = CargoShipRecord.Parked;
        }

        // A player's own cargo: thrown out by the lever, scooped back
        PilotRecord pilot = Universe.Current.RecordOf(me);
        pilot.Ship.Cargo["iron"] = 4;
        await Seconds(0.1);
        me.State = new ShipState { Position = me.State.Position, Orientation = Quaternion.Identity };
        Universe.Current.Jettison(me);
        await Seconds(0.1);
        Check("the hold's release throws it all out", pilot.Ship.Tonnes == 0 && Pods(me).Sum(p => p.Tonnes) == 4);
        CargoPod mine = Pods(me).First();
        await Seconds(1);
        Check("it doesn't come straight back", pilot.Ship.Tonnes == 0);
        await Seconds(5);
        me.State = new ShipState { Position = mine.Position, Orientation = Quaternion.Identity, Velocity = mine.Velocity };
        await Until(() => pilot.Ship.Tonnes > 0, 3);
        Check("flying slowly past a pod scoops it", pilot.Ship.Tonnes == 4);
        pilot.Ship.Cargo["iron"] = 3;
        Universe.Current.Lose(me);
        await Seconds(0.1);
        Check("a ship blown up leaves what it carried", Pods(me).Sum(p => p.Tonnes) == 3);

    }

    // A person hurt badly enough stops fighting and runs from who did it
    private async Task Hurt(ShipBody raider, CargoShipRecord record, ShipBody by)
    {
        Combat.Struck(raider, by);
        await Until(() => record.Phase == CargoShipRecord.Loose, 3);
        var duty = (EscortDuty)raider.Find<FighterAi>().Engagement;
        Check("hit, a person who fights fights back", record.Phase == CargoShipRecord.Loose && duty.Fights && duty.Hostiles.Contains(by));
        ShipHull hull = raider.Find<ShipHull>();
        hull.Burn(hull.Left - hull.Integrity * 0.2f);
        await Until(() => !duty.Fights, 3);
        Check($"with little hull left they run instead ({hull.Fraction:0.00})", !duty.Fights);
    }

    private static List<CargoPod> Pods(Node3D near) =>
        Sectors.InGroup(near, CargoPod.Group).OfType<CargoPod>().Where(GodotObject.IsInstanceValid).Where(p => p.Tonnes > 0).ToList();

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * 60 && !done(); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        return done();
    }
}
