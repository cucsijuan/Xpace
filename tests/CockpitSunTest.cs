using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Cockpit;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The cockpit's own sun: the cockpit's meshes are on its layer, which the
/// sector's sun leaves alone; it stands towards the sun from the cockpit with
/// the sun's strength, riding with the ship; something solid between the
/// cockpit and the sun, however far, puts it out; and after a jump it is the
/// new sector's sun it stands for.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/cockpit_sun_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class CockpitSunTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        ShipBody ship = main.Ship;
        var cockpit = ship.GetNode<Node3D>("Cockpit");
        var light = cockpit.GetNode<CockpitSun>("CockpitSun");
        await Frames(5);
        uint bit = 1u << (light.Layer - 1);

        DirectionalLight3D sun = light.Sun;
        Check("it stands for the sector's sun", sun != null && sun == main.Sector.GetNode("Sun"));
        var meshes = cockpit.FindChildren("*", nameof(VisualInstance3D), true, false).OfType<VisualInstance3D>().ToList();
        Check($"the cockpit's meshes are on its layer ({meshes.Count})", meshes.Count > 0 && meshes.All(m => m.Layers == bit));
        Check("it lights that layer, and the sun does not", light.LightCullMask == bit && (sun.LightCullMask & bit) == 0);
        Check("the sun still lights everything else", (sun.LightCullMask & 1) != 0);
        Check("as strong as the sun", Math.Abs(light.LightEnergy - sun.LightEnergy) < 0.01f && light.LightColor == sun.LightColor);
        Check("towards the sun from the cockpit", Near(light.GlobalPosition, cockpit.GlobalPosition + sun.GlobalBasis.Z.Normalized() * light.Distance));
        Check("shining at it", (-light.GlobalBasis.Z).Dot(-sun.GlobalBasis.Z.Normalized()) > 0.999f);

        // It rides with the ship
        ship.State = new ShipState { Position = ship.GlobalPosition + new Vector3(30, 5, -40), Orientation = new Basis(Vector3.Up, 0.7f).GetRotationQuaternion() };
        await Frames(3);
        Check("it rides with the ship, its way still the sun's", Near(light.GlobalPosition, cockpit.GlobalPosition + sun.GlobalBasis.Z.Normalized() * light.Distance));

        // Far beyond what it sees, something hides the sun
        Vector3 towards = sun.GlobalBasis.Z.Normalized();
        var wall = new Solid { Name = "Eclipse", Position = cockpit.GlobalPosition + towards * 2000 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(600, 600, 4) } });
        main.Sector.AddChild(wall);
        wall.LookAt(cockpit.GlobalPosition, Mathf.Abs(towards.Y) < 0.9f ? Vector3.Up : Vector3.Right);
        await Frames(60);
        Check($"something 2 km off hides the sun: it goes out ({light.LightEnergy:0.00})", light.Hidden && light.LightEnergy == 0 && !light.Visible);
        wall.QueueFree();
        await Frames(60);
        Check($"out of the way, it comes back ({light.LightEnergy:0.00})", !light.Hidden && light.Visible && Math.Abs(light.LightEnergy - sun.LightEnergy) < 0.01f);

        // In another sector, its sun
        main.Jump("sector_02", new Transform3D(Basis.Identity, new Vector3(0, 0, -400)));
        await Frames(5);
        DirectionalLight3D other = light.Sun;
        Check("after a jump it stands for the new sector's sun", other != null && other == main.Sector.GetNode("Sun") && (other.LightCullMask & bit) == 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < 0.5f;

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
