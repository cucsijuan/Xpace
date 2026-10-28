using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.OnFoot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Trading, in the game's main scene: the player's fighter docks on a pad of the station's first hangar, the pilot
/// gets out and walks to the market's kiosk, then:
///
///   - the market's rules: a tonne dearer as stock runs short, cheaper as it piles up; buying and selling back loses;
///     stock moves with universe time, made or used up, between none and full;
///   - at the kiosk the walker may trade; using it opens the market's window, frees the mouse, holds the walker;
///   - buying takes credits and puts the tonnes in the ship's hold (the pilot's record), no more than the hold takes
///     and the money pays for; the hold's tonnes weigh on the ship and the computer reads them on the bus;
///   - selling gives credits back for less than they cost; fines owed are paid at the market;
///   - wanted here: held on the pad; with no money, surrendering seizes the cargo and impounds the ship, and the
///     insurer's courtesy ship (a small hold, old green monochrome screens) takes its place; paying releases it;
///   - not docked, nothing can be traded; the ship blown up, its cargo goes with it;
///   - Close gives the walker back;
///   - at the lockers' kiosk the pilot stores and takes things (the insurer's backup diskette every pilot starts
///     with), carrying no more than the inventory's slots; each station's locker is its own.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/market_test.tscn (XPACE_MARKET_SHOTS=dir, in a window,
/// saves pictures). Exits with 0 if every check passed.
/// </summary>
public partial class MarketTest : Node
{
    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        Rules();

        Universe universe = Universe.Current;
        universe.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Node3D>();
        AddChild(main);
        _ship = main.GetNode<ShipBody>("Ship");
        var seat = _ship.GetNode<ShipSeat>("Seat");
        var radio = _ship.GetNode<RadioDevice>("Radio");
        var hold = _ship.GetNode<CargoHold>("Hold");
        Node3D station = main.GetNode<Node3D>("Station");
        StationMarket market = station.GetNode<StationMarket>("Market");
        HangarDock dock = station.GetNode("HangarBig1").GetNode<HangarDock>("Dock");
        var kiosk = dock.GetParent().GetNode<StationKiosk>("MarketKiosk");
        PilotRecord me = universe.Me;

