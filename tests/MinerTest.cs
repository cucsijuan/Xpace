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
/// Kestrel's miners, in the real sector: a spawner brings them in, each flies to a rock of the belt, stops off it and
/// works it with its ordinary laser, and with its hold full takes the ore to Kestrel, whose market's stock goes up.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/miner_test.tscn. Exits with 0 if every check passed.
/// </summary>
public partial class MinerTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var sector = GD.Load<PackedScene>("res://scenes/sectors/sector_02.tscn").Instantiate<Node3D>();
        AddChild(sector);
        var spawner = sector.GetNode<ShipSpawner>("Miners");
        Check("a miner comes in", await Until(() => spawner.Ships.Count > 0, 20));
        if (spawner.Ships.Count == 0)
        {
            Finish();
            return;
        }
        var ship = (ShipBody)spawner.Ships[0];
        MinerJob job = ship.Find<MinerJob>();
        var pilot = ship.GetNode<FighterAi>("Pilot");
        job.TonnesPerCycle = 5;
        job.CycleSeconds = 3;
        Check($"it carries lasers and no missiles, in its own colours ({ship.Device<LaserDevice>().Emitters} lasers, {ship.Device<LauncherDevice>().MissilesLeft} missiles)",
              ship.Device<LaserDevice>().Emitters == 2 && ship.Device<LauncherDevice>().MissilesLeft == 0 && ship.Device<TurretDevice>() == null);
        Check("the computer boots", await Until(() => ship.Computer.ScreenText().Length > 0, 90));

        Check($"it picks a rock of the belt and flies there ({pilot.Doing})", await Until(() => job.Rock != null, 30));
        Check($"it reaches the rock and starts cutting ({job.Doing}, {pilot.Doing})", await Until(() => job.Doing == "cutting" && pilot.Route.Count == 0, 240));
        int before = ship.Device<LaserDevice>().PulsesFired;
        Check($"cutting, the hold fills ({job.Hold} t, {ship.Device<LaserDevice>().PulsesFired - before} pulses)", await Until(() => job.Hold > 0, 60) && ship.Device<LaserDevice>().PulsesFired > before);
        Check($"with the hold full it heads home ({job.Doing})", await Until(() => job.Doing == "taking ore home", 90));

        StationRecord kestrel = Universe.Current.State.Station("KESTREL");
        var market = sector.GetNode("Kestrel").GetNode<StationMarket>("Market");
        double stock = kestrel.Stock.GetValueOrDefault("iron_ore");
        Check($"it hands the ore over at the station ({job.Delivered} t)", await Until(() => job.Delivered > 0, 300));
        Check($"and the market has it ({stock:0} -> {kestrel.Stock.GetValueOrDefault("iron_ore"):0} t)", kestrel.Stock.GetValueOrDefault("iron_ore") > stock && job.Hold == 0);
        Check($"then it goes back to work ({job.Doing})", job.Doing == "flying to a rock");
        Check("it is still whole", !ship.Wrecked);
        Finish();
    }

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
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
