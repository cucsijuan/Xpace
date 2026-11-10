using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Stolen goods in a pilot's hold, by the same rules free people go by:
///
///   - the hold keeps its stolen tonnes apart, a part of the cargo; taking tonnes away takes the pilot's own first;
///   - a pod knows whose it was: taking back one's own is no theft, anyone else's (or what was stolen already) is;
///   - a hold spilled into space leaves a pod for its own tonnes and another for its stolen ones of each good;
///   - a lawful market buys the pilot's own tonnes and no stolen ones; a fence buys them too, for its share of the price;
///   - a ship released from the pound gets back what the courtesy ship carried, stolen or not as it was.
///
/// Run: Godot --headless --path . res://tests/stolen_cargo_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class StolenCargoTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        Hold();
        Pods();
        Selling();
        Release();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Hold()
    {
        var ship = new ShipRecord();
        ship.Load("food", 3);
        ship.Steal("food", 2);
        Check($"the hold keeps stolen tonnes apart ({ship.Holds("food")} t, {ship.StolenOf("food")} stolen)",
              ship.Holds("food") == 5 && ship.StolenOf("food") == 2 && ship.Clean("food") == 3 && ship.Tonnes == 5);
        ship.Load("food", -4);
        Check($"taking tonnes away takes the pilot's own first ({ship.Holds("food")} t, {ship.StolenOf("food")} stolen)",
              ship.Holds("food") == 1 && ship.StolenOf("food") == 1 && ship.Clean("food") == 0);
        ship.Empty();
        Check("an emptied hold has nothing, stolen or not", ship.Cargo.Count == 0 && ship.Stolen.Count == 0);
    }

    private void Pods()
    {
        var own = new CargoPod { Owner = "ann" };
        var hot = new CargoPod { Owner = "ann", Stolen = true };
        var nobodys = new CargoPod();
        Check("a pod is stolen goods to anyone but its owner", !own.StolenBy("ann") && own.StolenBy("bob"));
        Check("stolen goods stay stolen, even for who stole them", hot.StolenBy("ann"));
        Check("a pod of nobody known is stolen goods to anyone", nobodys.StolenBy("ann") && nobodys.StolenBy(""));
        own.Free();
        hot.Free();
        nobodys.Free();

        var origin = new FloatingOrigin();
        var ship = new Node3D();
        origin.AddChild(ship);
        AddChild(origin);
        CargoPod.Spill(ship, Vector3.Zero, new Dictionary<string, int> { ["food"] = 5, ["iron"] = 2 }, "ann", new Dictionary<string, int> { ["food"] = 2 });
        List<CargoPod> pods = origin.GetChildren().OfType<CargoPod>().ToList();
        Check($"a spilled hold leaves its own and its stolen tonnes apart ({string.Join(", ", pods.Select(p => $"{p.Tonnes} t {p.Good}{(p.Stolen ? " stolen" : "")}"))})",
              pods.Count == 3 && pods.All(p => p.Owner == "ann") &&
              pods.Any(p => p.Good == "food" && p.Tonnes == 3 && !p.Stolen) && pods.Any(p => p.Good == "food" && p.Tonnes == 2 && p.Stolen) &&
              pods.Any(p => p.Good == "iron" && p.Tonnes == 2 && !p.Stolen));
        origin.QueueFree();
    }

    private void Selling()
    {
        var line = new MarketGood { Good = GoodsCatalog.Find("food"), Stock = 100 };
        var station = new StationRecord { Id = "TEST" };
        station.Stock["food"] = 100;
        var pilot = new PilotRecord { Id = "ann", Credits = 0 };
        pilot.Ship.Load("food", 1);
        pilot.Ship.Steal("food", 2);
        long price = Market.SellPrice(station, line);
        Market.Result result = Market.Sell(station, line, pilot, 3, out int sold, out long earned);
        Check($"a lawful market buys the pilot's own tonne and no stolen ones ({sold} t for {earned}, {result})",
              result == Market.Result.Stolen && sold == 1 && earned == price && pilot.Ship.Holds("food") == 2 && pilot.Ship.StolenOf("food") == 2);

        long next = Market.SellPrice(station, line);
        var owner = new OrganisationRecord { Id = "fence", Credits = 1_000_000 };
        result = Market.Sell(station, line, pilot, 1, out sold, out earned, owner, 0.6);
        Check($"a fence buys stolen tonnes for its share of the price ({earned} of {next})",
              result == Market.Result.Done && sold == 1 && earned == Market.Fenced(next, 0.6) && earned < next &&
              owner.Credits == 1_000_000 - earned && pilot.Ship.StolenOf("food") == 1);
    }

    private void Release()
    {
        var station = new StationRecord { Id = "TEST" };
        var pilot = new PilotRecord { Id = "ann" };
        var own = new ShipRecord();
        station.Impounded["ann"] = own;
        pilot.Ship = new ShipRecord { Hull = ShipRecord.CourtesyHull };
        pilot.Ship.Load("iron", 1);
        pilot.Ship.Steal("iron", 2);
        bool released = Justice.Release(station, new CrimeRecord(), pilot);
        Check($"a released ship gets the courtesy ship's cargo as it was ({pilot.Ship.Holds("iron")} t, {pilot.Ship.StolenOf("iron")} stolen)",
              released && pilot.Ship == own && pilot.Ship.Holds("iron") == 3 && pilot.Ship.StolenOf("iron") == 2);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
