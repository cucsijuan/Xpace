using Godot;

namespace Xpace.Space;

/// <summary>
/// A target drone: flies between random points around the player's ship
/// (or straight on), takes hits, blows up and comes back a while later.
/// It jinks: gentle weaves while nobody has it locked, hard and frequent
/// turns (up to <see cref="EvadeG"/>) once a radar locks it or rounds hit it. Its brain is plain game
/// code: no computer is emulated for it.
///
/// Its missile warner sees the plume of a burning motor out to
/// <see cref="WarningRange"/>: a missile due within <see cref="BreakSeconds"/>
/// makes it break hard across the missile's line of sight and, while a
/// radar has it locked, drop chaff. The chaff leaves with the drone's own
/// velocity, so it is the break that pulls the two apart. A missile that
/// has burnt out coasts unseen. It drops flares too, against heat seekers:
/// its warner cannot tell one missile from another, so it drops them
/// against any.
///
/// Its motor is hot: brightest from behind, where the nozzle is, dimmer
/// from the side and the front.
///
/// With <see cref="Attacks"/> it hunts what its radar (a
/// <see cref="RadarEmitter"/> below it) hunts: inside
/// <see cref="EngageRange"/> it turns to put it on the nose, too close it
/// flies on past and comes round again. Once its radar has the target
/// locked it points the heat seeker's head on its rail there; if the head
/// holds the heat for <see cref="ToneSeconds"/>, in range, it fires. A
/// target flying cold, nose on, shows it little heat.
/// </summary>
public partial class Drone : Node3D, IRadarTarget, IDamageable, IOriginShift, IHeatSource
{

    [Export] public float Speed = 120f;
    [Export] public float TurnRate = 0.5f;              // rad/s
    [Export] public float Health = 100f;
    [Export] public bool Straight;                      // no turning: a test target
    [Export] public float WanderRadius = 2500f;
    [Export] public float RespawnSeconds = 4f;
    [Export] public NodePath AnchorPath;                // what it flies around
    [Export] public float RadarCrossSection { get; set; } = 3f;   // m², about a light fighter's
    [Export] public bool Evades = true;
    [Export] public float WeaveG = 0.5f, EvadeG = 2f;
    [Export] public float MotorHeat = 50f;              // seen from straight behind
    [Export] public float FrontHeat = 0.3f;             // the share of it seen from ahead or the side

    [ExportGroup("Countermeasures")]
    [Export] public bool Defends = true;
    [Export] public float WarningRange = 4000f;         // m: how far its warner sees a burning motor
    [Export] public float BreakSeconds = 5f;            // it reacts to a missile due within this
    [Export] public float BreakG = 3f;
    [Export] public int ChaffCharges = 6;
    [Export] public double ChaffInterval = 1.5;
    [Export] public float ChaffKick = 15f;              // m/s the dispenser throws a bundle
    [Export] public PackedScene ChaffScene;
    [Export] public int FlareCharges = 6;
    [Export] public double FlareInterval = 1.0;
    [Export] public float FlareKick = 20f;
    [Export] public PackedScene FlareScene;

    [ExportGroup("Attack")]
    [Export] public bool Attacks;
    [Export] public float EngageRange = 3500f;          // it turns on its radar's target inside this
    [Export] public float BreakOffRange = 500f;         // closer than this it flies on past
    [Export] public double ExtendSeconds = 8;           // and comes round again after this
    [Export] public PackedScene MissileScene;           // a heat seeker
    [Export] public int MissileCharges = 2;
    [Export] public float MissileRange = 2500f, MinMissileRange = 600f;
    [Export] public double MissileInterval = 12;
    [Export] public double ToneSeconds = 0.5;           // the head holds the heat this long before it fires

    public ushort RadarId { get; private set; }
    public Vector3 Velocity { get; private set; }
    public bool Alive { get; private set; } = true;
    public int Hits { get; private set; }
    public bool Painted => _painted > 0;
    /// <summary>Locked or recently hit: it fights for its life.</summary>
    public bool Threatened => _painted > 0 || _hitRecently > 0;
    /// <summary>Its warner sees a missile coming, this step.</summary>
    public bool MissileWarning { get; private set; }
    public int ChaffLeft { get; private set; }
    public int FlaresLeft { get; private set; }
    public int MissilesLeft { get; private set; }
    public int Launched { get; private set; }
    /// <summary>Turning on its target, this step.</summary>
    public bool Attacking { get; private set; }
    /// <summary>The head on its rail sees heat.</summary>
    public bool HeadTone => _head.Tone;

    private Node3D _anchor, _body;
    private Vector3 _waypoint;
    private float _health;
    private double _respawn;
    private StandardMaterial3D _paint;
    private Vector3 _jink;
    private double _jinkLeft, _painted, _hitRecently, _chaffWait, _flareWait;
    private Vector3 _breakSide;
    private RadarEmitter _radar;
    private IRadarTarget _quarry;
    private readonly SeekerHead _head = new();
    private double _toneFor, _missileWait, _extend;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        RadarId = RadarIds.Next();
        _rng.Seed = RadarId * 7919UL;
        AddToGroup("radar_targets");
        AddToGroup("heat_sources");
        _anchor = AnchorPath == null || AnchorPath.IsEmpty ? null : GetNodeOrNull<Node3D>(AnchorPath);
        _health = Health;
        ChaffLeft = ChaffCharges;
        FlaresLeft = FlareCharges;
        MissilesLeft = MissileCharges;
        foreach (Node child in GetChildren())
            _radar ??= child as RadarEmitter;
        if (MissileScene != null)
        {
            Missile pattern = MissileScene.Instantiate<Missile>();
            pattern.SetUpHead(_head);
            pattern.Free();
        }
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

