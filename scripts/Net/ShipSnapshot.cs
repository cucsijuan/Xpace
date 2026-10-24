using System;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>What a ship's radar is doing, as the others can tell from its pulses.</summary>
public enum RadarMode : byte { Off, Search, Track }

/// <summary>
/// A ship's state at one sector tick, as its owner sends it: 34 bytes, 36
/// while its radar tracks something.
///
///   tick                 32 bits
///   position             3 x 32 bits, 1/1024 m, to ±2,097 km from the sector's centre
///   orientation          smallest three: 2 bits for the largest component,
///                        3 x 11 bits for the others (within 0.12 degrees)
///   velocity             3 x 17 bits, ±4,096 m/s (steps of 1/16 m/s)
///   angular velocity     3 x 16 bits, ±16 rad/s (steps of 1/2048 rad/s)
///   destroyed            1 bit
///   thrust               4 bits, its motor's push, 0 to 15 fifteenths of the most
///   radar                2 bits: off, searching, tracking; tracking, then
///                        16 bits for the ship tracked (0FFFFh: none in the
///                        session) and 1 bit: guiding a missile at it
///
/// Anything that flies is sent this way: ships, missiles.
/// </summary>
public struct ShipSnapshot
{
    public const int Bytes = 34, MaxBytes = 36;
    public const ushort NoTarget = 0xFFFF;
    public const double PositionScale = 1024, VelocityRange = 4096, AngularRange = 16;
    private const int QuaternionBits = 11, VelocityBits = 17, AngularBits = 16;
    private static readonly double QuaternionRange = 1 / Math.Sqrt(2);

    public uint Tick;
    public SectorPoint Position;
    public Quaternion Orientation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    public bool Destroyed;                  // blown up and not back: shown hidden, cannot be hit
    public float Thrust;                    // 0 to 1: what the motor pushes, of the most it can (its heat, its plume)
    public RadarMode Radar;
    public ushort RadarTarget;              // tracking: whom
    public bool RadarGuiding;               // tracking: and guiding a missile at it

    public void Write(BitWriter w)
    {
        w.Write(Tick, 32);
        WritePosition(w, Position.X);
        WritePosition(w, Position.Y);
        WritePosition(w, Position.Z);
        WriteOrientation(w, Orientation);
        WriteVector(w, Velocity, VelocityRange, VelocityBits);
        WriteVector(w, AngularVelocity, AngularRange, AngularBits);
        w.Write(Destroyed ? 1u : 0, 1);
        w.Write((uint)Math.Clamp(MathF.Round(Thrust * 15), 0, 15), 4);
        w.Write((uint)Radar, 2);
        if (Radar == RadarMode.Track)
        {
            w.Write(RadarTarget, 16);
            w.Write(RadarGuiding ? 1u : 0, 1);
        }
    }

    public static ShipSnapshot Read(BitReader r)
    {
        var s = new ShipSnapshot { Tick = r.Read(32) };
        s.Position = new SectorPoint(ReadPosition(r), ReadPosition(r), ReadPosition(r));
        s.Orientation = ReadOrientation(r);
        s.Velocity = ReadVector(r, VelocityRange, VelocityBits);
        s.AngularVelocity = ReadVector(r, AngularRange, AngularBits);
        s.Destroyed = r.Read(1) != 0;
        s.Thrust = r.Read(4) / 15f;
        s.Radar = (RadarMode)r.Read(2);
        s.RadarTarget = s.Radar == RadarMode.Track ? (ushort)r.Read(16) : NoTarget;
        s.RadarGuiding = s.Radar == RadarMode.Track && r.Read(1) != 0;
        return s;
    }

    public byte[] ToBytes()
    {
        var w = new BitWriter();
        Write(w);
        return w.ToArray();
    }

    private static void WritePosition(BitWriter w, double x) =>
        w.WriteSigned((int)Math.Round(Math.Clamp(x * PositionScale, int.MinValue, int.MaxValue)), 32);

    private static double ReadPosition(BitReader r) => r.ReadSigned(32) / PositionScale;

    /// <summary>
    /// The largest component is left out and rebuilt from the other three
    /// (the quaternion has length 1); q and -q are the same turn, so it is
    /// made positive first. The others then lie within ±1/√2.
    /// </summary>
    private static void WriteOrientation(BitWriter w, Quaternion q)
    {
        q = q.Normalized();
        Span<float> c = stackalloc float[] { q.X, q.Y, q.Z, q.W };
        int largest = 0;
        for (int i = 1; i < 4; i++)
            if (MathF.Abs(c[i]) > MathF.Abs(c[largest]))
                largest = i;
        float sign = c[largest] < 0 ? -1 : 1;
        w.Write((uint)largest, 2);
        for (int i = 0; i < 4; i++)
            if (i != largest)
                w.WriteScaled(c[i] * sign, QuaternionRange, QuaternionBits);
    }

    private static Quaternion ReadOrientation(BitReader r)
    {
        int largest = (int)r.Read(2);
        Span<float> c = stackalloc float[4];
        float sum = 0;
        for (int i = 0; i < 4; i++)
            if (i != largest)
            {
                c[i] = (float)r.ReadScaled(QuaternionRange, QuaternionBits);
                sum += c[i] * c[i];
            }
        c[largest] = MathF.Sqrt(MathF.Max(0, 1 - sum));
        return new Quaternion(c[0], c[1], c[2], c[3]).Normalized();
    }

    private static void WriteVector(BitWriter w, Vector3 v, double range, int bits)
    {
        w.WriteScaled(v.X, range, bits);
        w.WriteScaled(v.Y, range, bits);
        w.WriteScaled(v.Z, range, bits);
    }

    private static Vector3 ReadVector(BitReader r, double range, int bits) =>
        new((float)r.ReadScaled(range, bits), (float)r.ReadScaled(range, bits), (float)r.ReadScaled(range, bits));
}
