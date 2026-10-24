using System.Collections.Generic;
using Godot;
using Xpace.Net;

namespace Xpace.Space;

/// <summary>
/// Every round in flight: moved each physics step, checked for hits with a
/// ray along the step, drawn as streaks, a MultiMesh for each kind of round
/// (<see cref="Looks"/>: 0 the cannon's tracers, 1 laser pulses).
///
/// In a network session the host judges the rounds: they are fired through
/// the session (<see cref="SessionNode"/>), fly here only for show, and go
/// when the host says they hit (<see cref="Remove"/>).
/// </summary>
public partial class Projectiles : MultiMeshInstance3D, IOriginShift
{
    [Export] public int MaxRounds = 512;
    [Export] public float Life = 4f;
    /// <summary>How each kind of round looks, by kind; a kind with none looks like the first.</summary>
    [Export] public Godot.Collections.Array<RoundLook> Looks = new();

    public static Projectiles Instance { get; private set; }

    private struct Round
    {
        public Vector3 Position, Velocity;
        public float Damage, Age;
        public bool Harmless;               // for show: the session's host judges it
        public byte Kind;
        public byte Shooter;
        public ushort Sequence;
        public Godot.Collections.Array<Rid> Own;    // the shooter's own areas: a round never hits its own ship
    }

    private readonly List<Round> _rounds = new();
    private readonly List<MultiMeshInstance3D> _drawers = new();
    private readonly List<int> _counts = new();

    public int Flying => _rounds.Count;

    public override void _Ready()
    {
        Instance = this;
        TopLevel = true;
        if (Looks.Count == 0)
            Looks.Add(new RoundLook());
        for (int kind = 0; kind < Looks.Count; kind++)
        {
            MultiMeshInstance3D drawer = kind == 0 ? this : new MultiMeshInstance3D { Name = $"Kind{kind}", TopLevel = true };
            RoundLook look = Looks[kind] ?? Looks[0];
            drawer.Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh
                {
                    Size = new Vector3(1f, 1f, 1f),
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        AlbedoColor = look.Colour,
                        EmissionEnabled = true,
                        Emission = look.Glow,
                        EmissionEnergyMultiplier = look.GlowEnergy,
                    },
                },
                InstanceCount = MaxRounds,
                VisibleInstanceCount = 0,
            };
            drawer.CastShadow = ShadowCastingSetting.Off;
            drawer.CustomAabb = new Aabb(-Vector3.One * 50_000, Vector3.One * 100_000);    // rounds go anywhere: never cull
            if (drawer != this)
                AddChild(drawer);
            _drawers.Add(drawer);
            _counts.Add(0);
        }
    }

    private RoundLook Look(byte kind) => kind < Looks.Count && Looks[kind] != null ? Looks[kind] : Looks[0];

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ShiftOrigin(Vector3 offset)
    {
        for (int i = 0; i < _rounds.Count; i++)
        {
            Round r = _rounds[i];
            r.Position -= offset;
            _rounds[i] = r;
        }
    }

    /// <summary>A round of a <paramref name="kind"/> leaves a muzzle of <paramref name="shooter"/> (it is never hit by its own round).</summary>
    public void Fire(Vector3 origin, Vector3 velocity, float damage, Node shooter = null, byte kind = 0)
    {
        if (SessionNode.Instance is SessionNode session && session.Active)
            session.Fire(origin, velocity, damage, shooter, kind);
        else if (_rounds.Count < MaxRounds)
            _rounds.Add(new Round { Position = origin, Velocity = velocity, Damage = damage, Kind = kind, Own = Areas(shooter) });
    }

    /// <summary>A round that only flies for show, named by its shooter and sequence.</summary>
    public void Show(Vector3 position, Vector3 velocity, float age, byte shooter, ushort sequence, byte kind = 0)
    {
        if (_rounds.Count < MaxRounds && age < Life)
            _rounds.Add(new Round
            {
                Position = position, Velocity = velocity, Age = age, Harmless = true, Kind = kind, Shooter = shooter, Sequence = sequence,
            });
    }

    /// <summary>The host says this round hit: it goes.</summary>
    public void Remove(byte shooter, ushort sequence) =>
        _rounds.RemoveAll(r => r.Harmless && r.Shooter == shooter && r.Sequence == sequence);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            Round r = _rounds[i];
            Vector3 next = r.Position + r.Velocity * dt;
            r.Age += dt;
            if (r.Harmless)
            {
                if (r.Age > Life)
                    _rounds.RemoveAt(i);
                else
                {
                    r.Position = next;
                    _rounds[i] = r;
                }
                continue;
            }
            var query = PhysicsRayQueryParameters3D.Create(r.Position, next, Hitboxes.RoundLayer, r.Own);
            query.CollideWithAreas = true;
            query.CollideWithBodies = false;
            var hit = space.IntersectRay(query);
            if (hit.Count > 0 && Damageable(hit["collider"].As<Node>()) is IDamageable victim)
            {
                victim.Hit(r.Damage);
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

    private static Godot.Collections.Array<Rid> Areas(Node shooter)
    {
        var own = new Godot.Collections.Array<Rid>();
        if (shooter == null)
            return own;
        if (shooter is CollisionObject3D self)
            own.Add(self.GetRid());
        foreach (Node n in shooter.FindChildren("*", "", true, false))
            if (n is CollisionObject3D o)
                own.Add(o.GetRid());
        return own;
    }

    /// <summary>What takes the damage for a hitbox: the nearest damageable thing above it.</summary>
    private static IDamageable Damageable(Node n)
    {
        for (; n != null; n = n.GetParent())
            if (n is IDamageable d)
                return d;
        return null;
    }

    public override void _Process(double delta)
    {
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        for (int k = 0; k < _counts.Count; k++)
            _counts[k] = 0;
        foreach (Round r in _rounds)
        {
            int k = r.Kind < _drawers.Count ? r.Kind : 0;
            RoundLook look = Look(r.Kind);
            Vector3 dir = r.Velocity.Normalized();
            float width = Mathf.Max(look.MinWidth, r.Position.DistanceTo(eye) * look.WidthPerMetre);
            Basis basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up)
                * Basis.FromScale(new Vector3(width, width, look.Length));
            _drawers[k].Multimesh.SetInstanceTransform(_counts[k]++, new Transform3D(basis, r.Position - dir * look.Length / 2));
        }
        for (int k = 0; k < _drawers.Count; k++)
            _drawers[k].Multimesh.VisibleInstanceCount = _counts[k];
    }
}
