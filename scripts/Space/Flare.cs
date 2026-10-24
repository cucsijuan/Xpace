using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A flare: a pellet that burns hotter than a ship's motor for a few
/// seconds. A heat seeker that has it in view sees it beside its target and
/// may follow it instead. Like chaff it keeps the velocity it was dropped
/// with, so it is the dropper's turn that takes the target out of the
/// seeker's view.
/// </summary>
public partial class Flare : Node3D, IHeatSource
{
    [Export] public float PeakHeat = 80f;               // a drone's motor is 50 from behind
    [Export] public float IgniteSeconds = 0.15f;
    [Export] public float BurnSeconds = 2f;

    public Vector3 Velocity { get; set; }
    public bool Burning => _age < BurnSeconds;

    /// <summary>Lights up, burns at its peak, then dies down over the last half.</summary>
    public float HeatNow
    {
        get
        {
            float ignite = Math.Clamp(_age / IgniteSeconds, 0, 1);
            float fade = 1 - Math.Clamp((_age - BurnSeconds * 0.5f) / (BurnSeconds * 0.5f), 0, 1);
            return PeakHeat * ignite * fade;
        }
    }

    private float _age;
    private MeshInstance3D _ball;
    private OmniLight3D _light;

    public override void _Ready()
    {
        AddToGroup("heat_sources");
        _ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.95f, 0.8f),
            },
        };
        AddChild(_ball);
        _light = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.5f), OmniRange = 60f };
        AddChild(_light);
    }

    public float HeatAt(Vector3 from, out Vector3 direction) => Heat.Point(GlobalPosition, HeatNow, from, out direction);

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        GlobalPosition += Velocity * (float)delta;
        float glow = HeatNow / PeakHeat;
        _ball.Scale = Vector3.One * Math.Max(0.05f, glow);
        _light.LightEnergy = 4f * glow;
        if (!Burning)
            QueueFree();
    }
}
