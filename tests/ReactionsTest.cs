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
/// How owners answer damage, by their personalities:
///   - a lost ship keeps an owner off that way, a cautious one longer than an aggressive one;
///   - a loss elsewhere changes nothing, and neither does a hit;
///   - money goes on the attacker's head, more from the aggressive, never more than the owner has;
///   - the pilot who destroys the attacker is paid by the owner;
///   - need shortens fear, and a yard sends for what an order is waiting for;
///   - in the real game a cargo ship under fire calls on the radio and its owner hears of it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/reactions_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class ReactionsTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Records();
        YardFetches();
        await InGame();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Records()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        var bold = GD.Load<Personality>("res://assets/personalities/aggressive.tres");
        var meek = GD.Load<Personality>("res://assets/personalities/mediator.tres");
        Check($"a mediator stays away longer ({Reactions.FearHours(meek):0.0} h) than an aggressive owner ({Reactions.FearHours(bold):0.0} h)", Reactions.FearHours(meek) > Reactions.FearHours(bold));

        StationBook.Station alpha = book.Find("ALPHA"), drydock = book.Find("DRYDOCK");
        List<StationBook.Leg> legs = book.Departure(alpha, alpha.Pads[0], drydock, model, 0, out _);
        StationBook.Leg open = legs.First(l => l.Open);
        Vector3 onTheWay = open.From.Lerp(open.To, 0.5f);
        double hour = 3600, clock = 100 * hour;

        var owner = new OrganisationRecord { Id = "test", Credits = 1_000_000 };
        Reactions.Happened(owner, meek, model, open.Sector, onTheWay, "", clock, lost: false);
        Check("a ship hit doesn't close a way", !Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock));
        Reactions.Happened(owner, meek, model, open.Sector, onTheWay + new Vector3(0, 90000, 0), "", clock, lost: true);
        Check("a ship lost far from the way doesn't either", !Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock));
        Reactions.Happened(owner, meek, model, open.Sector, onTheWay, "", clock, lost: true);
        Check("a ship lost on the way does", Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock + hour));
        double after = clock + (Reactions.FearHours(bold) + 1) * hour;
        Check("the aggressive owner goes back first", !Reactions.Avoids(owner, bold, legs, model.DangerMeters, after) && Reactions.Avoids(owner, meek, legs, model.DangerMeters, after));
        Check("and in time so does the other", !Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock + (Reactions.FearHours(meek) + 1) * hour));

        Check("the more it needs what's that way, the sooner it goes", Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock + 4 * hour, 0.1) && !Reactions.Avoids(owner, meek, legs, model.DangerMeters, clock + 4 * hour, 1));

        var hard = new OrganisationRecord { Id = "hard", Credits = 1_000_000 };
        var soft = new OrganisationRecord { Id = "soft", Credits = 1_000_000 };
        Reactions.Happened(hard, bold, model, open.Sector, onTheWay, "villain", clock, lost: true);
        Reactions.Happened(soft, meek, model, open.Sector, onTheWay, "villain", clock, lost: true);
        Check($"the aggressive owner offers more ({hard.Bounties["villain"]} against {soft.Bounties["villain"]})", hard.Bounties["villain"] > soft.Bounties["villain"] && soft.Bounties["villain"] > 0);
        var poor = new OrganisationRecord { Id = "poor", Credits = 100 };
        Reactions.Happened(poor, bold, model, open.Sector, onTheWay, "villain", clock, lost: true);
        Check("nobody offers more than they have", poor.Bounties["villain"] == 100);

        var state = new UniverseState();
        state.Organisations["hard"] = hard;
        state.Organisations["poor"] = poor;
        long offered = hard.Bounties["villain"] + 100;
        Check("the offers add up", Reactions.Hunted(state, "villain") == offered);
        poor.Credits = 40;
        var hunter = new PilotRecord { Credits = 0 };
        long paid = Reactions.Collect(state, "villain", hunter);
        Check($"the hunter is paid by the owners, as far as they can ({paid})", paid == offered - 60 && hunter.Credits == paid && poor.Credits == 0);
        Check("and the head is clean", Reactions.Hunted(state, "villain") == 0);
    }

    // A yard short of what an order needs sends for it, though it has more than it would keep for its own use
    private void YardFetches()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        var state = new UniverseState { Clock = 1 };
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        CargoShips.Muster(state, book, Account);
        CargoShips.Muster(state, book, Account);
        StationBook.Station yard = book.Find("DRYDOCK");
        foreach (MarketGood line in yard.Goods)
            state.Station(yard.Id).Stock[line.Good.Id] = line.Stock;
        state.Station(yard.Id).Stock["electronics"] = 30;
        foreach (CargoShipRecord other in state.CargoShips.Values)
            other.NextLook = 1e12;
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == yard.Id);
        ship.NextLook = 0;
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check("with no order it has enough electronics", ship.Phase == CargoShipRecord.Parked);
        state.ShipOrders["TEST"] = new ShipOrderRecord { Id = "TEST", Model = model.ResourcePath, Owner = ship.Owner, Home = yard.Id, Yard = yard.Id };
        ship.NextLook = 0;
        state.Clock += 5;
        CargoShips.Tick(state, book, Account);
        Check($"an order waiting for 40 sends a ship for them ({ship.Good} from {ship.Going})", ship.Phase == CargoShipRecord.Flying && ship.Good == "electronics");
    }

    private async Task InGame()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        ShipBody me = main.Ship;
        var radio = me.Find<RadioDevice>();
        CargoView view = main.GetNode<CargoView>("CargoView");
        Check("the software runs", await Until(() => view.Shown.Count > 0 && radio.Contacts.Count > 1, 60));
        UniverseState state = Universe.Current.State;
        (string id, ShipBody ship) = view.Shown.First(kv => state.CargoShips[kv.Key].Home == "ALPHA");
        CargoShipRecord record = state.CargoShips[id];
        Universe.Current.Fly(me, Universe.Current.LocalPilotId);
        var owner = (OrganisationRecord)Universe.Current.Account(record.Owner);

        Combat.Struck(ship, me);
        Check("a ship under fire calls for help", await Until(() => radio.Heard.Any(m => m.Text.StartsWith("Mayday")), 3));
        Check("its owner hears of it", owner.Incidents.Count == 1 && !owner.Incidents[0].Lost);
        Check($"and offers something for the attacker ({owner.Bounties.GetValueOrDefault(Universe.Current.LocalPilotId)})", owner.Bounties.GetValueOrDefault(Universe.Current.LocalPilotId) > 0);
        Combat.Struck(ship, me);
        await Seconds(0.5);
        Check("it doesn't tell again at every hit", owner.Incidents.Count == 1);

        ShipHull hull = ship.Find<ShipHull>();
        ship.Wake();
        for (int i = 0; i < 40 && !hull.Wrecked; i++)
        {
            Combat.Strike(ship.Find<ShipSignature>(), 500, me, ship.GlobalPosition);
            await Seconds(0.1);
        }
        Check("destroyed", hull.Wrecked);
        Check("its owner marks the loss", await Until(() => owner.Incidents.Any(i => i.Lost), 3));
        Check($"and the record is gone", !state.CargoShips.ContainsKey(id));
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
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
