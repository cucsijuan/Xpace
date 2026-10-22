using Godot;

namespace Xpace.Space;

/// <summary>Something a radar can see.</summary>
public interface IRadarTarget
{
    ushort RadarId { get; }
    Vector3 GlobalPosition { get; }
    Vector3 Velocity { get; }
    bool Alive { get; }
}

/// <summary>
/// A target drone: flies between random points around the player's ship
/// (or straight on), takes hits, blows up and comes back a while later.
/// Its brain is plain game code: no computer is emulated for it.
/// </summary>
public partial class Drone : Node3D, IRadarTarget
{
    public const uint HitLayer = 2;                     // what rounds look for

    [Export] public float Speed = 120f;
    [Export] public float TurnRate = 0.5f;              // rad/s
    [Export] public float Health = 100f;
    [Export] public bool Straight;                      // no turning: a test target
    [Export] public float WanderRadius = 2500f;
    [Export] public float RespawnSeconds = 4f;
    [Export] public NodePath AnchorPath;                // what it flies around

    public ushort RadarId { get; private set; }
    public Vector3 Velocity { get; private set; }
    public bool Alive { get; private set; } = true;
    public int Hits { get; private set; }

    private static ushort _nextId = 1;
    private Node3D _anchor, _body;
    private Vector3 _waypoint;
    private float _health;
    private double _respawn;
    private StandardMaterial3D _paint;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        RadarId = _nextId++;
        _rng.Seed = RadarId * 7919UL;
        AddToGroup("radar_targets");
        _anchor = AnchorPath == null || AnchorPath.IsEmpty ? null : GetNodeOrNull<Node3D>(AnchorPath);
        _health = Health;
        Velocity = -GlobalBasis.Z * Speed;
        _waypoint = GlobalPosition + Velocity * 10;
        BuildModel();
    }

    private void BuildModel()
    {
        _body = new Node3D();
        AddChild(_body);
        _paint = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.3f, 0.2f), Roughness = 0.6f };
        var light = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.3f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.25f, 0.15f),
            EmissionEnergyMultiplier = 4f,
        };
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.5f, 1.6f, 8f) }, MaterialOverride = _paint });
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(11f, 0.3f, 3f) }, MaterialOverride = _paint, Position = new Vector3(0, 0, 1.5f) });
        _body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f }, MaterialOverride = light, Position = new Vector3(0, 0, -4.2f) });

        var hitbox = new Area3D { Monitoring = false, CollisionLayer = HitLayer, CollisionMask = 0 };
        hitbox.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 6f } });
        _body.AddChild(hitbox);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Alive)
        {
            _respawn -= delta;
            if (_respawn <= 0)
                Respawn();
            return;
        }
        if (!Straight)
        {
            if (GlobalPosition.DistanceTo(_waypoint) < 300)
                _waypoint = NewWaypoint();
            Vector3 heading = Velocity.Normalized();
            Vector3 wanted = (_waypoint - GlobalPosition).Normalized();
            float angle = heading.AngleTo(wanted);
            if (angle > 0.001f)
            {
                Vector3 axis = heading.Cross(wanted);
                if (axis.LengthSquared() < 1e-8f)
                    axis = Vector3.Up;
                heading = heading.Rotated(axis.Normalized(), Mathf.Min(angle, TurnRate * dt));
            }
            Velocity = heading * Speed;
        }
        GlobalPosition += Velocity * dt;
        if (Velocity.LengthSquared() > 1)
            LookAt(GlobalPosition + Velocity, Vector3.Up);
    }

    private Vector3 Centre => _anchor?.GlobalPosition ?? Vector3.Zero;

    private Vector3 NewWaypoint()
    {
        var direction = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-1, 1)).Normalized();
        return Centre + direction * _rng.RandfRange(0.4f, 1f) * WanderRadius;
    }

    /// <summary>A round hit it.</summary>
    public void Hit(float damage)
    {
        if (!Alive)
            return;
        Hits++;
        _health -= damage;
        var flash = CreateTween();
        _paint.AlbedoColor = new Color(1f, 0.9f, 0.6f);
        flash.TweenProperty(_paint, "albedo_color", new Color(0.75f, 0.3f, 0.2f), 0.15);
        if (_health <= 0)
            Explode();
    }

    private void Explode()
    {
        Alive = false;
        _body.Visible = false;
        _respawn = RespawnSeconds;
        var fire = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.7f, 0.3f, 1f),
        };
        var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f }, MaterialOverride = fire };
        var glow = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 12f, OmniRange = 120f };
        GetParent().AddChild(ball);
        GetParent().AddChild(glow);
        ball.GlobalPosition = glow.GlobalPosition = GlobalPosition;
        var t = ball.CreateTween().SetParallel();
        t.TweenProperty(ball, "scale", Vector3.One * 25f, 1.2);
        t.TweenProperty(fire, "albedo_color", new Color(1f, 0.3f, 0.1f, 0f), 1.2);
        t.TweenProperty(glow, "light_energy", 0f, 1.2);
        t.Chain().TweenCallback(Callable.From(() => { ball.QueueFree(); glow.QueueFree(); }));
    }

    private void Respawn()
    {
        _health = Health;
        Alive = true;
        _body.Visible = true;
        var direction = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-1, 1)).Normalized();
        GlobalPosition = Centre + direction * 3500f;
        _waypoint = NewWaypoint();
        Velocity = (_waypoint - GlobalPosition).Normalized() * Speed;
    }
}
