using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A warhead went off, sent reliably by the machine that flew the missile
/// to the host. Its fuse went off against the ships as that machine showed
/// them, so the blast says how late it showed each one (as a fire event
/// does): the host hurts every ship as it was then, and its verdicts are
/// hit events, one for each ship the fragments reached.
///
///   shooter 8, sequence 16, tick 32 and 1/256 of one 8, position 3 x 32
///   (1/1024 m), damage 16 (1/100), lethal and blast radius 2 x 16 (1/100 m),
///   the missile 16, views as in a fire event
/// </summary>
public struct BlastEvent
{
    public byte Shooter;
    public ushort Sequence;
    public uint Tick;
    public float TickFraction;              // when in the tick it went off, 0 to 1
    public SectorPoint Position;
    public float Damage;                    // within the lethal radius; with the square of the distance beyond, none past the blast radius
    public float LethalRadius;
    public float BlastRadius;
    public ushort Missile;                  // what went off: not hurt by itself
    public List<FireEvent.View> Views;

    /// <summary>What the fragments do at <paramref name="distance"/> m from the burst.</summary>
    public readonly float DamageAt(float distance)
    {
        if (distance >= BlastRadius)
            return 0;
        float r = MathF.Max(distance, LethalRadius) / LethalRadius;
        return Damage / (r * r);
    }

    public readonly byte[] ToBytes()
    {
        var w = new BitWriter();
        w.Write(Shooter, 8);
        w.Write(Sequence, 16);
        w.Write(Tick, 32);
        w.Write((uint)Math.Clamp(MathF.Round(TickFraction * 256), 0, 255), 8);
        Events.WritePoint(w, Position);
        w.Write((uint)Math.Clamp(Math.Round(Damage * 100), 0, 65535), 16);
        w.Write((uint)Math.Clamp(Math.Round(LethalRadius * 100), 1, 65535), 16);
        w.Write((uint)Math.Clamp(Math.Round(BlastRadius * 100), 0, 65535), 16);
        w.Write(Missile, 16);
        FireEvent.WriteViews(w, Views);
        return w.ToArray();
    }

    public static BlastEvent Read(BitReader r) => new()
    {
        Shooter = (byte)r.Read(8),
        Sequence = (ushort)r.Read(16),
        Tick = r.Read(32),
        TickFraction = r.Read(8) / 256f,
        Position = Events.ReadPoint(r),
        Damage = r.Read(16) / 100f,
        LethalRadius = r.Read(16) / 100f,
        BlastRadius = r.Read(16) / 100f,
        Missile = (ushort)r.Read(16),
        Views = FireEvent.ReadViews(r),
    };
}

/// <summary>
/// Something thrown out of a ship that then flies on its own, straight:
/// chaff, a flare. Sent reliably by the dropper's machine and relayed by
/// the host; every machine makes it from this and it flies the same
/// everywhere (no air, no guidance).
///
///   dropper 8, what 8 (a session's spawnable scene), tick 32,
///   position 3 x 32 (1/1024 m), velocity 3 x 17 (±4,096 m/s)
/// </summary>
public struct DropEvent
{
    public byte Dropper;
    public byte Scene;
    public uint Tick;
    public SectorPoint Position;
    public Vector3 Velocity;

    public readonly byte[] ToBytes()
    {
        var w = new BitWriter();
        w.Write(Dropper, 8);
        w.Write(Scene, 8);
        w.Write(Tick, 32);
        Events.WritePoint(w, Position);
        Events.WriteVelocity(w, Velocity);
        return w.ToArray();
    }

    public static DropEvent Read(BitReader r) => new()
    {
        Dropper = (byte)r.Read(8),
        Scene = (byte)r.Read(8),
        Tick = r.Read(32),
        Position = Events.ReadPoint(r),
        Velocity = Events.ReadVelocity(r),
    };
}

/// <summary>The fields events share, written as snapshots write them.</summary>
internal static class Events
{
    public static void WritePoint(BitWriter w, SectorPoint p)
    {
        foreach (double x in new[] { p.X, p.Y, p.Z })
            w.WriteSigned((int)Math.Round(Math.Clamp(x * ShipSnapshot.PositionScale, int.MinValue, int.MaxValue)), 32);
    }

    public static SectorPoint ReadPoint(BitReader r)
    {
        double x = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        double y = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        double z = r.ReadSigned(32) / ShipSnapshot.PositionScale;
        return new SectorPoint(x, y, z);
    }

    public static void WriteVelocity(BitWriter w, Vector3 v)
    {
        w.WriteScaled(v.X, ShipSnapshot.VelocityRange, 17);
        w.WriteScaled(v.Y, ShipSnapshot.VelocityRange, 17);
        w.WriteScaled(v.Z, ShipSnapshot.VelocityRange, 17);
    }

    public static Vector3 ReadVelocity(BitReader r) =>
        new((float)r.ReadScaled(ShipSnapshot.VelocityRange, 17),
            (float)r.ReadScaled(ShipSnapshot.VelocityRange, 17),
            (float)r.ReadScaled(ShipSnapshot.VelocityRange, 17));
}
