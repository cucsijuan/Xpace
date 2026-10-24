using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A round fired, sent reliably by the shooter to the host. A round flies
/// straight in space, so this is all any machine needs to fly it: where
/// and when it left the muzzle, and its velocity (the ship's included).
///
/// It also says how the shooter saw every other ship at that moment
/// (<see cref="Views"/>): how many ticks in the past each one was shown.
/// The host tests the round against each ship as the shooter saw it
/// (lag compensation, see <see cref="LagCompensator"/>).
///
///   shooter 8, sequence 16, tick 32, origin 3 x 32 (1/1024 m),
///   velocity 3 x 17 (±4,096 m/s), damage 16 (1/100), views 3 bits count,
///   then each: ship 8, delay 16 (1/256 tick, up to 4 s)
/// </summary>
public struct FireEvent
{
    public const int MaxViews = 7;
    private const double DelayScale = 256;

    public struct View
    {
        public byte Ship;
        public double DelayTicks;           // how far in the past the shooter showed it
    }

    public byte Shooter;
    public ushort Sequence;                 // the shooter's round count: names the round in hit events
    public uint Tick;
    public SectorPoint Origin;
    public Vector3 Velocity;
    public float Damage;
    public List<View> Views;

    /// <summary>Where the round is <paramref name="ticks"/> after it was fired.</summary>
    public readonly SectorPoint At(double ticks, float tickSeconds) => Origin + Velocity * (float)(ticks * tickSeconds);

    public readonly byte[] ToBytes()
    {
        var w = new BitWriter();
        w.Write(Shooter, 8);
        w.Write(Sequence, 16);
        w.Write(Tick, 32);
        foreach (double x in new[] { Origin.X, Origin.Y, Origin.Z })
            w.WriteSigned((int)Math.Round(Math.Clamp(x * ShipSnapshot.PositionScale, int.MinValue, int.MaxValue)), 32);
        w.WriteScaled(Velocity.X, ShipSnapshot.VelocityRange, 17);
        w.WriteScaled(Velocity.Y, ShipSnapshot.VelocityRange, 17);
        w.WriteScaled(Velocity.Z, ShipSnapshot.VelocityRange, 17);
        w.Write((uint)Math.Clamp(Math.Round(Damage * 100), 0, 65535), 16);
        int count = Math.Min(Views?.Count ?? 0, MaxViews);
        w.Write((uint)count, 3);
        for (int i = 0; i < count; i++)
        {
            w.Write(Views[i].Ship, 8);
            w.Write((uint)Math.Clamp(Math.Round(Views[i].DelayTicks * DelayScale), 0, 65535), 16);
        }
        return w.ToArray();
    }

    public static FireEvent Read(BitReader r)
    {
        var e = new FireEvent
        {
            Shooter = (byte)r.Read(8),
            Sequence = (ushort)r.Read(16),
            Tick = r.Read(32),
        };
        double x = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        double y = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        double z = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        e.Origin = new SectorPoint(x, y, z);
        float vx = (float)r.ReadScaled(ShipSnapshot.VelocityRange, 17);
        float vy = (float)r.ReadScaled(ShipSnapshot.VelocityRange, 17);
        float vz = (float)r.ReadScaled(ShipSnapshot.VelocityRange, 17);
        e.Velocity = new Vector3(vx, vy, vz);
        e.Damage = r.Read(16) / 100f;
        int count = (int)r.Read(3);
        e.Views = new List<View>(count);
        for (int i = 0; i < count; i++)
            e.Views.Add(new View { Ship = (byte)r.Read(8), DelayTicks = r.Read(16) / DelayScale });
        return e;
    }
}

/// <summary>The host's verdict on a round, sent reliably to everyone: it hit this ship at this tick.</summary>
public struct HitEvent
{
    public byte Shooter;
    public ushort Sequence;
    public byte Target;
    public uint Tick;                       // in the shooter's timeline: the tick the round reached the ship
    public float Damage;

    public readonly byte[] ToBytes()
    {
        var w = new BitWriter();
        w.Write(Shooter, 8);
        w.Write(Sequence, 16);
        w.Write(Target, 8);
        w.Write(Tick, 32);
        w.Write((uint)Math.Clamp(Math.Round(Damage * 100), 0, 65535), 16);
        return w.ToArray();
    }

    public static HitEvent Read(BitReader r) => new()
    {
        Shooter = (byte)r.Read(8),
        Sequence = (ushort)r.Read(16),
        Target = (byte)r.Read(8),
        Tick = r.Read(32),
        Damage = r.Read(16) / 100f,
    };
}
