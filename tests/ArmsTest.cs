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
        var radio = _ship.Find<RadioDevice>();
        var launcher = _ship.Find<LauncherDevice>();
        var dispenser = _ship.Device<DispenserDevice>();
        var turret = _ship.Device<TurretDevice>();
        var station = main.Sector.GetNode<Node3D>("MiltecAlpha");
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
        int magazine = turret.Magazine;
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
        Check("the arms kiosk opens its own window", window?.View != null && window.View.Offers.Count == 8);
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
        StationRecord record = universe.State.Station(market.Radio.Id);
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
        MarketView unloaded = market.Serve(me.Id, new MarketRequest { Station = market.Radio.Id, Do = MarketRequest.Sell, Good = "iron", Tonnes = 10 });
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

        await Fitting(kiosk, walker, market);
        Finish();
    }

    // Lasers and turrets are bought into the locker and fitted on the Fitting tab
    private async Task Fitting(StationKiosk kiosk, Walker walker, StationMarket market)
    {
        PilotRecord me = Universe.Current.Me;
        kiosk.Use(walker);
        await Seconds(0.3);
        if (kiosk.Window is not ArmsWindow window || window.Fit == null)
        {
            Check("the fitting tab answers", false);
            return;
        }
        window.ShowTab(1);
        var pilotShip = _ship.GetNode<PilotShip>("Fit");
        FittingPlace Place(string name) => window.Fit.Places.First(p => p.Name == name);
        Check($"it shows the hardpoints and what is on them ({string.Join(", ", window.Fit.Places.Select(p => $"{p.Name} {p.Model}"))})",
              window.Fit.Places.Count == 5 && Place("NoseLeft").Model == "LSR-1" && Place("WingLeft").Model == "PYL-1" && Place("Spine").Model == "GUN-1");
        Check("and the ship on its stand, with the weapons", window.Ship != null && window.Ship.FindChild("Laser", true, false) != null);

        ArmsOffer lsr = window.View.Offers.First(o => o.Id == "lsr_1");
        Check($"lasers are sold too, as pieces ({lsr.Fitted} fitted, {lsr.Kept} kept, {lsr.Price} cr; {lsr.Details})", lsr.Piece && lsr.Fitted == 2 && lsr.Kept == 0 && lsr.Price > 0);
        long before = me.Credits;
        window.Ask(MarketRequest.Buy, "lsr_1", 1);
        await Seconds(0.3);
        Check($"a laser bought waits in the locker ({window.View.Message})",
              window.Fit.Locker.Count(p => p.Name == "LSR-1") == 1 && me.Credits == before - lsr.Price && window.View.Offers.First(o => o.Id == "lsr_1").Kept == 1);

        window.AskFitting(FittingRequest.Remove, "WingLeft", 0);
        await Seconds(0.3);
        Check($"a pylon whose missiles the other rails can't take stays on ({Universe.MissilesAboard(me)} aboard; {window.Fit.Message})",
              Place("WingLeft").Model == "PYL-1" && window.Fit.Message.StartsWith("The ship has 8 missiles aboard") && _ship.Device<LauncherDevice>().Rails.Count == 8);

        int lasers = _ship.Device<LaserDevice>().Emitters;
        window.AskFitting(FittingRequest.Remove, "Spine", 0);
        await Seconds(0.3);
        Check($"the turret taken off goes to the locker, and the ship has none ({window.Fit.Message})",
              Place("Spine").Mounted == "" && window.Fit.Locker.Any(p => p.Name == "GUN-1") && _ship.Device<TurretDevice>() == null && pilotShip.Refits == 1);
        int puffs = window.Puffs;
        FittingPiece laser = window.Fit.Locker.First(p => p.Name == "LSR-1");
        Shot("_fitting");
        window.AskFitting(FittingRequest.Fit, "Spine", laser.Index);
        await Seconds(0.1);
        Shot("_puff");
        await Seconds(0.2);
        Check($"a laser fitted on the spine: three lasers ({lasers} -> {_ship.Device<LaserDevice>().Emitters}; {window.Fit.Message})",
              Place("Spine").Model == "LSR-1" && _ship.Device<LaserDevice>().Emitters == lasers + 1 && pilotShip.Refits == 2);
        Check($"it appears on the stand in a puff of dust ({window.Puffs - puffs})",
              window.Puffs == puffs + 1 && window.Ship.GetNode("Spine").GetNodeOrNull("Laser") != null);

        await Seconds(1);
        Shot("_three");
        FittingPiece gun = window.Fit.Locker.First(p => p.Name == "GUN-1");
        Check($"the turret fits only the spine ({string.Join(", ", gun.FitsOn)})", gun.FitsOn.SequenceEqual(new[] { "Spine" }));
        window.AskFitting(FittingRequest.Fit, "NoseLeft", gun.Index);
        await Seconds(0.3);
        Check($"not on the nose ({window.Fit.Message})", window.Fit.Message == market.NotFitText && Place("NoseLeft").Model == "LSR-1");
        window.AskFitting(FittingRequest.Fit, "Spine", gun.Index);
        await Seconds(0.3);
        Check($"fitted on the spine in the laser's place, the laser back in the locker ({window.Fit.Message})",
              Place("Spine").Model == "GUN-1" && window.Fit.Locker.Any(p => p.Name == "LSR-1") && _ship.Device<TurretDevice>() != null
              && _ship.Device<LaserDevice>().Emitters == lasers);
        ArmsOffer spare = window.View.Offers.First(o => o.Id == "lsr_1");
        StationRecord here = Universe.Current.State.Station(market.Radio.Id);
        double stock = here.Stock["lsr_1"];
        long credits = me.Credits;
        window.Ask(MarketRequest.Sell, "lsr_1", 1);
        await Seconds(0.3);
        Check($"the dealer buys the spare laser back from the locker ({spare.SellPrice} cr, under its price {spare.Price}; stock {stock:0.00} -> {here.Stock["lsr_1"]:0.00}, credits +{me.Credits - credits}, kept {spare.Kept}; {window.View.Message})",
              spare.Kept == 1 && spare.SellPrice > 0 && spare.SellPrice < spare.Price && me.Credits == credits + spare.SellPrice
              && Math.Abs(here.Stock["lsr_1"] - (stock + 1)) < 0.05 && !window.Fit.Locker.Any(p => p.Name == "LSR-1"));
        window.Ask(MarketRequest.Sell, "lsr_1", 1);
        await Seconds(0.3);
        Check($"none left to sell ({window.View.Message})", window.View.Message == market.NoPieceText && me.Credits == credits + spare.SellPrice);
        Check("the restarted computer runs its programs again", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        window.Close();
    }

    // The factory, on its own
    private void Rules()
    {
        var iron = GD.Load<Good>("res://assets/goods/iron.tres");
        var goods = new[] { new MarketGood { Good = iron, Stock = 10 } };
        var line = new OutfitLine { Item = GD.Load<MissileSpec>("res://assets/weapons/missiles/msl_1.tres"), Stock = 10 };
        var recipe = new Recipe { Output = line.Item, Inputs = new[] { new RecipePart { Item = iron, Amount = 0.5f } } };
        var factories = new[] { new FactoryLine { Recipe = recipe, PerHour = 4 } };
        var outfits = new[] { line };
        var station = new StationRecord();
        Market.Advance(station, goods, 0, outfits, factories);
        Check($"a dealer starts with the stock it wants ({station.Stock[line.Id]})", station.Stock[line.Id] == 10);
        Market.Advance(station, goods, 3600, outfits, factories);
        Check($"with all it wants the factory rests, using no iron ({station.Stock["iron"]} t)", station.Stock["iron"] == 10);
        station.Stock[line.Id] = 2;
        Market.Advance(station, goods, 3600 * 2, outfits, factories);
        Check($"short of stock, it makes what its rate allows, using iron ({station.Stock[line.Id]}, {station.Stock["iron"]} t)",
              Math.Abs(station.Stock[line.Id] - 6) < 1e-9 && Math.Abs(station.Stock["iron"] - 8) < 1e-9);
        station.Stock[line.Id] = 0;
        station.Stock["iron"] = 1;
        Market.Advance(station, goods, 3600 * 3, outfits, factories);
        Check($"it makes only what the iron lasts for ({station.Stock[line.Id]}, {station.Stock["iron"]} t)",
              Math.Abs(station.Stock[line.Id] - 2) < 1e-9 && station.Stock["iron"] < 1e-9);
        Check("the last ones cost more than the first", Market.OutfitPrice(line, 1) > Market.OutfitPrice(line, 10) && Market.OutfitPrice(line, 0) < 0);
    }

    private static MarketRequest Buy(StationMarket market, string model, int count) =>
        new() { Station = market.Radio.Id, Do = MarketRequest.Buy, Good = model, Tonnes = count };

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

    private void Shot(string suffix = "")
    {
        if (OS.GetEnvironment("XPACE_ARMS_SHOT") is { Length: > 0 } file && DisplayServer.GetName() != "headless")
            GetViewport().GetTexture().GetImage().SavePng(suffix == "" ? file : file.GetBaseName() + suffix + ".png");
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
