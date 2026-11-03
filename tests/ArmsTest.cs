using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// The first chain: Kestrel's iron, carried to the station, is what the station's factory makes missiles of, and a
/// pilot who fired theirs buys more; chaff, flares and the cannon's rounds, made of the station's metals, are bought
/// the same way. In the game's main scene:
///
///   - the rules: the factory makes what the dealer lacks, using iron up, and stops with none;
///   - a new ship comes loaded; a heat seeker fired leaves one fewer, in the pilot's record, and docking doesn't bring it back;
///     the same for chaff and flares thrown and cannon rounds fired;
///   - at the arms kiosk, its own window, apart from the market: buying loads the ship and takes the money, and
///     the dealer has one fewer; no more than the rack takes or the money pays for; only docked; chaff and flares
///     are sold there too, filling the dispenser, and rounds by the box, filling the turret's magazine;
///   - with the iron gone and the stock sold there are no more to buy, until a hold of iron sold to the market lets
///     the factory start again;
///   - the ship blown up, the insurer's new ship comes loaded again; a station with no factory sells none.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/arms_test.tscn (XPACE_ARMS_SHOT=file.png, in a window,
/// saves the window's picture). Exits with 0 if every check passed.
/// </summary>
public partial class ArmsTest : Node
{
    private const string Heat = "ir_1", Radar = "msl_1", Chaff = "chaff_1", Flare = "flare_1", Ammo = "rounds_1";

    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        Rules();

        Universe universe = Universe.Current;
        universe.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        var radio = _ship.GetNode<RadioDevice>("Radio");
        var launcher = _ship.GetNode<LauncherDevice>("Launcher");
        var dispenser = _ship.Device<DispenserDevice>();
        var turret = _ship.Device<TurretDevice>();
        var station = main.Sector.GetNode<Node3D>("Station");
        var market = station.GetNode<StationMarket>("Market");
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        var kiosk = dock.GetParent().GetNode<StationKiosk>("ArmsKiosk");
        PilotRecord me = universe.Me;
        me.Credits = 100_000;
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        int rack = _ship.Spec.MissileRack;
        Check($"a new ship comes loaded ({launcher.MissilesLeft} radar, {launcher.HeatSeekersLeft} heat)", launcher.MissilesLeft == rack && launcher.HeatSeekersLeft == rack);
        int chaffRack = _ship.Spec.Dispenser.Chaff, flareRack = _ship.Spec.Dispenser.Flares;
        Check($"its dispenser is the hull's, full ({dispenser.ChaffLeft}/{chaffRack} chaff, {dispenser.FlaresLeft}/{flareRack} flares)",
              chaffRack > 0 && flareRack > 0 && dispenser.ChaffLeft == chaffRack && dispenser.FlaresLeft == flareRack);
        int magazine = _ship.Spec.Turret.Rounds;
        Check($"and its turret's magazine, full ({turret.RoundsLeft}/{magazine})", magazine > 0 && turret.RoundsLeft == magazine);

