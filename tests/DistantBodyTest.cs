using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A planet further off than the camera can see: its body comes inside the far plane, whole, and looks the same size in
/// the same place; near enough to fit, it is where it really is.
///
/// Run: Godot --headless --path . res://tests/distant_body_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class DistantBodyTest : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        var camera = new Camera3D { Far = 1000, Current = true };
        AddChild(camera);
        var planet = new DistantBody { Radius = 100, Position = new Vector3(3000, 0, -4000) };
        var body = new MeshInstance3D { Name = "Body" };
        planet.AddChild(body);
        AddChild(planet);
        await Frames(2);

        float drawn = body.GlobalPosition.Length(), size = 100 * body.Scale.X;
        Check($"far off, all of it is inside the far plane ({drawn + size:0} m of {camera.Far:0})", drawn + size <= camera.Far * planet.Reach * 1.001f);
        Check($"it looks as big ({size / drawn:0.0000} against {100f / 5000f:0.0000})", Mathf.IsEqualApprox(size / drawn, 100f / 5000f));
        Check("it is in the same direction", body.GlobalPosition.Normalized().IsEqualApprox(planet.GlobalPosition.Normalized()));
        Check("its material is told the share", Mathf.IsEqualApprox((float)body.GetInstanceShaderParameter("share"), body.Scale.X));
        Check("the node stays where the planet is", planet.GlobalPosition.IsEqualApprox(new Vector3(3000, 0, -4000)));

        planet.Position = new Vector3(0, 0, -500);
        await Frames(2);
        Check("near enough to fit, it is where it is, at its size", body.GlobalPosition.IsEqualApprox(planet.GlobalPosition) && body.Scale.IsEqualApprox(Vector3.One));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
