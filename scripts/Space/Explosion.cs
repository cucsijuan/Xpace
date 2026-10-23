using Godot;

namespace Xpace.Space;

/// <summary>A fireball that grows, fades and goes: a drone blowing up, a missile's warhead.</summary>
public static class Explosion
{
    public static void Spawn(Node parent, Vector3 where, float size = 25f, double seconds = 1.2, float light = 12f)
    {
        var fire = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.7f, 0.3f, 1f),
        };
        var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f }, MaterialOverride = fire };
        var glow = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = light, OmniRange = size * 5 };
        parent.AddChild(ball);
        parent.AddChild(glow);
        ball.GlobalPosition = glow.GlobalPosition = where;
        var t = ball.CreateTween().SetParallel();
        t.TweenProperty(ball, "scale", Vector3.One * size, seconds);
        t.TweenProperty(fire, "albedo_color", new Color(1f, 0.3f, 0.1f, 0f), seconds);
        t.TweenProperty(glow, "light_energy", 0f, seconds);
        t.Chain().TweenCallback(Callable.From(() => { ball.QueueFree(); glow.QueueFree(); }));
    }
}
