using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The game's shell (<see cref="SectorHost"/>) with a map of two sectors: the
/// player's ship starts in the first, where the map says, with its computer
/// booting; a jump takes it whole to the second, which is now the one drawn
/// (its camera the second world's), and lets the first go. In the new sector
/// the ship's rounds are that sector's, and its floating origin follows it.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/sector_jump_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SectorJumpTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var map = new SectorMap { Start = "a" };
        map.Sectors.Add(new Sector { Id = "a", Title = "A", Scene = "res://tests/fixtures/sectors/a.tscn" });
        map.Sectors.Add(new Sector { Id = "b", Title = "B", Scene = "res://tests/fixtures/sectors/b.tscn" });
        var host = new SectorHost { Map = map, ShipScene = GD.Load<PackedScene>("res://scenes/ships/player_ship.tscn") };
        AddChild(host);
        ShipBody ship = host.Ship;
        var camera = ship.GetNode<Camera3D>("Pilot/Camera3D");
        Check("the ship starts in the map's first sector", host.Here?.Id == "a" && ship.GetWorld3D() == host.View.FindWorld3D());
        Check("the pilot's camera is the one drawn", host.View.GetCamera3D() == camera);
        Check("the computer boots", await Until(() => ship.Computer.ScreenText().Contains("Radio resident"), 90));

        FloatingOrigin a = host.Sector;
        double before = ship.Computer.EmulatedSeconds;
        string screen = ship.Computer.ScreenText();
        host.Jump("b", new Transform3D(Basis.Identity, new Vector3(0, 0, -200)));
        await Frames(2);
        Check("the jump takes it to the second sector", host.Here?.Id == "b" && ship.GetWorld3D() == host.View.FindWorld3D());
        Check("the first sector is let go", host.Root("a") == null && !IsInstanceValid(a));
        Check("its camera is the second world's", host.View.GetCamera3D() == camera && camera.Current);
        Check($"the same computer runs on ({before:0.0} -> {ship.Computer.EmulatedSeconds:0.0} s)",
              ship.Computer.EmulatedSeconds >= before && ship.Computer.EmulatedSeconds < before + 1 && ship.Computer.ScreenText() == screen);
        Check("its rounds fly in the second sector", Projectiles.In(ship) == host.Sector.GetNode("Projectiles"));

        // The floating origin of the new sector keeps the ship near the origin
        ship.GlobalPosition = new Vector3(1500, 0, 0);
        await Frames(2);
        Check($"the new sector's origin follows the ship ({ship.GlobalPosition.Length():0} m out, origin {host.Sector.Origin.X:0})",
              ship.GlobalPosition.Length() < 100 && Math.Abs(host.Sector.Origin.X - 1500) < 100);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task<bool> Until(Func<bool> condition, float seconds)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            if (condition())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return condition();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
