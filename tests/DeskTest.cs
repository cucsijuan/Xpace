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
/// DeskPilot and the cargo scanner, on the stock diskette: the COMMS screen starts on the desktop, in graphics, with
/// the two applications; SELECT opens the one under the cursor and BACK comes back; the scanner reads a locked ship
/// after its time, and starts over if the lock goes.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/desk_test.tscn
/// XPACE_DESK_SHOTS=dir writes the desktop as a PNG.
/// </summary>
public partial class DeskTest : Node
{
    private int _failures;
    private ShipBody _ship;
    private DisplayDevice _display;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        _display = _ship.Find<DisplayDevice>();
        foreach (string ship in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(ship)?.QueueFree();

        Check($"the programs are resident\n{_ship.Computer.ScreenText()}", await Until(() => _ship.Computer.ScreenText().Contains("Cargo scanner resident"), 90));
        await Seconds(1);
        Check("the logo comes first, on navy", _display.Pixel("COMMS", 2, 2) == 2 && _display.Pixel("COMMS", 128, 40) != 2);
        double heard = 0;
        for (int i = 0; i < 120; i++)
        {
            heard = Math.Max(heard, _ship.Find<Xpace.Cockpit.ComputerSpeaker>().Hz);
            await Seconds(1.0 / 60);
        }
        Shot("logo");
        Check($"its tune plays on the speaker ({heard:0} Hz at most)", heard > 1000);
        await Seconds(3);
        Check("and the speaker falls silent", _ship.Computer.SpeakerHz == 0);
        Check("the desktop is on the screen, nobody's mark on it", _display.Lent("COMMS") == 0);
        Check("it paints a window on teal", _display.Pixel("COMMS", 2, 2) == 1 && _display.Pixel("COMMS", 128, 100) == 3 && _display.Pixel("COMMS", 40, 15) == 2);
        Check("the first icon and the chosen name's bar are there", Inked(20, 34, 52, 66) && _display.Pixel("COMMS", 58, 41) == 2);
        Check("the second icon is there", Inked(20, 76, 52, 108));
        Shot("desktop");

        await Key(DisplayKey.Down);
        await Seconds(0.6);
        Check("DOWN moves the bar to the second", _display.Pixel("COMMS", 58, 41) == 3 && _display.Pixel("COMMS", 58, 83) == 2);
        await Key(DisplayKey.Up);
        await Seconds(0.6);
        await Key(DisplayKey.Select);
        await Seconds(0.5);
        Check($"SELECT opens COMMS\n{Text()}", _display.Lent("COMMS") == 'C' && Text().Contains("RADIO 1.7"));
        await Key(DisplayKey.Back);
        await Seconds(1);
        Check("BACK on the list goes back to the desktop", _display.Lent("COMMS") == 0 && _display.Pixel("COMMS", 2, 2) == 1);

        await Key(DisplayKey.Down);
        await Seconds(0.6);
        await Key(DisplayKey.Select);
        await Seconds(0.7);
        Check($"SELECT opens the scanner\n{Text()}", _display.Lent("COMMS") == 'S' && Text().Contains("CARGO SCANNER"));
        Check($"with no lock it says so\n{Text()}", Text().Contains("NO LOCK"));

        // A ship with something in its hold, ahead, locked by the radar
        var prey = GD.Load<PackedScene>("res://scenes/ships/ai_freighter.tscn").Instantiate<ShipBody>();
        prey.Name = "Prey";
        foreach (FighterAi ai in prey.FindChildren("*", "", true, false).OfType<FighterAi>())
            ai.ProcessMode = ProcessModeEnum.Disabled;
        var hold = new CargoHold();
        hold.Carried.Load("iron", 4);
        hold.Carried.Steal("iron", 3);
        prey.AddChild(hold);
        main.Sector.AddChild(prey);
        prey.GlobalPosition = _ship.GlobalPosition - _ship.GlobalBasis.Z * 2000;
        await Seconds(1);
        var radar = _ship.Find<RadarDevice>();
        var scanner = _ship.Find<ScannerDevice>();
        Check("the ship has a scanner of 15 seconds", scanner?.Spec?.Seconds == 15);
        await Lock(radar);
        Check("the radar locks it", radar.Locked != null);
        await Seconds(0.5);
        Check($"the scanner sees the lock\n{Text()}", Text().Contains("SHIP LOCKED"));

        await Key(DisplayKey.Select);
        await Seconds(5);
        Check($"it is reading, a third through ({scanner.Progress:0.00})\n{Text()}", scanner.Scanning && scanner.Progress is > 0.25f and < 0.45f && Text().Contains("READING"));

        // The lock goes: it starts over
        await Unlock(radar);
        await Seconds(0.5);
        Check($"a lost lock breaks it off\n{Text()}", !scanner.Scanning && Text().Contains("LOCK LOST"));
        await Lock(radar);
        await Seconds(0.5);
        await Key(DisplayKey.Select);
        await Seconds(10);
        Check("ten seconds are not enough", scanner.Scanning && scanner.Result == null);
        await Seconds(6);
        Check($"after its time it has read the hold\n{Text()}", scanner.Result?.Tonnes == 7 && Text().Contains("IRON") && Text().Contains("7 / "));
        await Seconds(0.5);
        Check($"and what of it is stolen\n{Text()}", scanner.Result?.StolenTonnes == 3 && scanner.Result.Stolen.GetValueOrDefault("iron") == 3 && Text().Contains("3 STOLEN"));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
    }

    private string Text() => _display.Text("COMMS");

    private bool Inked(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (_display.Pixel("COMMS", x, y) != 3)
                    return true;
        return false;
    }

    private void Shot(string name)
    {
        if (OS.GetEnvironment("XPACE_DESK_SHOTS") is not { Length: > 0 } dir)
            return;
        Color[] colours = { Colors.Black, new(0, 0.5f, 0.5f), new(0.05f, 0.1f, 0.55f), new(0.95f, 1, 0.95f) };
        Image image = Image.CreateEmpty(256, 192, false, Image.Format.Rgb8);
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 256; x++)
                image.SetPixel(x, y, colours[_display.Pixel("COMMS", x, y)]);
        image.Resize(768, 576, Image.Interpolation.Nearest);
        image.SavePng($"{dir}/{name}.png");
    }

    private async Task Lock(RadarDevice radar)
    {
        for (int i = 0; i < 6 && radar.Locked == null; i++)
        {
            await Press(c => c.TargetAhead = true);
            await Seconds(0.6);
        }
    }

    private async Task Unlock(RadarDevice radar)
    {
        for (int i = 0; i < 3 && radar.Locked != null; i++)
        {
            await Press(c => c.TargetAhead = true);
            await Seconds(0.3);
        }
    }

    private async Task Press(Action<PilotControlsBox> hands)
    {
        var box = new PilotControlsBox();
        hands(box);
        Func<PilotControls> before = _ship.ControlsSource;
        _ship.ControlsSource = () => new PilotControls { TargetAhead = box.TargetAhead };
        await Seconds(0.15);
        _ship.ControlsSource = before;
    }

    private sealed class PilotControlsBox
    {
        public bool TargetAhead;
    }

    private async Task Key(DisplayKey key)
    {
        _display.PressKeyOn("COMMS", key);
        await Seconds(0.1);
    }

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
