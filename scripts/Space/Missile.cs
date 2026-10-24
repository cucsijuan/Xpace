using System;
using Godot;
using Xpace.Computer;

namespace Xpace.Space;

/// <summary>
/// A guided missile: a motor, a proximity fuse and a warhead, steered by
/// its own small computer (a bare 8086, <see cref="Microcontroller"/>)
/// running the firmware in software/missile/missile.asm.
///
/// The physics is a point with a fuel budget: the motor gives the
/// acceleration the firmware asks for, up to <see cref="MaxAcceleration"/>,
/// and every m/s it gives comes out of <see cref="DeltaV"/>. The firmware
/// knows the target only from what the launching ship sends over the
/// datalink (<see cref="Uplink"/>): the ship's radar measurement, noise and
/// all. Once armed, the fuse goes off at the closest approach to anything
/// a radar can see if that is within <see cref="FuseRadius"/>. The warhead
/// throws fragments: within <see cref="LethalRadius"/> they do
/// <see cref="Damage"/>, further out they thin with the square of the
/// distance (none beyond <see cref="BlastRadius"/>), so a near miss hurts
/// and only a close one kills.
///
/// A heat seeker (<see cref="HeatSeeker"/>) has no datalink: its head
/// (<see cref="SeekerHead"/>) follows the centre of the heat in its cone,
/// and the firmware is told where the head points. It leaves the rail
/// looking where the launcher's head looked; if that saw nothing, it looks
/// ahead and follows the first heat that comes into view. A flare in the
/// cone beside the target pulls the centre to itself; the sun in the cone
/// outshines everything.
///
/// The firmware's ports (base 100h): +0 type 9  +1 datalink counter (a
/// heat seeker: counts up while the head sees heat)  +2 +4 +6 the target
/// from the missile (m), or for a heat seeker the way its head points
/// (1/16384, always)  +8 +10 +12 acceleration wanted
/// (1/16 m/s²)  +14 status  +16 fuel (m/s)  +18 time to go (1/8 s)
/// +20 +22 +24 accelerometers (1/16 m/s²). The axes are the world's.
/// </summary>
public partial class Missile : Node3D
{
    public const byte Type = 9;
    public const byte StatusGuiding = 1, StatusNoWord = 2, StatusLost = 4;
    public const int DirectionScale = 16384;

    public enum Outcome { Flying, Hit, Missed }

    [Export] public float MaxAcceleration = 200f;      // m/s², about 20 g
    [Export] public float DeltaV = 1500f;              // m/s the fuel gives in all
    [Export] public float ArmSeconds = 0.5f;
    [Export] public float LifeSeconds = 30f;
    [Export] public float FuseRadius = 12f;
    [Export] public float LethalRadius = 6f;
    [Export] public float BlastRadius = 25f;
    [Export] public float Damage = 150f;               // within LethalRadius
    [Export] public uint InstructionsPerSecond = 400_000;
    [Export(PropertyHint.File, "*.bin")] public string FirmwarePath = "res://disks/missile.bin";

    [ExportGroup("Heat seeker")]
    [Export] public bool HeatSeeker;
    [Export] public float SeekerFieldDegrees = 8f;     // the whole cone it looks at
    [Export] public float SeekerSensitivity = 1e-6f;   // the least heat it notices
    [Export] public float SeekerSlewDegrees = 60f;     // how fast the gimbal turns, per second
    [Export] public float GimbalLimitDegrees = 50f;    // how far off the nose it can look

    public Vector3 Velocity { get; set; }
    /// <summary>Where a heat seeker's head points, world axes (the launcher aims it first).</summary>
    public Vector3 SeekerLook { get => _head.Look; set => _head.Look = value; }
    /// <summary>The brightest thing in the head's view this step (null: nothing it notices).</summary>
    public IHeatSource SeekerSees => _head.Sees;
    /// <summary>What the datalink speaks of (set by the launcher).</summary>
    public IRadarTarget Target { get; set; }
    public float FuelLeft { get; private set; }
    public Outcome Result { get; private set; } = Outcome.Flying;
    /// <summary>What the firmware reports: guiding, no word from the ship, lost.</summary>
    public byte Status { get; private set; }
    /// <summary>The firmware's time to go, seconds (negative: it does not know).</summary>
    public float TimeToGo { get; private set; } = -1;
    public float Age { get; private set; }
    /// <summary>The motor is pushing: its plume is what a missile warner sees.</summary>
    public bool Burning => _applied.LengthSquared() > 1;
    /// <summary>The radar track it was fired on (<see cref="Ship.RadarDevice.TrackNumber"/>).</summary>
    public int TrackNumber { get; set; }
    /// <summary>The closest it came to its target.</summary>
    public float ClosestApproach { get; private set; } = float.MaxValue;
    /// <summary>What the warhead did to its target.</summary>
    public float DamageDealt { get; private set; }
    public bool FirmwareFailed => _mcu?.Failed ?? false;

