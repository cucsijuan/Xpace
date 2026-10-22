using System;
using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// A gimballed turret with a kinetic cannon, bus slot 4 (140h). Servos turn
/// it toward the angles the program asks for, at a limited rate; the gun
/// fires while the program keeps writing the fire register.
///
/// Registers: +0 type 5  +2 azimuth wanted  +4 elevation wanted  +6 azimuth
/// now  +8 elevation now (binary angles, 65536 to the turn; 0 straight
/// ahead, right and up positive)  +10 muzzle speed (m/s)  +12 write 1: fire
/// this step  +13 status: bit 0 ready, bit 1 at a limit, bit 2 too hot
/// +14 heat (0-255)  +15 rounds fired (counts up)
/// </summary>
public partial class TurretDevice : Node3D, IShipDevice
{
    public const int Slot = 4;
    public const byte Type = 5;

    [Export] public float MuzzleSpeed = 1200f;          // m/s
    [Export] public float ServoDegreesPerSecond = 90f;
    [Export] public float AzimuthLimit = 170f;          // degrees either side
    [Export] public float ElevationMin = -5f, ElevationMax = 85f;
    [Export] public double FireInterval = 0.1;
    [Export] public float Damage = 10f;
    [Export] public float HeatPerShot = 7f, CoolingPerSecond = 30f;
    [Export] public float HotAbove = 250f, CoolBelow = 120f;

    /// <summary>Radians, right and up positive.</summary>
    public float Azimuth { get; private set; }
    public float Elevation { get; private set; }
    public int RoundsFired { get; private set; }
    public float Heat { get; private set; }
    public bool TooHot { get; private set; }

    /// <summary>Where the barrel points, in world space.</summary>
    public Vector3 BarrelDirection => -_muzzle.GlobalBasis.Z;

    private Node3D _yoke, _cradle, _muzzle;
    private OmniLight3D _flash;
    private double _cooldown;
    private bool _registersSet;

    public override void _Ready()
    {
        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.37f), Metallic = 0.5f, Roughness = 0.45f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Roughness = 0.6f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.65f, Height = 0.3f }, MaterialOverride = metal });
        _yoke = new Node3D { Position = new Vector3(0, 0.35f, 0) };
        AddChild(_yoke);
        _yoke.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.45f, 0.9f) }, MaterialOverride = metal });
        _cradle = new Node3D();
        _yoke.AddChild(_cradle);
        _cradle.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 2.4f },
            MaterialOverride = dark,
            Position = new Vector3(0, 0, -1.3f),
            RotationDegrees = new Vector3(90, 0, 0),
        });
        _muzzle = new Node3D { Position = new Vector3(0, 0, -2.6f) };
        _cradle.AddChild(_muzzle);
        _flash = new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.35f), LightEnergy = 0, OmniRange = 8f };
        _muzzle.AddChild(_flash);
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        if (!_registersSet)
        {
            _registersSet = true;
            bus[Slot, 0] = Type;
            bus[Slot, 1] = 1;
            bus.SetWord(Slot, 10, (int)MuzzleSpeed);
        }
        bus.SetWord(Slot, 6, Bam(Azimuth));
        bus.SetWord(Slot, 8, Bam(Elevation));
        bool atLimit = MathF.Abs(Azimuth) >= Mathf.DegToRad(AzimuthLimit) - 1e-4f
                       || Elevation <= Mathf.DegToRad(ElevationMin) + 1e-4f || Elevation >= Mathf.DegToRad(ElevationMax) - 1e-4f;
        bus[Slot, 13] = (byte)((TooHot ? 0 : 1) | (atLimit ? 2 : 0) | (TooHot ? 4 : 0));
        bus[Slot, 14] = (byte)Math.Clamp((int)Heat, 0, 255);
        bus[Slot, 15] = (byte)RoundsFired;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        float dt = (float)delta;
        float wantAz = Math.Clamp(Radians(bus.Word(Slot, 2)), -Mathf.DegToRad(AzimuthLimit), Mathf.DegToRad(AzimuthLimit));
        float wantEl = Math.Clamp(Radians(bus.Word(Slot, 4)), Mathf.DegToRad(ElevationMin), Mathf.DegToRad(ElevationMax));
        float step = Mathf.DegToRad(ServoDegreesPerSecond) * dt;
        Azimuth = Mathf.MoveToward(Azimuth, wantAz, step);
        Elevation = Mathf.MoveToward(Elevation, wantEl, step);
        _yoke.Rotation = new Vector3(0, -Azimuth, 0);
        _cradle.Rotation = new Vector3(Elevation, 0, 0);

        Heat = MathF.Max(0, Heat - CoolingPerSecond * dt);
        if (TooHot && Heat < CoolBelow)
            TooHot = false;
        _cooldown -= delta;
        _flash.LightEnergy = MathF.Max(0, _flash.LightEnergy - 60 * dt);

        bool fire = bus.TakeWritten(Slot, 12) && (bus[Slot, 12] & 1) != 0;
        if (fire && _cooldown <= 0 && !TooHot)
        {
            _cooldown = FireInterval;
            Heat += HeatPerShot;
            TooHot = Heat > HotAbove;
            RoundsFired++;
            Vector3 direction = -_muzzle.GlobalBasis.Z;
            Projectiles.Instance?.Fire(_muzzle.GlobalPosition, ship.LinearVelocity + direction * MuzzleSpeed, Damage);
            _flash.LightEnergy = 6;
        }
    }

    private static int Bam(float radians)
    {
        int b = (int)MathF.Round(radians / Mathf.Tau * 65536);
        return Math.Clamp(b, -32767, 32767);
    }

    private static float Radians(short bam) => bam / 65536f * Mathf.Tau;
}
