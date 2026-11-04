using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Menu;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// How shadows are spent. The sun's cascades are in the metres of its flying
/// reach, and the low setting's two take the middle and far reaches. A hangar
/// loses its inside past its detail range and keeps what shows from outside,
/// with a plug in its mouth from there on. The field's big rocks cast shadows
/// (its small ones do not; that is in how the chunks are drawn).
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/sun_shadows_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SunShadowsTest : Node
{
    private int _failures;

    public override async void _Ready()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<SectorHost>();
        AddChild(main);
        await Frames(5);
        var sun = main.Sector.GetNode<DirectionalLight3D>("Sun");
        var reach = sun.GetNode<SunShadows>("Shadows").Flying;
        GameSettings settings = GameSettings.Current;
        Quality was = settings.Shadows;

        settings.Shadows = Quality.High;
        await Frames(2);
        Check($"the sun's cascades are its flying reach ({sun.DirectionalShadowMaxDistance:0} m)",
              Near(sun.DirectionalShadowMaxDistance, reach.Max) &&
              Near(sun.DirectionalShadowSplit1 * reach.Max, reach.Near) &&
              Near(sun.DirectionalShadowSplit2 * reach.Max, reach.Middle) &&
              Near(sun.DirectionalShadowSplit3 * reach.Max, reach.Far));

        Check($"its edges smoothed as the reach says ({sun.ShadowBlur}), with no soft shadows from the sun's size",
              Near(sun.ShadowBlur, reach.Blur) && sun.LightAngularDistance == 0);

        settings.Shadows = Quality.Low;
        await Frames(2);
        Check($"low shadows: two cascades, the middle and far reaches ({sun.DirectionalShadowMaxDistance:0} m)",
              Near(sun.DirectionalShadowMaxDistance, reach.Far) && Near(sun.DirectionalShadowSplit1 * reach.Far, reach.Middle));
        settings.Shadows = was;

        var hangar = main.Sector.GetNode("MiltecAlpha").FindChildren("*", "Node3D", true, false).OfType<HangarDock>().First().GetParent();
        float range = hangar.GetNode<DetailRange>("Detail").Range;
        var floor = hangar.GetNode("Floor").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().FirstOrDefault()
            ?? hangar.GetNode<MeshInstance3D>("Floor");
        var door = hangar.GetNode("Model").FindChild("SM_Bld_HangarDoor_01", true, false) as GeometryInstance3D;
        var casing = hangar.GetNode("Casing").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First();
        var plug = hangar.GetNode<GeometryInstance3D>("FarPlug");
        var light = hangar.GetNode<OmniLight3D>("Light1");
        Check($"the hangar's floor goes past {range:0} m", Near(floor.VisibilityRangeEnd, range));
        Check("its lights fade out by then", light.DistanceFadeEnabled && Near(light.DistanceFadeBegin + light.DistanceFadeLength, range));
        Check("its door and casing stay", door?.VisibilityRangeEnd == 0 && casing.VisibilityRangeEnd == 0);
        Check("the plug in its mouth starts there and ends nowhere", Near(plug.VisibilityRangeBegin, range) && plug.VisibilityRangeEnd == 0);
        var small = hangar.GetNode("Model").FindChild("SM_Bld_HangarDoor_01_small", true, false) as GeometryInstance3D;
        var columns = hangar.GetNode("Model").GetChildren().OfType<MeshInstance3D>().Count(m => m.Name.ToString().StartsWith("SM_Bld_Hangar_01_"));
        Check($"the room is in columns ({columns} meshes)", columns > 10);
        Check("small parts go far sooner, and cast no shadows",
              small != null && small.VisibilityRangeEnd > 0 && small.VisibilityRangeEnd < range && small.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off);
        Check("the sector's view culls what walls hide", main.View.UseOcclusionCulling || DisplayServer.GetName() == "headless");

        // The door: its number rides with it, and the mouth hides nothing unless it is quite shut
        var dock = hangar.GetChildren().OfType<HangarDock>().First();
        var mark = hangar.GetNode<HangarMark>("Number");
        var mouth = hangar.GetNode<Node3D>("Occluders/Mouth");
        var doorMesh = (Node3D)door;
        dock.Show(false);
        await Frames(3);
        Check("shut, the mouth hides what is behind it", mouth.Visible);
        float markFrom = mark.Position.Y, doorFrom = doorMesh.Position.Y;
        dock.Show(true);
        await Frames(120);
        float rose = doorMesh.Position.Y - doorFrom;
        Check($"opening, the door rises ({rose:0.0} m) and the number with it ({mark.Position.Y - markFrom:0.0} m)",
              rose > 0.5f && Mathf.Abs(mark.Position.Y - markFrom - rose) < 0.1f);
        Check("and the mouth hides nothing", !mouth.Visible);
        dock.Show(false);

        Check("the field's rocks cast shadows", main.Sector.GetNode<RockField>("Asteroids").CastShadows);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f * Mathf.Max(1, Mathf.Abs(b));

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