    private Microcontroller _mcu;
    private Vector3? _uplink;
    private byte _linkCounter;
    private Vector3 _applied;
    private MeshInstance3D _flame;
    private OmniLight3D _glow;
    private readonly SeekerHead _head = new();

    private static readonly System.Collections.Generic.Dictionary<string, byte[]> _firmware = new();

    public override void _Ready()
    {
        FuelLeft = DeltaV;
        AddToGroup("missiles");
        if (!_firmware.TryGetValue(FirmwarePath, out byte[] firmware))
        {
            firmware = Godot.FileAccess.GetFileAsBytes(FirmwarePath);
            _firmware[FirmwarePath] = firmware;
            if (firmware.Length == 0)
                GD.PushError($"Missile: cannot read {FirmwarePath} (build it: see native/CMakeLists.txt)");
        }
        _mcu = new Microcontroller(firmware) { InstructionsPerSecond = InstructionsPerSecond };
        _mcu[0] = Type;
        SetUpHead(_head);
        BuildModel();
    }

    public override void _ExitTree() => _mcu?.Dispose();

    private void BuildModel()
    {
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.78f), Roughness = 0.5f };
        var fire = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.75f, 0.4f),
        };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 2.6f }, MaterialOverride = hull, RotationDegrees = new Vector3(90, 0, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.12f, Height = 0.4f }, MaterialOverride = hull, RotationDegrees = new Vector3(-90, 0, 0), Position = new Vector3(0, 0, -1.5f) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.03f, 0.4f) }, MaterialOverride = hull, Position = new Vector3(0, 0, 1.1f) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.7f, 0.4f) }, MaterialOverride = hull, Position = new Vector3(0, 0, 1.1f) });
        _flame = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.25f, Height = 1.5f }, MaterialOverride = fire, Position = new Vector3(0, 0, 1.7f), Visible = false };
        AddChild(_flame);
        _glow = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 0, OmniRange = 30f };
        AddChild(_glow);
    }

    /// <summary>A datalink message: where the ship's radar puts the target, from this missile.</summary>
    public void Uplink(Vector3 targetFromMissile)
    {
        if (!HeatSeeker)
            _uplink = targetFromMissile;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Result != Outcome.Flying)
            return;
        float dt = (float)delta;
        Age += dt;

        if (HeatSeeker)
            Seek(dt);
        if (_uplink is Vector3 r)
        {
            _mcu.SetWord(2, (int)MathF.Round(r.X));
            _mcu.SetWord(4, (int)MathF.Round(r.Y));
            _mcu.SetWord(6, (int)MathF.Round(r.Z));
            _mcu[1] = ++_linkCounter;
            _uplink = null;
        }
        _mcu.SetWord(16, (int)FuelLeft);
        _mcu.SetWord(20, (int)MathF.Round(_applied.X * 16));
        _mcu.SetWord(22, (int)MathF.Round(_applied.Y * 16));
        _mcu.SetWord(24, (int)MathF.Round(_applied.Z * 16));
        _mcu.Tick(delta);

        Status = _mcu[14];
        ushort tgo = (ushort)_mcu.Word(18);
        TimeToGo = tgo == 0xFFFF ? -1 : tgo / 8f;

        // The motor: what is asked, up to its top acceleration, while the fuel lasts
        Vector3 wanted = new Vector3(_mcu.Word(8), _mcu.Word(10), _mcu.Word(12)) / 16f;
        if (wanted.Length() > MaxAcceleration)
            wanted = wanted.Normalized() * MaxAcceleration;
        float spend = wanted.Length() * dt;
        if (spend > FuelLeft)
            wanted *= spend > 0 ? FuelLeft / spend : 0;
        FuelLeft = MathF.Max(0, FuelLeft - wanted.Length() * dt);
        _applied = wanted;
        Velocity += _applied * dt;

        if (Age >= ArmSeconds && Fuse(dt))
            return;
        GlobalPosition += Velocity * dt;
        Point(dt);

        if (Age >= LifeSeconds)
        {
            Result = Outcome.Missed;            // out of battery: it blows itself up
            Explosion.Spawn(GetParent(), GlobalPosition, 6f, 0.6, 4f);
            QueueFree();
        }
    }

    /// <summary>
    /// The proximity fuse, over this step: for everything a radar can see,
    /// the closest it comes while both fly straight. Goes off if that falls
    /// inside the step and within the fuse's reach. Only solid things set it
    /// off: chaff is foil, it does not.
    /// </summary>
    private bool Fuse(float dt)
    {
        float best = float.MaxValue, bestT = 0;
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || !t.Alive || node is not IDamageable)
                continue;
            (float d, float at) = Closest(t, dt);
            if (t == Target)
                ClosestApproach = MathF.Min(ClosestApproach, d);
            if (at < dt && d < best)
            {
                best = d;
                bestT = at;
            }
        }
        if (best > FuseRadius)
            return false;

        Vector3 burst = GlobalPosition + Velocity * bestT;
        foreach (Node node in GetTree().GetNodesInGroup("radar_targets"))
        {
            if (node is not IRadarTarget t || !t.Alive || node is not IDamageable victim)
                continue;
            float d = (t.GlobalPosition + t.Velocity * bestT).DistanceTo(burst);
            if (d >= BlastRadius)
                continue;
            float r = MathF.Max(d, LethalRadius) / LethalRadius;
            float damage = Damage / (r * r);
            victim.Hit(damage);
            if (t == Target)
                DamageDealt = damage;
        }
        Result = Outcome.Hit;
        Explosion.Spawn(GetParent(), burst, 14f, 0.8, 10f);
        QueueFree();
        return true;
    }

    /// <summary>Gives a head this missile's seeker (the launcher builds the one on its rail with it).</summary>
    public void SetUpHead(SeekerHead head)
    {
        head.FieldDegrees = SeekerFieldDegrees;
        head.Sensitivity = SeekerSensitivity;
        head.SlewDegrees = SeekerSlewDegrees;
        head.GimbalLimitDegrees = GimbalLimitDegrees;
    }

    /// <summary>The head follows the heat; the firmware reads where it points, and a new word when it sees something.</summary>
    private void Seek(float dt)
    {
        bool tracking = _head.Track(GetTree(), GlobalPosition, -GlobalBasis.Z, dt);
        Vector3 look = _head.Look * DirectionScale;     // the gimbal's angles, always there
        _mcu.SetWord(2, (int)MathF.Round(look.X));
        _mcu.SetWord(4, (int)MathF.Round(look.Y));
        _mcu.SetWord(6, (int)MathF.Round(look.Z));
        if (tracking)
            _mcu[1] = ++_linkCounter;
    }

    /// <summary>The closest distance to t over the next dt seconds, and when.</summary>
    private (float, float) Closest(IRadarTarget t, float dt)
    {
        Vector3 p = t.GlobalPosition - GlobalPosition;
        Vector3 v = t.Velocity - Velocity;
        float vv = v.LengthSquared();
        float at = vv < 1e-6f ? 0 : Math.Clamp(-p.Dot(v) / vv, 0, dt);
        return ((p + v * at).Length(), at);
    }

    /// <summary>The nose along the push while the motor burns, along the flight when it does not.</summary>
    private void Point(float dt)
    {
        float push = _applied.Length();
        _flame.Visible = push > 1;
        _flame.Scale = Vector3.One * Math.Clamp(push / MaxAcceleration, 0.3f, 1f);
        _glow.LightEnergy = push > 1 ? 3f * push / MaxAcceleration : 0;
        Vector3 nose = push > 1 ? _applied : Velocity;
        if (nose.LengthSquared() < 1e-4f)
            return;
        Vector3 forward = -GlobalBasis.Z;
        Vector3 to = forward.Slerp(nose.Normalized(), Math.Clamp(dt * 10, 0, 1));
        Vector3 up = MathF.Abs(to.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        LookAt(GlobalPosition + to, up);
    }
}
