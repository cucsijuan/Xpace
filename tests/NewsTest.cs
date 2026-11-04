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
/// What actors know of markets: nobody reads a far station's stock.
///   - a beacon reports its sector, or as many gates away as it says;
///   - a cargo ship with no news goes to look at the nearest maker, and its owner learns what it saw;
///   - it trusts what its owner heard, even when that is wrong, until the news is too old;
///   - what it finds on arrival may not be worth buying;
///   - a cargo ship flying past a beacon tells its owner;
///   - a pilot flies to a beacon, asks on COMMS, and learns that sector only.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/news_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class NewsTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Records();
        await Pilot();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Records()
    {
        StationBook book = StationBook.Read(GD.Load<SectorMap>("res://assets/sectors/map.tres"));
        Check($"the book has the beacons ({string.Join(", ", book.Beacons.Select(b => b.Id))})", book.Beacons.Count == 2);
        StationBook.Beacon first = book.Beacons.First(b => b.Id == "BEACON1");
        Check("a beacon reports its own sector", book.ReportedBy(first).All(s => s.Sector == first.Sector) && book.ReportedBy(first).Any(s => s.Id == "ALPHA"));
        var wide = new StationBook.Beacon { Id = "WIDE", Sector = first.Sector, At = first.At, Reach = 1, Heard = first.Heard };
        Check("and with a reach of one gate, the next as well", book.ReportedBy(wide).Count() == book.Stations.Count);

        var state = new UniverseState();
        var owners = new Dictionary<string, OrganisationRecord>();
        IAccount Account(string id) => CargoShipsTest.Account(state, owners, id);
        foreach (StationBook.Gate gate in book.Gates)
            Account(gate.Owner);
        CargoShips.Muster(state, book, Account);
        CargoShips.Muster(state, book, Account);
        StationBook.Station alpha = book.Find("ALPHA");
        ShipModel model = GD.Load<ShipModel>("res://assets/ship/models/freighter.tres");
        IAccount miltec = Account(alpha.Owner);
        Check("a new owner knows no market", miltec.Known.Count == 0);

        // What Alpha wants and only a station of someone else makes
        CargoShipRecord ship = state.CargoShips.Values.First(s => s.Home == alpha.Id);
        foreach (CargoShipRecord other in state.CargoShips.Values.Where(s => s != ship))
            other.NextLook = 1e12;
        MarketGood wanted = alpha.Goods.Where(g => Market.Net(g.Good.Id, alpha.Goods, alpha.Factories) < 0).First(g => Makers(book, alpha, g.Good.Id).Any());
        foreach (MarketGood line in alpha.Goods.Where(g => Market.Net(g.Good.Id, alpha.Goods, alpha.Factories) < 0))
            state.Station(alpha.Id).Stock[line.Good.Id] = line == wanted ? 0 : line.Stock;
        List<StationBook.Station> makers = Makers(book, alpha, wanted.Good.Id).ToList();
        StationBook.Station nearest = makers.OrderBy(s => book.Seconds(alpha, s, model, 0)).First();

        Tick(state, book, Account, 10);
        Check($"with no news it goes to look at the nearest maker of {wanted.Good.Id} ({ship.Going})", ship.Phase == CargoShipRecord.Flying && ship.Going == nearest.Id);
        Until(state, book, Account, () => ship.State == CargoShipRecord.Returning, 7200);
        Check("and its owner learns that market when it gets there", miltec.Known.ContainsKey(nearest.Id) && miltec.Known[nearest.Id].AsOf > 0);
        Check($"and it found some to bring ({ship.Tonnes} t)", ship.Tonnes > 0);
        Until(state, book, Account, () => ship.Phase == CargoShipRecord.Parked && ship.State == CargoShipRecord.Waiting, 7200);

        // News that says the only maker has none: believed, though it is false
        state.Station(alpha.Id).Stock[wanted.Good.Id] = 0;
        foreach (StationBook.Station maker in makers)
        {
            state.Station(maker.Id).Stock[wanted.Good.Id] = maker.Goods.First(g => g.Good.Id == wanted.Good.Id).Stock * 2;
            miltec.Known[maker.Id] = new StationNews { AsOf = state.Clock, Goods = { [wanted.Good.Id] = new GoodNews { Stock = 0, Buy = -1 } } };
        }
        ship.NextLook = 0;
        Tick(state, book, Account, 10);
        Check("told the makers have none, it stays, whatever they really have", ship.Phase == CargoShipRecord.Parked);
        foreach (StationBook.Station maker in makers)
            miltec.Known[maker.Id].AsOf = state.Clock - model.NewsHours * 3600 - 1;
        ship.NextLook = 0;
        Tick(state, book, Account, 10);
        Check("once the news is old it goes to look again", ship.Phase == CargoShipRecord.Flying);

        // It gets there to find the price gone up past what the good is worth at home
        Until(state, book, Account, () =>
        {
            state.Station(alpha.Id).Stock[wanted.Good.Id] = wanted.Stock * 2.9;
            state.Station(ship.Going).Stock[wanted.Good.Id] = wanted.Stock * 0.3;
            return ship.State == CargoShipRecord.Returning;
        }, 7200);
        Check("too dear when it gets there: it comes back empty", ship.Tonnes == 0);

        StationBook.Station across = book.Stations.First(s => s.Sector != alpha.Sector);
        List<StationBook.Leg> legs = book.Departure(alpha, alpha.Pads[0], across, model, 0, out _);
        Check("the way to the gate passes the beacon", book.BeaconsOn(legs).Any(b => b.Id == "BEACON1"));
        var pilot = new PilotRecord();
        MarketNews.Hear(pilot, first, state, book, Account);
        Check("what a beacon tells is dated", pilot.Known.Values.All(n => n.AsOf == state.Clock) && pilot.Known.Count == book.ReportedBy(first).Count());
        double then = state.Clock;
        pilot.Known["ALPHA"].AsOf = then + 100;
        MarketNews.Learn(pilot, "ALPHA", new StationNews { AsOf = then });
        Check("older news doesn't replace newer", pilot.Known["ALPHA"].AsOf == then + 100);
    }

    private static IEnumerable<StationBook.Station> Makers(StationBook book, StationBook.Station home, string good) =>
        book.Stations.Where(s => s != home && s.Owner != home.Owner && s.Goods.Any(g => g.Good.Id == good) && Market.Net(good, s.Goods, s.Factories) > 0);

    private static void Tick(UniverseState state, StationBook book, Func<string, IAccount> account, double seconds)
    {
        state.Clock += seconds;
        CargoShips.Tick(state, book, account);
    }

    private static void Until(UniverseState state, StationBook book, Func<string, IAccount> account, Func<bool> done, double seconds)
    {
        for (double t = 0; t < seconds && !done(); t += 5)
            Tick(state, book, account, 5);
    }

    private async Task Pilot()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        ShipBody ship = main.Ship;
        var radio = ship.Find<RadioDevice>();
        var display = ship.Find<DisplayDevice>();
        foreach (string name in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(name)?.QueueFree();
        CommercialBeacon beacon = main.Sector.GetNode<CommercialBeacon>("Beacon");
        await Seconds(25);
        foreach (CargoShipRecord cargo in Universe.Current.State.CargoShips.Values)
            cargo.NextLook = 1e12;
        ship.State = new ShipState { Position = beacon.GlobalPosition + new Vector3(0, 1000, 0), Orientation = Quaternion.Identity };
        await Seconds(2);
        PilotRecord me = Universe.Current.Me;
        var radioOfBeacon = beacon.GetNode<StationRadio>("Radio");
        Check("the beacon is on the list as a beacon", radio.Contacts.Contains(radioOfBeacon) && ((IRadio)radioOfBeacon).Kind == 'B');
        for (int i = radio.Contacts.Count; i > 0; i--)     // the cursor stayed on the contact it was on
            await Key(display, DisplayKey.Up);
        for (int i = radio.Contacts.ToList().IndexOf(radioOfBeacon); i > 0; i--)
            await Key(display, DisplayKey.Down);
        await Key(display, DisplayKey.Select);
        await Key(display, DisplayKey.Select);
        await Seconds(1.5);
        Check($"asked on COMMS it sends its data ({me.Known.Count} stations)", me.Known.ContainsKey("ALPHA") && me.Known.ContainsKey("QUARTZ"));
        Check("of its own sector only", !me.Known.ContainsKey("KESTREL") && !me.Known.ContainsKey("DRYDOCK"));
        Check("and says so", radio.Heard.Any(m => m.Text.StartsWith("Market data")));
    }

    private async Task Key(DisplayDevice display, DisplayKey key)
    {
        display.PressKeyOn("COMMS", key);
        await Seconds(0.3);
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
