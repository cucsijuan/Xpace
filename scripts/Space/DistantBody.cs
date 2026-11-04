using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Something too big and too far off for the camera's far plane, a planet: sectors are wider than what a camera can
/// hold, and past it the planet would be cut open and then gone. This node stays where the body really is (what
/// counts for the medium, the map, the floating origin); its <see cref="Body"/> child, the part that shows, is brought
/// towards the eye and shrunk by the same share, just enough to fit inside the far plane, so it looks the same size in
/// the same place. Near enough to fit, it is simply where the node is. Its meshes' material takes that share
/// (assets/materials/planet_surface.gdshader) and writes the depth of the real place, so nothing is hidden by a planet
/// it stands in front of.
/// </summary>
public partial class DistantBody : Node3D
{
    /// <summary>The body's radius, metres.</summary>
    [Export] public float Radius = 20000f;

    /// <summary>How much of the camera's far plane it may fill.</summary>
    [Export] public float Reach = 0.6f;

    public Node3D Body { get; private set; }

    private const string Share = "share";

    private GeometryInstance3D[] _meshes;

    public override void _Ready()
    {
        Body = GetNode<Node3D>("Body");
        _meshes = Body.FindChildren("*", nameof(GeometryInstance3D), true, false).Append(Body).OfType<GeometryInstance3D>().ToArray();
        ProcessPriority = 1000;                 // after whatever moves the camera
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GetCamera3D() is not Camera3D camera)
            return;
        Vector3 eye = camera.GlobalPosition, at = GlobalPosition;
        float share = Mathf.Min(1f, camera.Far * Reach / (eye.DistanceTo(at) + Radius));
        Body.GlobalPosition = eye + (at - eye) * share;
        Body.Scale = Vector3.One * share;
        foreach (GeometryInstance3D mesh in _meshes)
            mesh.SetInstanceShaderParameter(Share, share);
    }
}
