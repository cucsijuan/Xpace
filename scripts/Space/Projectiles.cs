using System.Collections.Generic;
using Godot;

namespace Xpace.Space;

/// <summary>
/// Every round in flight: moved each physics step, checked for hits with a
/// ray along the step, drawn as tracers in one MultiMesh.
/// </summary>
public partial class Projectiles : MultiMeshInstance3D
{
    [Export] public int MaxRounds = 512;
    [Export] public float Life = 4f;
    [Export] public float TracerLength = 40f;
    /// <summary>Tracer width per metre of distance from the camera: about the same few pixels near or far.</summary>
    [Export] public float WidthPerMetre = 0.0025f;

    public static Projectiles Instance { get; private set; }

    private struct Round
    {
        public Vector3 Position, Velocity;
        public float Damage, Age;
    }

    private readonly List<Round> _rounds = new();

    public override void _Ready()
    {
        Instance = this;
        TopLevel = true;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh
            {
                Size = new Vector3(1f, 1f, 1f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(1f, 0.75f, 0.35f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.6f, 0.2f),
                    EmissionEnergyMultiplier = 3f,
                },
            },
            InstanceCount = MaxRounds,
            VisibleInstanceCount = 0,
        };
        CastShadow = ShadowCastingSetting.Off;
        CustomAabb = new Aabb(-Vector3.One * 50_000, Vector3.One * 100_000);    // rounds go anywhere: never cull
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Fire(Vector3 origin, Vector3 velocity, float damage)
    {
        if (_rounds.Count < MaxRounds)
            _rounds.Add(new Round { Position = origin, Velocity = velocity, Damage = damage });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            Round r = _rounds[i];
            Vector3 next = r.Position + r.Velocity * dt;
            var query = PhysicsRayQueryParameters3D.Create(r.Position, next, Drone.HitLayer);
            query.CollideWithAreas = true;
            query.CollideWithBodies = false;
            var hit = space.IntersectRay(query);
            r.Age += dt;
            if (hit.Count > 0 && hit["collider"].As<Node>()?.GetParent()?.GetParent() is Drone drone)
            {
                drone.Hit(r.Damage);
                _rounds.RemoveAt(i);
                continue;
            }
            if (r.Age > Life)
            {
                _rounds.RemoveAt(i);
                continue;
            }
            r.Position = next;
            _rounds[i] = r;
        }
    }

    public override void _Process(double delta)
    {
        MultiMesh mm = Multimesh;
        mm.VisibleInstanceCount = _rounds.Count;
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        for (int i = 0; i < _rounds.Count; i++)
        {
            Round r = _rounds[i];
            Vector3 dir = r.Velocity.Normalized();
            float width = Mathf.Max(0.15f, r.Position.DistanceTo(eye) * WidthPerMetre);
            Basis basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up)
                * Basis.FromScale(new Vector3(width, width, TracerLength));
            mm.SetInstanceTransform(i, new Transform3D(basis, r.Position - dir * TracerLength / 2));
        }
    }
}
