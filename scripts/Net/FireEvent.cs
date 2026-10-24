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
///   shooter 8, ship 16, sequence 16, tick 32, origin 3 x 32 (1/1024 m),
///   velocity 3 x 17 (±4,096 m/s), damage 16 (1/100), views 4 bits count,
///   then each: ship 16, delay 16 (1/256 tick, up to 4 s)
/// </summary>
public struct FireEvent
{
    public const int MaxViews = 15;
    private const double DelayScale = 256;

    public struct View
    {
        public ushort Ship;
        public double DelayTicks;           // how far in the past the shooter showed it
    }

    public byte Shooter;                    // the machine
    public ushort Ship;                     // the ship that fired: never hit by its own round
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
        w.Write(Ship, 16);
        w.Write(Sequence, 16);
        w.Write(Tick, 32);
        Events.WritePoint(w, Origin);
        Events.WriteVelocity(w, Velocity);
        w.Write((uint)Math.Clamp(Math.Round(Damage * 100), 0, 65535), 16);
        WriteViews(w, Views);
        return w.ToArray();
    }

    internal static void WriteViews(BitWriter w, List<View> views)
    {
        int count = Math.Min(views?.Count ?? 0, MaxViews);
        w.Write((uint)count, 4);
        for (int i = 0; i < count; i++)
        {
            w.Write(views[i].Ship, 16);
            w.Write((uint)Math.Clamp(Math.Round(views[i].DelayTicks * DelayScale), 0, 65535), 16);
        }
    }

    internal static List<View> ReadViews(BitReader r)
    {
        int count = (int)r.Read(4);
        var views = new List<View>(count);
        for (int i = 0; i < count; i++)
            views.Add(new View { Ship = (ushort)r.Read(16), DelayTicks = r.Read(16) / DelayScale });
        return views;
    }

    public static FireEvent Read(BitReader r)
    {
        var e = new FireEvent
        {
            Shooter = (byte)r.Read(8),
            Ship = (ushort)r.Read(16),
            Sequence = (ushort)r.Read(16),
            Tick = r.Read(32),
            Origin = Events.ReadPoint(r),
            Velocity = Events.ReadVelocity(r),
        };
        e.Damage = r.Read(16) / 100f;
        e.Views = ReadViews(r);
        return e;
    }
}

/// <summary>The host's verdict on a round or a blast, sent reliably to everyone: it hit this ship at this tick.</summary>
public struct HitEvent
{
    public byte Shooter;
    public ushort Sequence;
    public ushort Target;
    public uint Tick;                       // in the shooter's timeline: the tick the round reached the ship
    public float Damage;

    public readonly byte[] ToBytes()
    {
        var w = new BitWriter();
        w.Write(Shooter, 8);
        w.Write(Sequence, 16);
        w.Write(Target, 16);
        w.Write(Tick, 32);
        w.Write((uint)Math.Clamp(Math.Round(Damage * 100), 0, 65535), 16);
        return w.ToArray();
    }

    public static HitEvent Read(BitReader r) => new()
    {
        Shooter = (byte)r.Read(8),
        Sequence = (ushort)r.Read(16),
        Target = (ushort)r.Read(16),
        Tick = r.Read(32),
        Damage = r.Read(16) / 100f,
    };
}
