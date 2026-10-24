using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A cloud of chaff: thin foil strips cut to the radars' wavelength. To a
/// radar it is one more echo; it blooms as the bundle opens and fades as the
/// strips scatter. There is no air to slow it: it keeps the velocity it was
/// dropped with, so it only parts from the ship that dropped it if that
/// ship manoeuvres (or by the little the dispenser kicks it).
/// </summary>
public partial class ChaffCloud : Node3D, IRadarTarget, IDropped
{
    [Export] public float PeakCrossSection = 2.5f;     // m², a bit less than a light fighter
    [Export] public float BloomSeconds = 0.4f;
    [Export] public float LifeSeconds = 5f;
    [Export] public float SpreadSpeed = 4f;            // m/s: how fast the strips drift apart

    public ushort RadarId { get; private set; }
    public Vector3 Velocity { get; set; }
    public bool Alive => Age < LifeSeconds;
    public float Age { get; set; }

    /// <summary>Blooms to its peak, holds, then fades out over the last 60% of its life.</summary>
    public float RadarCrossSection
    {
        get
        {
            float bloom = Math.Clamp(Age / BloomSeconds, 0, 1);
            float fade = 1 - Math.Clamp((Age - LifeSeconds * 0.4f) / (LifeSeconds * 0.6f), 0, 1);
            return PeakCrossSection * bloom * fade;
        }
    }

    private Node3D _strips;
    private StandardMaterial3D _foil;

    public void Paint() { }

    public override void _Ready()
    {
        RadarId = RadarIds.Next();
        AddToGroup("radar_targets");
        _foil = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.85f, 0.9f),
            Metallic = 1f,
            Roughness = 0.2f,
            EmissionEnabled = true,
            Emission = new Color(0.5f, 0.5f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var rng = new RandomNumberGenerator { Seed = RadarId * 104729UL };
        var mesh = new QuadMesh { Size = new Vector2(0.4f, 0.05f) };
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 60 };
        for (int i = 0; i < multi.InstanceCount; i++)
        {
            var place = new Vector3(rng.Randfn(), rng.Randfn(), rng.Randfn()) * 0.4f;
            var turn = new Basis(new Vector3(rng.Randf(), rng.Randf(), rng.Randf()).Normalized(), rng.RandfRange(0, Mathf.Tau));
            multi.SetInstanceTransform(i, new Transform3D(turn, place));
        }
        _strips = new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = _foil };
        AddChild(_strips);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Age += dt;
        GlobalPosition += Velocity * dt;
        _strips.Scale = Vector3.One * (1 + Age * SpreadSpeed);
        _foil.AlbedoColor = _foil.AlbedoColor with { A = Math.Clamp(RadarCrossSection / PeakCrossSection + 0.2f, 0, 1) };
        if (!Alive)
            QueueFree();
    }
}
