using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// A trade run from end to end in the game's main scene (the sign of milestone M5): docked at the first sector's
/// station the pilot buys what it makes, gets clearance to launch, pays the gate's toll, goes through to the second
/// sector with the cargo aboard and weighing on the ship, docks at Kestrel and sells. The money at the end is what it
/// was, less what was paid and the toll, plus what was earned, and in a new universe, with a new pilot's money, the run pays.
/// The flying between is skipped (the ship is placed): how long it takes and what it pays is trade_balance_test's.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/trade_run_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class TradeRunTest : Node
{
    private const string Cargo = "metals";

    private int _failures;
    private SectorHost _main;
    private ShipBody _ship;
    private RadioDevice _radio;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Forget();
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(_main);
        _ship = _main.Ship;
        _radio = _ship.GetNode<RadioDevice>("Radio");
        var hold = _ship.GetNode<CargoHold>("Hold");
        PilotRecord me = universe.Me;
        long start = me.Credits;
        float empty = _ship.Mass;
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));

        var station = _main.Sector.GetNode<Node3D>("Station");
        var market = station.GetNode<StationMarket>("Market");

        if (!await Dock(station, market))
        {
            Finish();
            return;
        }
        // As many as the money allows, keeping the gate's toll
        long keep = _main.Sector.GetNode<JumpGate>("Gate2").Radio.Laws.Toll;
        MarketView bought = market.Serve(me.Id, new MarketRequest { Station = market.Radio.Sign });
        while (bought.Carried < hold.Capacity && bought.Lines.Find(l => l.Good == Cargo).Buy is var price and >= 0 && me.Credits - price >= keep)
            bought = market.Serve(me.Id, new MarketRequest { Station = market.Radio.Sign, Do = MarketRequest.Buy, Good = Cargo, Tonnes = 1 });
        int tonnes = me.Ship.Holds(Cargo);
        long paid = start - me.Credits;
        await Seconds(0.1);
        Check($"bought {tonnes} t of {Cargo} for {paid} ({bought.Message})", tonnes > 0 && paid > 0);
        Check($"the hold has them, and they weigh ({_ship.Mass - empty:0} kg)",
              hold.Contents.Holds(Cargo) == tonnes && Mathf.IsEqualApprox(_ship.Mass - empty, tonnes * CargoHold.KilogramsPerTonne));

        _radio.AskLaunch();
        Check("cleared to launch, the pad lets go", await Until(() => _ship.Holder == null, 30));

        JumpGate gate = _main.Sector.GetNode<JumpGate>("Gate2");
        PlaceBefore(gate, 600, 0);
        await Seconds(1.5);
        long beforeToll = me.Credits;
        _radio.AskTransit();
        Check("the gate clears the ship", await Until(() => gate.Cleared(_ship), 3));
        long toll = beforeToll - me.Credits;
        Check($"and takes its toll ({toll})", toll == gate.Radio.Laws.Toll && toll > 0);
        PlaceBefore(gate, 30, 100);
        Check("through the ring, the ship is in the second sector", await Until(() => _main.Here.Id == "sector_02", 2));
        Check("out of the tunnel", await Until(() => _ship.Holder == null, 10));
        Check($"the cargo came along ({hold.Contents.Holds(Cargo)} t), and still weighs",
              hold.Contents.Holds(Cargo) == tonnes && Mathf.IsEqualApprox(_ship.Mass - empty, tonnes * CargoHold.KilogramsPerTonne));

        var kestrel = _main.Sector.GetNode<Node3D>("Kestrel");
        var there = kestrel.GetNode<StationMarket>("Market");
        if (!await Dock(kestrel, there))
        {
            Finish();
            return;
        }
        long beforeSale = me.Credits;
        MarketView sold = there.Serve(me.Id, new MarketRequest { Station = there.Radio.Sign, Do = MarketRequest.Sell, Good = Cargo, Tonnes = tonnes });
        long earned = me.Credits - beforeSale;
        await Seconds(0.1);
        Check($"sold it all at {there.Radio.Sign} for {earned} ({sold.Message})", me.Ship.Holds(Cargo) == 0 && earned > 0);
        Check("the ship weighs what it did", Mathf.IsEqualApprox(_ship.Mass, empty));
        Check($"the money adds up ({start} - {paid} - {toll} + {earned} = {me.Credits})", me.Credits == start - paid - toll + earned);
        Check($"and the run paid ({me.Credits - start:+0;-0})", me.Credits > start);

        Finish();
    }

    // Cleared by the station, the ship placed just over its pad: the pad takes it
    private async Task<bool> Dock(Node3D station, StationMarket market)
    {
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 300)));
        await Seconds(1.5);
        _radio.AskDocking();
        bool cleared = await Until(() => _radio.Dock == dock.Number && _radio.Pad != 0, 5);
        Check($"cleared to a pad at {market.Radio.Sign}", cleared);
        if (!cleared)
            return false;
        LandingPad pad = dock.PadNumbered(_radio.Pad);
        Place(pad.GlobalTransform * pad.Rest(_ship).TranslatedLocal(new Vector3(0, 1, 0)));
        bool docked = await Until(() => market.Radio.DockedHere(_ship), (float)pad.SettleSeconds + 3);
        Check($"docked at {market.Radio.Sign}", docked);
        return docked;
    }

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

    // On the gate's axis, so far out on its +Z side, going in at that speed
    private void PlaceBefore(JumpGate gate, float along, float speed)
    {
        Basis b = gate.GlobalBasis.Orthonormalized();
        _ship.State = new ShipState
        {
            Position = gate.ToGlobal(new Vector3(0, 0, along)),
            Orientation = b.GetRotationQuaternion(),
            Velocity = b * new Vector3(0, 0, -speed),
        };
    }

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Seconds(double s) => await Until(() => false, s);

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
