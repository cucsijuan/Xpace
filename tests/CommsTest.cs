using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// COMMS's list goes nearest first, so it changes order as things move. A key
/// must act on the contact the cursor is on, even when the list changed after
/// the screen was last drawn: here the ship jumps between a gate and a station
/// in the very step a key arrives.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/comms_test.tscn
/// </summary>
public partial class CommsTest : Node
{
    private int _failures;
    private ShipBody _ship;

    public override async void _Ready()
    {
        Universe.Current.Forget();
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        _ship = main.Ship;
        var cruise = _ship.Find<CruiseDevice>();
        var radio = _ship.Find<RadioDevice>();
        var display = _ship.Find<DisplayDevice>();
        foreach (string ship in new[] { "Drone1", "Drone2", "Raider", "InterdictorBuoy" })
            main.Sector.GetNodeOrNull(ship)?.QueueFree();
        JumpGate gate = main.Sector.GetNode<JumpGate>("Gate2");
        Node3D station = main.Sector.GetNode<Node3D>("MiltecAlpha");

        await Seconds(25);
        foreach (CargoShipRecord ship in Universe.Current.State.CargoShips.Values)
            ship.NextLook = 1e12;
        Place(station.GlobalPosition + new Vector3(0, 6000, 0));
        await Seconds(2);
        int row = radio.Contacts.ToList().IndexOf(gate.Radio);
        Check($"by the station the gate is down the list (row {row})", row > 0);
        for (int i = 0; i < row; i++)
            await Key(display, DisplayKey.Down);

        // The cursor is on the gate. The gate comes to the top just as SELECT arrives
        await Seconds(0.05);
        Place(gate.GlobalPosition + new Vector3(0, 9000, 0));
        await Key(display, DisplayKey.Select);
        Check("the gate is at the top now", radio.Contacts[0] == gate.Radio);
        await Key(display, DisplayKey.Down);
        await Key(display, DisplayKey.Down);

        // And goes back down just as the choice is made
        await Seconds(0.05);
        Place(station.GlobalPosition + new Vector3(0, 6000, 0));
        await Key(display, DisplayKey.Select);
        await Seconds(0.2);
        Check($"the course is for the gate ({cruise.Course?.Callsign})", ReferenceEquals(cruise.Course, gate.Radio));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Place(Vector3 at) => _ship.State = new ShipState
    {
        Position = at, Orientation = Quaternion.Identity, Velocity = Vector3.Zero, AngularVelocity = Vector3.Zero,
    };

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