        // The hitbox is the drone's shape, fuselage and wings: rounds through
        // the empty space between them miss
        var hitbox = new Area3D { Monitoring = false, CollisionLayer = Hitboxes.RoundLayer, CollisionMask = 0 };
        hitbox.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.5f, 1.6f, 8f) } });
        hitbox.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(11f, 0.3f, 3f) }, Position = new Vector3(0, 0, 1.5f) });
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
        _painted -= delta;
        _hitRecently -= delta;
        _chaffWait -= delta;
        _flareWait -= delta;
        _missileWait -= delta;
        _extend -= delta;
        Missile threat = Defends ? Incoming() : null;
        MissileWarning = threat != null;
        if (threat != null)
        {
            Break(threat, dt);
            DropChaff();
            DropFlare();
        }
        else
        {
            _breakSide = Vector3.Zero;
            if (Evades)
                Jink(dt);
        }
        Node3D quarry = Attacks && _radar != null && IsInstanceValid(_radar.Target) ? _radar.Target : null;
        Attacking = false;
        if (quarry != null && threat == null && _extend <= 0)
        {
            float range = GlobalPosition.DistanceTo(quarry.GlobalPosition);
            if (range < BreakOffRange)
            {
                _extend = ExtendSeconds;            // on past it, then round again
                _waypoint = GlobalPosition + Velocity.Normalized() * 2500;
            }
            else
                Attacking = range < EngageRange;
        }
        if (!Straight)
        {
            if (!Attacking && GlobalPosition.DistanceTo(_waypoint) < 300)
                _waypoint = NewWaypoint();
            Vector3 heading = Velocity.Normalized();
            Vector3 wanted = ((Attacking ? quarry.GlobalPosition : _waypoint) - GlobalPosition).Normalized();
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
        if (quarry != null)
            Aim(quarry, dt);
    }

    /// <summary>
    /// The head on the rail: on the radar's target while the radar holds a
    /// lock, along the nose otherwise. Heat held long enough, in range: a
    /// missile goes.
    /// </summary>
    private void Aim(Node3D quarry, float dt)
    {
        if (MissileScene == null)
            return;
        Vector3 nose = -GlobalBasis.Z;
        if (_head.Look.AngleTo(nose) > Mathf.DegToRad(_head.GimbalLimitDegrees))
            _head.Look = nose;                      // against the stops: it rests caged on the nose
        Vector3 to = _radar.Locked ? quarry.GlobalPosition - GlobalPosition : nose;
        _head.Point(GetTree(), GlobalPosition, nose, to, dt);
        _toneFor = _radar.Locked && _head.Tone ? _toneFor + dt : 0;
        float range = GlobalPosition.DistanceTo(quarry.GlobalPosition);
        if (_toneFor < ToneSeconds || MissilesLeft <= 0 || _missileWait > 0 || range > MissileRange || range < MinMissileRange)
            return;
        Missile m = MissileScene.Instantiate<Missile>();
        m.Shooter = this;
        m.Target = _quarry ??= Signature(quarry);
        m.SeekerLook = _head.Look;
        GetParent().AddChild(m);
        m.GlobalTransform = GlobalTransform;
        m.GlobalPosition = GlobalPosition + nose * 6 - GlobalBasis.Y * 1.5f;
        m.Velocity = Velocity + nose * 20;
        MissilesLeft--;
        Launched++;
        _missileWait = MissileInterval;
        _toneFor = 0;
    }

    /// <summary>What of the target a radar sees (the target, or something below it that is a radar target).</summary>
    private static IRadarTarget Signature(Node3D target)
    {
        if (target is IRadarTarget t)
            return t;
        foreach (Node child in target.GetChildren())
            if (child is IRadarTarget c)
                return c;
        return null;
    }

    public void ShiftOrigin(Vector3 offset) => _waypoint -= offset;

    /// <summary>Sets it flying with this velocity from where it is (a test, a mission).</summary>
    public void Fly(Vector3 velocity)
    {
        Velocity = velocity;
        _waypoint = GlobalPosition + velocity * 10;
        _jinkLeft = 0;
    }

    /// <summary>A radar has it locked (the radar calls this every step it does).</summary>
    public void Paint() => _painted = 0.25;

    /// <summary>A sideways push that changes every second or so: the speed stays, the path bends.</summary>
    private void Jink(float dt)
    {
        _jinkLeft -= dt;
        Vector3 heading = Velocity.Normalized();
        if (_jinkLeft <= 0)
        {
            _jinkLeft = Threatened ? _rng.RandfRange(0.5f, 1.4f) : _rng.RandfRange(0.6f, 1.6f);
            Vector3 side = heading.Cross(Vector3.Up);
            if (side.LengthSquared() < 1e-6f)
                side = heading.Cross(Vector3.Right);
            side = side.Normalized().Rotated(heading, _rng.RandfRange(0, Mathf.Tau));
            _jink = side * (Threatened ? EvadeG : WeaveG) * 9.81f * _rng.RandfRange(0.5f, 1f);
        }
        Vector3 push = _jink - heading * _jink.Dot(heading);
        Velocity = (Velocity + push * dt).Normalized() * Speed;
    }

    /// <summary>The burning missile that would reach it soonest, if within BreakSeconds.</summary>
    private Missile Incoming()
    {
        Missile soonest = null;
        float best = BreakSeconds;
        foreach (Node node in GetTree().GetNodesInGroup("missiles"))
        {
            if (node is not Missile m || !m.Burning)
                continue;
            Vector3 toMe = GlobalPosition - m.GlobalPosition;
            float range = toMe.Length();
            if (range > WarningRange || range < 1)
                continue;
            float closing = (m.Velocity - Velocity).Dot(toMe / range);
            if (closing > 0 && range / closing < best)
            {
                best = range / closing;
                soonest = m;
            }
        }
        return soonest;
    }

    /// <summary>A hard turn across the missile's line of sight, the same way until the danger is past.</summary>
    private void Break(Missile m, float dt)
    {
        Vector3 heading = Velocity.Normalized();
        if (_breakSide == Vector3.Zero)
        {
            Vector3 los = (GlobalPosition - m.GlobalPosition).Normalized();
            Vector3 side = los.Cross(heading);
            if (side.LengthSquared() < 1e-4f)
                side = heading.Cross(Vector3.Up);
            _breakSide = side.Normalized() * (_rng.Randf() < 0.5f ? -1 : 1);
        }
        Vector3 push = _breakSide * BreakG * 9.81f;
        push -= heading * push.Dot(heading);
        Velocity = (Velocity + push * dt).Normalized() * Speed;
    }

    /// <summary>A bundle of chaff behind it, every ChaffInterval while a radar holds it.</summary>
    private void DropChaff()
    {
        if (!Painted || ChaffLeft <= 0 || _chaffWait > 0)
            return;
        _chaffWait = ChaffInterval;
        ChaffLeft--;
        ChaffCloud chaff = ChaffScene?.Instantiate<ChaffCloud>() ?? new ChaffCloud();
        GetParent().AddChild(chaff);
        Vector3 heading = Velocity.Normalized();
        chaff.GlobalPosition = GlobalPosition - heading * 5;
        var scatter = new Vector3(_rng.Randfn(), _rng.Randfn(), _rng.Randfn()).Normalized();
        chaff.Velocity = Velocity + (scatter - heading).Normalized() * ChaffKick;
    }

    /// <summary>A flare behind it, every FlareInterval while a missile comes.</summary>
    private void DropFlare()
    {
        if (FlaresLeft <= 0 || _flareWait > 0)
            return;
        _flareWait = FlareInterval;
        FlaresLeft--;
        Flare flare = FlareScene?.Instantiate<Flare>() ?? new Flare();
        GetParent().AddChild(flare);
        Vector3 heading = Velocity.Normalized();
        flare.GlobalPosition = GlobalPosition - heading * 5;
        var scatter = new Vector3(_rng.Randfn(), _rng.Randfn(), _rng.Randfn()).Normalized();
        flare.Velocity = Velocity + (scatter - heading).Normalized() * FlareKick;
    }

    /// <summary>A full dispenser again (a test, a resupply).</summary>
    public void Rearm()
    {
        ChaffLeft = ChaffCharges;
        FlaresLeft = FlareCharges;
        MissilesLeft = MissileCharges;
    }

    public float HeatAt(Vector3 from, out Vector3 direction)
    {
        if (!Alive)
        {
            direction = Vector3.Zero;
            return 0;
        }
        float heat = Heat.Point(GlobalPosition, MotorHeat, from, out direction);
        float behind = Mathf.Max(0, -direction.Dot(GlobalBasis.Z.Normalized()));
        return heat * (FrontHeat + (1 - FrontHeat) * behind);
    }

    private Vector3 Centre => _anchor?.GlobalPosition ?? Vector3.Zero;

    private Vector3 NewWaypoint()
    {
        var direction = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-1, 1)).Normalized();
        return Centre + direction * _rng.RandfRange(0.4f, 1f) * WanderRadius;
    }

    /// <summary>A round or a blast hit it.</summary>
    public void Hit(float damage)
    {
        if (!Alive)
            return;
        Hits++;
        _hitRecently = 3;
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
        Explosion.Spawn(GetParent(), GlobalPosition);
    }

    private void Respawn()
    {
        _health = Health;
        Rearm();
        Alive = true;
        _body.Visible = true;
        var direction = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-1, 1)).Normalized();
        GlobalPosition = Centre + direction * 3500f;
        _waypoint = NewWaypoint();
        Velocity = (_waypoint - GlobalPosition).Normalized() * Speed;
    }
}
