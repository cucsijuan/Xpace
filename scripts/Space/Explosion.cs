using Godot;

namespace Xpace.Space;

/// <summary>
/// An explosion where something blew up: a warhead, a ship. From
/// <see cref="BigFrom"/> metres on it is a ship's (scenes/fx/ship_explosion.tscn:
/// fire, sparks, burning debris), below a warhead's
/// (scenes/fx/missile_explosion.tscn); each scaled to the size asked. The
/// smallest (a round's hit) is a plain flash.
/// </summary>
public static class Explosion
{
    public const float BigFrom = 20f;
    public const float SmallBelow = 3f;
    /// <summary>How long after it goes off the fire and smoke hide what blew up (then it can go).</summary>
    public const double CoverSeconds = 0.6;
    /// <summary>When what blew up starts fading into the fire (it is gone at <see cref="CoverSeconds"/>).</summary>
    public const double FadeFrom = 0.15;

    /// <summary>
    /// How far gone something that blew up <paramref name="since"/> seconds ago
    /// is: 0 whole, 1 gone (fading while the fire swells and the smoke comes).
    /// </summary>
    public static float Gone(double since) => (float)System.Math.Clamp((since - FadeFrom) / (CoverSeconds - FadeFrom), 0, 1);

    /// <summary>Fades everything drawn under <paramref name="root"/> (0 whole, 1 gone, and then hidden).</summary>
    public static void Fade(Node3D root, float gone)
    {
        foreach (Node n in root.FindChildren("*", "", true, false))
            if (n is GeometryInstance3D g)
                g.Transparency = gone;
        if (root is GeometryInstance3D self)
            self.Transparency = gone;
        root.Visible = gone < 1;
    }

    private static PackedScene _missile, _ship;

    public static void Spawn(Node parent, Vector3 where, float size = 25f, double seconds = 1.2, float light = 12f)
    {
        if (size >= SmallBelow)
        {
            PackedScene scene = size >= BigFrom
                ? _ship ??= GD.Load<PackedScene>("res://scenes/fx/ship_explosion.tscn")
                : _missile ??= GD.Load<PackedScene>("res://scenes/fx/missile_explosion.tscn");
            if (scene?.Instantiate() is ExplosionFx fx)
            {
                fx.Size = size;
                fx.LightEnergy = light;
                fx.LightSeconds = seconds;
                parent.AddChild(fx);
                fx.GlobalPosition = where;
                return;
            }
        }
        Flash(parent, where, size, seconds, light);
    }

    /// <summary>A ball of fire that grows, fades and goes.</summary>
    private static void Flash(Node parent, Vector3 where, float size, double seconds, float light)
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
