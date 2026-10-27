using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Asking the Synty station for a dock from COMMS.COM, with the screen's
/// keys, on a ship 2 km from it:
///
///   - the contacts list the station; SELECT gives its options, SELECT on
///     the first asks for a dock;
///   - traffic control clears the ship to a hangar (the radio and COMMS say
///     which, and the message says it in words); that hangar's door opens,
///     slowly, and its beacon lights; the others stay shut;
///   - shut, the door stops a ship's way into the hangar; open, the way is
///     clear from outside to the inside;
///   - with no ship in the hangar the clearance runs out: the door comes
///     down, the beacon goes out, and the ship is told.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/docking_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DockingTest : Node3D
{
    private ShipBody _ship;
    private RadioDevice _radio;
    private DisplayDevice _display;
    private StationRadio _station;
    private int _failures;

    public override async void _Ready()
    {
        var station = GD.Load<PackedScene>("res://scenes/stations/synty_station.tscn").Instantiate<Node3D>();
        station.Name = "Haven";
        AddChild(station);
        _station = station.GetNode<StationRadio>("Radio");
        _station.ClearanceSeconds = 30;

        _ship = new ShipBody { Spec = GD.Load<ShipSpec>("res://assets/ship/hulls/hull_fighter.tres"), Position = new Vector3(0, 0, 2000) };
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice { Screens = new Godot.Collections.Array<DisplayScreen> { GD.Load<DisplayScreen>("res://assets/ship/displays/comms.tres") } };
        _ship.AddChild(_display);
        _radio = new RadioDevice { Name = "Radio", Callsign = "TESTER" };
        _ship.AddChild(_radio);
        AddChild(_ship);

        HangarDock[] docks = _station.Docks.ToArray();
        Check($"the station has its docks numbered ({docks.Length})", docks.Length == 4 && docks.Select(d => d.Number).SequenceEqual(new[] { 1, 2, 3, 4 }));
        Check("the doors start shut and dark", docks.All(d => d.Door.At == 0 && !d.Beacon.Visible));
        HangarDock any = docks[0];
        Check("shut, a door stops the way in", Obstacles.Segment(any.ApproachPoint, any.InsidePoint, out _, out _));

        Check("COMMS.COM is resident", await Until(() => _ship.Computer.ScreenText().Contains("Radio resident"), 60));
        await Seconds(0.5);
        Check($"the station is listed, under the cursor\n{Comms()}", Comms().Contains("HAVEN") && Comms().Contains("STATION"));

        await Key(DisplayKey.Select);
        Check($"SELECT: the station's options\n{Comms()}", Comms().Contains("REQUEST DOCKING") && Comms().Contains("REQUEST LAWS"));
        await Key(DisplayKey.Select);
        Check("SELECT on the first: cleared to a dock", await Until(() => _radio.Dock != 0, 3));
        HangarDock dock = docks.First(d => d.Number == _radio.Dock);
        float nearest = docks.Min(d => d.GlobalPosition.DistanceTo(_ship.GlobalPosition));
        Check($"hangar {dock.Number}, the nearest", dock.GlobalPosition.DistanceTo(_ship.GlobalPosition) == nearest);
        await Seconds(0.5);
        Check($"COMMS shows it and the words\n{Comms()}", Comms().Contains($"HANGAR {dock.Number}") && Comms().Contains("CLEARED TO DOCK"));
        Check("its beacon lights, its door starts up; the others stay shut",
              dock.Beacon.Visible && dock.Door.Open == 1 && docks.Where(d => d != dock).All(d => d.Door.Open == 0 && !d.Beacon.Visible));
        await Seconds(4);
        Check($"slowly ({dock.Door.At:0.00} open after 4 s), and still in the way", dock.Door.At > 0.2f && dock.Door.At < 0.5f
              && Obstacles.Segment(dock.ApproachPoint, dock.InsidePoint, out _, out _));
        await Seconds(10);
        bool blocked = Obstacles.Segment(dock.ApproachPoint, dock.InsidePoint, out float along, out _);
        Check($"open, the way in is clear{(blocked ? $" (blocked at {along:0.00})" : "")}", dock.Door.At == 1 && !blocked);

        await Key(DisplayKey.Select);
        await Key(DisplayKey.Select);
        await Seconds(1);
        Check("asking again gives the same hangar", _radio.Dock == dock.Number);

        Check("with no ship in it, the clearance runs out", await Until(() => _radio.Dock == 0, 40));
        await Seconds(0.5);
        Check($"the beacon goes out, the door comes down, the ship is told\n{Comms()}",
              !dock.Beacon.Visible && dock.Door.Open == 0 && _radio.Heard[^1].Text.Contains("expired"));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private string Comms() => _display.Text("COMMS");

    private async Task Key(DisplayKey key)
    {
        _display.PressKeyOn("COMMS", key);
        await Seconds(0.2);
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
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