        Check($"the fighter's racks hold {hold.Capacity} t", hold.Capacity == _ship.Spec.CargoCapacity && hold.Capacity > 0);
        Check("the computer boots", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 90));
        var request = new MarketRequest { Station = market.Radio.Sign, Do = MarketRequest.Buy, Good = "food", Tonnes = 1 };
        Check("not docked: nothing traded", market.Serve(me.Id, request).Message == market.NotDockedText && me.Ship.Tonnes == 0);

        Place(dock.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 300)));
        await Seconds(1);
        radio.AskDocking();
        Check("cleared to a pad", await Until(() => radio.Dock == dock.Number && radio.Pad != 0, 5));
        LandingPad pad = dock.PadNumbered(radio.Pad);
        Place(pad.GlobalTransform * pad.Rest(_ship).TranslatedLocal(new Vector3(0, 1, 0)));
        Check("docked", await Until(() => market.Radio.DockedHere(_ship), (float)pad.SettleSeconds + 3));

        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.LeaveSeat, Pressed = false });
        Walker walker = seat.Walker;
        if (walker == null)
        {
            Check("out of the seat", false);
            Finish();
            return;
        }
        Check($"far from the kiosk, it isn't offered ({walker.Prompt})", !walker.Prompt.Contains("trade"));
        Node3D front = kiosk.GetNode<Node3D>("Front");
        walker.GlobalPosition = front.GlobalPosition + front.GlobalBasis.Y * 0.3f;
        walker.LookYaw = Mathf.Pi;              // the hangar's +Z: the kiosk faces the pads, the walker faces it
        await Seconds(0.5);
        Check($"at the kiosk the walker may trade ({walker.Prompt})", walker.Prompt.EndsWith("trade") && walker.Offered == kiosk);
        Shot("0_kiosk");

        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.Interact, Pressed = true });
        await Seconds(0.1);
        Input.ParseInputEvent(new InputEventAction { Action = OnFootInput.Interact, Pressed = false });
        await Seconds(0.3);
        var window = kiosk.Window as MarketWindow;
        Check("using it opens the market's window, holds the walker and frees the mouse",
              window != null && walker.Busy && Input.MouseMode == Input.MouseModeEnum.Visible);
        if (window == null)
        {
            Finish();
            return;
        }
        Check($"the window shows the market ({window.View?.Lines.Count} goods, {window.View?.Credits} credits)",
              window.View is { Docked: true } v && v.Lines.Count == market.Goods.Length && v.Credits == PilotRecord.StartingCredits);
        Shot("1_window");

        MarketLine food = window.View.Lines.First(l => l.Good == "food");
        float empty = _ship.Mass;
        window.Ask(MarketRequest.Buy, "food", 3);
        await Seconds(0.2);
        long spent = PilotRecord.StartingCredits - me.Credits;
        Check($"buying 3 t of food: {spent} credits, the first tonne at {food.Buy} ({window.View.Message})",
              me.Ship.Holds("food") == 3 && spent >= 3 * food.Buy && window.View.Lines.First(l => l.Good == "food").Held == 3);
        Check($"the hold has them, and they weigh ({_ship.Mass - empty:0} kg more)",
              hold.Contents.Holds("food") == 3 && Mathf.IsEqualApprox(_ship.Mass - empty, 3 * CargoHold.KilogramsPerTonne));
        await Seconds(0.1);
        Check($"the computer reads the hold on the bus ({_ship.Computer.Bus.Word(CargoHold.Slot, 4)} of {_ship.Computer.Bus.Word(CargoHold.Slot, 2)} t)",
              _ship.Computer.Bus[CargoHold.Slot, 0] == CargoHold.Type && _ship.Computer.Bus.Word(CargoHold.Slot, 4) == 3 &&
              _ship.Computer.Bus.Word(CargoHold.Slot, 2) == hold.Capacity && _ship.Computer.Bus[CargoHold.Slot, 6] == 1);

        window.Ask(MarketRequest.Buy, "water", 1000);
        await Seconds(0.2);
        Check($"as many as the hold allows ({me.Ship.Holds("water")} t of water: {window.View.Message})",
              me.Ship.Tonnes == hold.Capacity && me.Ship.Holds("water") == hold.Capacity - 3 && window.View.Message.StartsWith("Bought"));
        window.Ask(MarketRequest.Sell, "water", hold.Capacity - 3);
        await Seconds(0.2);
        window.Ask(MarketRequest.Buy, "electronics", 1000);
        await Seconds(0.2);
        Check($"no more than the money allows ({me.Ship.Holds("electronics")} t of electronics: {window.View.Message})",
              me.Credits < window.View.Lines.First(l => l.Good == "electronics").Buy &&
              (me.Ship.Holds("electronics") > 0 || window.View.Message == market.NoMoneyText));
        long before = me.Credits;
        window.Ask(MarketRequest.Sell, "food", 3);
        await Seconds(0.2);
        Check($"selling it back gives less than it cost ({me.Credits - before} of {spent})",
              me.Ship.Holds("food") == 0 && me.Credits - before < spent && me.Credits > before);
        window.Ask(MarketRequest.Sell, "food", 1);
        await Seconds(0.2);
        Check($"none left to sell ({window.View.Message})", window.View.Message == market.NotHeldText);

        universe.State.Station(market.Radio.Sign).CrimesOf(me.Id).Fines = 200;
        before = me.Credits;
        window.Ask(MarketRequest.Pay, null, 0);
        await Seconds(0.2);
        Check($"fines are paid at the market ({window.View.Message})",
              universe.State.Station(market.Radio.Sign).CrimesOf(me.Id).Fines == 0 && me.Credits == before - 200);
        Shot("2_traded");

        // Wanted here, with no money to pay: held, then surrendering
        CrimeRecord crimes = universe.State.Station(market.Radio.Sign).CrimesOf(me.Id);
        crimes.Bounty = 5000;
        universe.Touch(me);
        window.Ask(MarketRequest.Buy, "water", 2);
        await Seconds(0.2);
        radio.AskLaunch();
        await Seconds(1);
        string heard = radio.Heard.Count > 0 ? radio.Heard[^1].Text : "";
        Check($"wanted here: held on the pad ({heard})", heard.Contains("wanted") && pad.State == LandingPad.PadState.Docked);
        window.Ask(MarketRequest.Look, null, 0);
        await Seconds(0.2);
        Check("the window offers to surrender", window.GetNode<Button>("%Surrender").Visible && window.View.Bounty == 5000);
        CockpitScreen screen = _ship.FindChildren("*", "MeshInstance3D", true, false).OfType<CockpitScreen>()
            .First(sc => sc.Source == ScreenSource.Computer);
        Color Tube() => ((ShaderMaterial)screen.MaterialOverride).GetShaderParameter("monochrome").AsColor();
        Check("the fighter's computer screen is a colour tube", Tube().A == 0);
        window.Ask(MarketRequest.Surrender, null, 0);
        await Seconds(0.3);
        Check($"surrendering: cargo seized, ship impounded ({window.View.Message})",
              universe.State.Station(market.Radio.Sign).Impounded.ContainsKey(me.Id) && me.Ship.Tonnes == 0 && me.Credits == 0 &&
              crimes.Owed > 0 && window.View.Impounded);
        Check($"the insurer's courtesy ship instead: its hull ({_ship.Spec.ResourcePath}), its hold ({hold.Capacity} t)",
              _ship.Spec.ResourcePath == ShipRecord.CourtesyHull && hold.Capacity == _ship.Spec.CargoCapacity && hold.Capacity < 10);
        Check($"its screens old monochrome tubes ({Tube()})", Tube().A == 1 && Tube().G > Tube().R);
        Check("no longer held: it may launch", !market.Radio.Security.Holds(_ship));

        me.Credits = 100_000;
        universe.Touch(me);
        window.Ask(MarketRequest.Pay, null, 0);
        await Seconds(0.3);
        Check($"paying it all releases the ship ({window.View.Message})",
              crimes.Owed == 0 && !universe.State.Station(market.Radio.Sign).Impounded.ContainsKey(me.Id) &&
              _ship.Spec.ResourcePath == ShipRecord.StarterHull && Tube().A == 0);

        window.Ask(MarketRequest.Buy, "water", 2);
        await Seconds(0.2);
        window.Close();
        await Seconds(0.2);
        Check("Close gives the walker back", kiosk.Window == null && !walker.Busy && walker.Camera.Current &&
              (DisplayServer.GetName() == "headless" || Input.MouseMode == Input.MouseModeEnum.Captured));

        // The lockers' kiosk beside it
        var lockers = dock.GetParent().GetNode<StationKiosk>("LockerKiosk");
        Node3D lockerFront = lockers.GetNode<Node3D>("Front");
        walker.GlobalPosition = lockerFront.GlobalPosition + lockerFront.GlobalBasis.Y * 0.3f;
        await Seconds(0.5);
        Check($"at the lockers' kiosk the walker may open their locker ({walker.Prompt})", walker.Offered == lockers);
        lockers.Use(walker);
        await Seconds(0.3);
        var locker = lockers.Window as LockerWindow;
        Check($"its window shows what the pilot carries: the insurer's backup diskette ({locker?.View?.Carried.Count} things)",
              locker?.View is { Present: true } lv && lv.Carried.Count == 1 && lv.Carried[0].Kind == ItemRecord.Diskette && lv.Locker.Count == 0);
        Shot("3_locker");
        locker.Ask(LockerRequest.Store, 0);
        await Seconds(0.2);
        Check($"stored: in the locker here, not carried ({locker.View.Message})",
              me.Items.Count == 0 && universe.State.Station(market.Radio.Sign).LockerOf(me.Id).Count == 1 && locker.View.Locker.Count == 1);
        for (int i = 0; i < PilotRecord.InventorySlots; i++)
            me.Items.Add(new ItemRecord { Name = $"Disk {i}" });
        locker.Ask(LockerRequest.Take, 0);
        await Seconds(0.2);
        Check($"carrying all a pilot can, nothing more is taken ({locker.View.Message})", locker.View.Message == "You can't carry any more.");
        me.Items.Clear();
        locker.Ask(LockerRequest.Take, 0);
        await Seconds(0.2);
        Check("taken back", me.Items.Count == 1 && locker.View.Locker.Count == 0);
        locker.Close();
        await Seconds(0.2);
        Check("another station's locker is another locker", universe.State.Station("KESTREL").LockerOf(me.Id).Count == 0);

        int carried = me.Ship.Tonnes;
        _ship.GetNode<ShipHull>("Hull").Destroy();
        await Seconds(0.2);
        Check($"the ship blown up, its cargo ({carried} t) goes with it", carried == 2 && me.Ship.Tonnes == 0 && universe.Me.Ship.Cargo.Count == 0);
        Finish();
    }

    private void Rules()
    {
        var line = new MarketGood { Good = GoodsCatalog.Find("food"), Stock = 100, Rate = -10 };
        Check("the goods' catalog loads", line.Good is { Name: "Food" } && GoodsCatalog.All.Goods.Length >= 8);
        Check($"a tonne at the wanted stock is at the base price ({Market.Price(line, 100)})", Market.Price(line, 100) == line.Good.BasePrice);
        Check("dearer as stock runs short, cheaper as it piles up",
              Market.Price(line, 25) > Market.Price(line, 100) && Market.Price(line, 250) < Market.Price(line, 100));
        Check("never past the dearest or the cheapest",
              Market.Price(line, 0) == (long)Math.Round(line.Good.BasePrice * Market.Dearest) &&
              Market.Price(line, 10_000) == (long)Math.Round(line.Good.BasePrice * Market.Cheapest));
        var station = new StationRecord();
        Market.Advance(station, new[] { line }, 0);
        Check($"a new market starts with the stock it wants ({station.Stock["food"]})", station.Stock["food"] == 100);
        Market.Advance(station, new[] { line }, 3600 * 2);
        Check($"two hours on, it has used 20 t ({station.Stock["food"]})", Math.Abs(station.Stock["food"] - 80) < 1e-9);
        Market.Advance(station, new[] { line }, 3600 * 100);
        Check("and never less than none", station.Stock["food"] == 0);
        Check("with none, nothing to sell", Market.BuyPrice(line, 0) < 0);
        var made = new MarketGood { Good = line.Good, Stock = 100, Rate = 50 };
        Market.Advance(station, new[] { made }, 3600 * 1000);
        Check("made, it piles up to full and no more", station.Stock["food"] == Market.Full * 100 && Market.SellPrice(made, station.Stock["food"]) < 0);

        var pilot = new PilotRecord { Credits = 10_000 };
        station.Stock["food"] = 100;
        Market.Buy(station, line, pilot, 5, 100, out int bought, out long paid);
        Market.Sell(station, line, pilot, 5, out int sold, out long earned);
        Check($"buying and selling back at once loses ({paid} paid, {earned} back)", bought == 5 && sold == 5 && earned < paid);
        Check("a hold that is full takes no more",
              Market.Buy(station, line, pilot, 5, 2, out bought, out _) == Market.Result.HoldFull && bought == 2);
    }

    private void Place(Transform3D at) =>
        _ship.State = new ShipState { Position = at.Origin, Orientation = at.Basis.GetRotationQuaternion() };

    private void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Shot(string name)
    {
        string dir = System.Environment.GetEnvironmentVariable("XPACE_MARKET_SHOTS");
        if (string.IsNullOrEmpty(dir) || DisplayServer.GetName() == "headless")
            return;
        GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
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
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