        ArmsView early = market.ServeArms(me.Id, Buy(market, Heat, 1));
        Check($"not docked, nothing is sold ({early.Message})", early.Message == market.NotDockedText);

        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 400)));
        await Seconds(1);
        _ship.Computer.Bus.Out(LauncherDevice.Slot, 6, 2);
        await Seconds(1.2);
        _ship.Computer.Bus.Out(LauncherDevice.Slot, 6, 2);
        await Seconds(0.2);
        Check($"two heat seekers fired, two fewer, in the pilot's record ({launcher.HeatSeekersLeft})",
              launcher.Launched == 2 && launcher.HeatSeekersLeft == rack - 2 && me.Ship.Rack(Heat, rack) == rack - 2);
        _ship.Computer.Bus.Out(DispenserDevice.Slot, 4, 3);
        dispenser.AfterStep(_ship, 0);
        Check($"a bundle of chaff and a flare thrown, one fewer of each, in the record ({dispenser.ChaffLeft}, {dispenser.FlaresLeft})",
              dispenser.ChaffLeft == chaffRack - 1 && dispenser.FlaresLeft == flareRack - 1 && me.Ship.Rack(Chaff, chaffRack) == chaffRack - 1 && me.Ship.Rack(Flare, flareRack) == flareRack - 1);
        _ship.Computer.Bus.Out(TurretDevice.Slot, 12, 1);
        turret.AfterStep(_ship, 1.0 / 60);
        Check($"a round fired, one fewer in the record ({turret.RoundsLeft})", turret.RoundsFired == 1 && turret.RoundsLeft == magazine - 1 && me.Ship.Rack(Ammo, magazine) == magazine - 1);

        radio.AskDocking();
        await Until(() => radio.Dock == dock.Number && radio.Pad != 0, 5);
        LandingPad pad = dock.PadNumbered(radio.Pad);
        Place(pad.GlobalTransform * pad.Rest(_ship).TranslatedLocal(new Vector3(0, 1, 0)));
        Check("docked", await Until(() => market.Radio.DockedHere(_ship), (float)pad.SettleSeconds + 3));
        await Seconds(0.2);
        Check($"docking gives no missiles back ({launcher.HeatSeekersLeft}), nor chaff or flares ({dispenser.ChaffLeft}, {dispenser.FlaresLeft})",
              launcher.HeatSeekersLeft == rack - 2 && dispenser.ChaffLeft == chaffRack - 1 && dispenser.FlaresLeft == flareRack - 1);

        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = false });
        Walker walker = _ship.GetNode<ShipSeat>("Seat").Walker;
        if (walker == null)
        {
            Check("out of the seat", false);
            Finish();
            return;
        }
        kiosk.Use(walker);
        await Seconds(0.3);
        var window = kiosk.Window as ArmsWindow;
        Check("the arms kiosk opens its own window", window?.View != null && window.View.Offers.Count == 5);
        if (window == null || window.View == null)
        {
            Finish();
            return;
        }
        ArmsOffer offer = window.View.Offers.First(o => o.Id == Heat);
        Check($"it shows what the ship has aboard and the dealer's stock ({offer.Aboard}/{offer.Rack}, {offer.Stock} at {offer.Price})",
              offer.Aboard == rack - 2 && offer.Rack == rack && offer.Stock > 0 && offer.Price > 0);
        long before = me.Credits;
        window.Ask(MarketRequest.Buy, Heat, 1);
        await Seconds(0.2);
        ArmsOffer after = window.View.Offers.First(o => o.Id == Heat);
        Check($"buying one loads it and takes the money ({window.View.Message})",
              launcher.HeatSeekersLeft == rack - 1 && me.Credits == before - offer.Price && after.Stock == offer.Stock - 1);
        window.Ask(MarketRequest.Buy, Heat, 50);
        await Seconds(0.2);
        Check($"no more than the rack takes ({launcher.HeatSeekersLeft}: {window.View.Message})", launcher.HeatSeekersLeft == rack && window.View.Message.StartsWith("Loaded 1"));
        window.Ask(MarketRequest.Buy, Heat, 1);
        await Seconds(0.2);
        Check($"a full rack takes none ({window.View.Message})", window.View.Message == market.RackFullText);

        ArmsOffer chaff = window.View.Offers.First(o => o.Id == Chaff), flare = window.View.Offers.First(o => o.Id == Flare);
        Check($"chaff and flares are on sale, for the dispenser ({chaff.Aboard}/{chaff.Rack} at {chaff.Price}, {flare.Aboard}/{flare.Rack} at {flare.Price}; {chaff.About})",
              chaff.Rack == chaffRack && chaff.Aboard == chaffRack - 1 && flare.Rack == flareRack && chaff.Price > 0 && flare.Price > 0 && chaff.About.Length > 0);
        before = me.Credits;
        window.Ask(MarketRequest.Buy, Chaff, chaffRack);
        await Seconds(0.2);
        window.Ask(MarketRequest.Buy, Flare, flareRack);
        await Seconds(0.2);
        Check($"filling them loads the dispenser and takes the money ({dispenser.ChaffLeft}, {dispenser.FlaresLeft}, {before - me.Credits} cr; {window.View.Message})",
              dispenser.ChaffLeft == chaffRack && dispenser.FlaresLeft == flareRack && before - me.Credits == chaff.Price + flare.Price);

        me.Ship.Magazine[Ammo] = 0;
        window.Ask(MarketRequest.Look, null, 0);
        await Seconds(0.2);
        ArmsOffer rounds = window.View.Offers.First(o => o.Id == Ammo);
        Check($"rounds are sold by the box ({rounds.Lot} at {rounds.Price} each, {rounds.Aboard}/{rounds.Rack} aboard)",
              rounds.Lot > 1 && rounds.Rack == magazine && rounds.Aboard == 0 && rounds.Price > 0 && turret.RoundsLeft == 0);
        before = me.Credits;
        window.Ask(MarketRequest.Buy, Ammo, rounds.Lot);
        await Seconds(0.2);
        Check($"a box fills the magazine that much ({turret.RoundsLeft}, {before - me.Credits} cr; {window.View.Message})",
              turret.RoundsLeft == rounds.Lot && before - me.Credits >= rounds.Price * rounds.Lot);
        window.Ask(MarketRequest.Buy, Ammo, magazine);
        await Seconds(0.2);
        Check($"filling it, up to what it holds ({turret.RoundsLeft})", turret.RoundsLeft == magazine);
        Shot();

        me.Ship.Magazine[Radar] = 0;
        me.Credits = 1;
        window.Ask(MarketRequest.Buy, Radar, 1);
        await Seconds(0.2);
        Check($"no more than the money pays for ({window.View.Message})", window.View.Message == market.NoMoneyText && launcher.MissilesLeft == 0);
        me.Credits = 1_000_000;

        // The dealer sold out, and the station out of iron: nothing until iron comes
        StationRecord record = universe.State.Station(market.Radio.Sign);
        record.Stock[Radar] = 0;
        record.Stock["iron"] = 0;
        universe.State.Clock += 3600;
        window.Ask(MarketRequest.Look, null, 0);
        await Seconds(0.2);
        ArmsOffer sold = window.View.Offers.First(o => o.Id == Radar);
        Check($"no iron, no missiles: the factory stops and it says so ({sold.Stock}, starved {sold.Starved})", sold.Stock == 0 && sold.Starved && sold.Price < 0);
        window.Ask(MarketRequest.Buy, Radar, 1);
        await Seconds(0.2);
        Check($"none to buy ({window.View.Message})", window.View.Message == market.NoStockText && launcher.MissilesLeft == 0);

        me.Ship.Load("iron", 10);
        MarketView unloaded = market.Serve(me.Id, new MarketRequest { Station = market.Radio.Sign, Do = MarketRequest.Sell, Good = "iron", Tonnes = 10 });
        Check($"a hold of iron sold to the market ({unloaded.Message})", me.Ship.Holds("iron") == 0 && record.Stock["iron"] >= 10);
        universe.State.Clock += 3600;
        window.Ask(MarketRequest.Buy, Radar, 1);
        await Seconds(0.2);
        Check($"an hour on the factory has made more of it, using the iron ({record.Stock[Radar]:0.0} in stock, {record.Stock["iron"]:0.0} t of iron; {window.View.Message})",
              launcher.MissilesLeft == 1 && record.Stock[Radar] > 0 && record.Stock["iron"] < 10);
        window.Close();
        await Seconds(0.1);

        me.Ship.Magazine[Ammo] = 3;
        universe.Lose(_ship);
        launcher.Rearm();
        await Seconds(0.1);
        Check($"the insurer's new ship comes loaded ({launcher.MissilesLeft}, {launcher.HeatSeekersLeft}; {dispenser.ChaffLeft}, {dispenser.FlaresLeft})",
              launcher.MissilesLeft == rack && launcher.HeatSeekersLeft == rack && dispenser.ChaffLeft == chaffRack && dispenser.FlaresLeft == flareRack
              && turret.RoundsLeft == magazine);

        Finish();
    }

    // The factory, on its own
    private void Rules()
    {
        var iron = GD.Load<Good>("res://assets/goods/iron.tres");
        var goods = new[] { new MarketGood { Good = iron, Stock = 10, Rate = 0 } };
        var line = new OutfitLine { Item = GD.Load<MissileSpec>("res://assets/weapons/missiles/msl_1.tres"), Stock = 10, PerHour = 4, Input = iron, InputPerUnit = 0.5f };
        var outfits = new[] { line };
        var station = new StationRecord();
        Market.Advance(station, goods, 0, outfits);
        Check($"a dealer starts with the stock it wants ({station.Stock[line.Id]})", station.Stock[line.Id] == 10);
        Market.Advance(station, goods, 3600, outfits);
        Check($"with all it wants the factory rests, using no iron ({station.Stock["iron"]} t)", station.Stock["iron"] == 10);
        station.Stock[line.Id] = 2;
        Market.Advance(station, goods, 3600 * 2, outfits);
        Check($"short of stock, it makes what its rate allows, using iron ({station.Stock[line.Id]}, {station.Stock["iron"]} t)",
              Math.Abs(station.Stock[line.Id] - 6) < 1e-9 && Math.Abs(station.Stock["iron"] - 8) < 1e-9);
        station.Stock[line.Id] = 0;
        station.Stock["iron"] = 1;
        Market.Advance(station, goods, 3600 * 3, outfits);
        Check($"it makes only what the iron lasts for ({station.Stock[line.Id]}, {station.Stock["iron"]} t)",
              Math.Abs(station.Stock[line.Id] - 2) < 1e-9 && station.Stock["iron"] < 1e-9);
        Check("the last ones cost more than the first", Market.OutfitPrice(line, 1) > Market.OutfitPrice(line, 10) && Market.OutfitPrice(line, 0) < 0);
    }

    private static MarketRequest Buy(StationMarket market, string model, int count) =>
        new() { Station = market.Radio.Sign, Do = MarketRequest.Buy, Good = model, Tonnes = count };

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

    private void Shot()
    {
        if (OS.GetEnvironment("XPACE_ARMS_SHOT") is { Length: > 0 } file && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(file);
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
