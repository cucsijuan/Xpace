using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Tests;

/// <summary>
/// Where the pilot's ship is kept between games, in a universe on disk (user://test_place):
///
///   - a new pilot starts docked at the map's start station (Alpha), on a pad, the door shut;
///   - the game closed and started again, the ship is back on the same pad;
///   - left in space, it comes back there, in the same sector, turned and moving as it was;
///   - the save deleted, nothing of it is left and the next game starts docked again with a new pilot's money.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/pilot_place_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PilotPlaceTest : Node
{
    private const string Folder = "user://test_place";
    private int _failures;

    public override async void _Ready()
    {
        Universe universe = Universe.Current;
        universe.Delete(Folder, null);
        universe.Open(Folder);

        // A new pilot
        SectorHost main = await Start();
        ShipBody ship = main.Ship;
        var pad = ship.Holder as LandingPad;
        Check($"a new pilot starts docked at Alpha ({pad?.Dock?.Station?.Id} hangar {pad?.Dock?.Number} pad {pad?.Number})",
              pad is { State: LandingPad.PadState.Docked, Dock.Station.Id: "ALPHA" } && pad.Docks(ship) && pad.Dock.Station.DockedHere(ship) && !pad.Dock.IsOpen);
        int dock = pad?.Dock.Number ?? 0, number = pad?.Number ?? 0;
        universe.Me.Credits += 123;
        universe.Touch(universe.Me);

        // Closed and started again
        await Stop(main);
        Check("the record says where it was left", universe.Me.Ship.Place is { Station: "ALPHA" } p && p.Dock == dock && p.Pad == number);
        universe.Open(Folder);
        main = await Start();
        ship = main.Ship;
        pad = ship.Holder as LandingPad;
        Check("started again: on the same pad", pad is { State: LandingPad.PadState.Docked } && pad.Dock.Number == dock && pad.Number == number &&
                                                 universe.Me.Credits == PilotRecord.StartingCredits + 123);

        // Left in space
        ship.Device<RadioDevice>()?.Cleared(null, 0, 0);       // gone from the pad (or its clamps take it again)
        ship.Holder = null;
        Quaternion turn = new Quaternion(Vector3.Up, 1.1f) * new Quaternion(Vector3.Right, 0.3f);
        ship.State = new ShipState { Position = ship.GlobalPosition + new Vector3(0, 6000, 0), Orientation = turn, Velocity = turn * new Vector3(0, 0, -20) };
        await Seconds(2);
        SectorPoint left = FloatingOrigin.Of(ship).Origin + ship.GlobalPosition;
        Quaternion leftTurn = ship.State.Orientation;
        await Stop(main);
        ShipPlace place = universe.Me.Ship.Place;
        Check($"left in space: kept with no station, {Distance(place, left):0.0} m from where it was", place is { Station: null, Sector: "sector_01" } && Distance(place, left) < 1);
        universe.Open(Folder);
        main = await Start();
        ship = main.Ship;
        SectorPoint back = FloatingOrigin.Of(ship).Origin + ship.GlobalPosition;
        float off = back.To(left).Length();
        Check($"started again: back in space where it was left ({off:0.0} m off), turned as it was ({Mathf.RadToDeg(ship.State.Orientation.AngleTo(leftTurn)):0.0} deg), near the origin ({ship.GlobalPosition.Length():0} m)",
              ship.Holder == null && off < 15 && ship.State.Orientation.AngleTo(leftTurn) < 0.05f && ship.GlobalPosition.Length() < 1000);

        // Deleted
        await Stop(main);
        universe.Delete(Folder, null);
        Check("deleted: its folder is gone", !DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(Folder)));
        universe.Open(Folder);
        main = await Start();
        pad = main.Ship.Holder as LandingPad;
        Check("the next game starts anew, docked at Alpha", pad is { Dock.Station.Id: "ALPHA" } && universe.Me.Credits == PilotRecord.StartingCredits);
        await Stop(main);
        universe.Delete(Folder, null);
        universe.Forget();

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float Distance(ShipPlace place, SectorPoint at) =>
        place == null ? float.MaxValue : new SectorPoint(place.X, place.Y, place.Z).To(at).Length();

    private async Task<SectorHost> Start()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Seconds(1);
        return main;
    }

    // As the game closing: the universe written while the ship is still there, then everything goes
    private async Task Stop(SectorHost main)
    {
        Universe.Current.Save();
        main.QueueFree();
        await Seconds(0.2);
        Universe.Current.Save();
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
