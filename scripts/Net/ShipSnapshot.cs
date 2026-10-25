using System;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>What a ship's radar is doing, as the others can tell from its pulses.</summary>
public enum RadarMode : byte { Off, Search, Track }

/// <summary>
/// A ship's state at one sector tick, as its owner sends it: 35 bytes, up
/// to 40 while its radar tracks something and its jammer sends, aimed.
///
///   tick                 32 bits
///   position             3 x 32 bits, 1/1024 m, to ±2,097 km from the sector's centre
///   orientation          smallest three: 2 bits for the largest component,
///                        3 x 11 bits for the others (within 0.12 degrees)
///   velocity             3 x 17 bits, ±4,096 m/s (steps of 1/16 m/s)
///   angular velocity     3 x 16 bits, ±16 rad/s (steps of 1/2048 rad/s)
///   destroyed            1 bit
///   thrust               4 bits, its motor's push, 0 to 15 fifteenths of the most
///   heat                 4 bits, its hull's, in tenths of 100% (to 150%)
///   radar                2 bits: off, searching, tracking; tracking, then
///                        16 bits for the ship tracked (0FFFFh: none in the
///                        session) and 1 bit: guiding a missile at it
///   jammer               2 bits: off, noise, deception; sending, then
///                        4 bits for its model (its place in
///                        <see cref="SessionNode.Jammers"/>; 15: not
///                        there) and 1 bit: aimed; aimed, then 8 bits of
///                        azimuth and 7 of elevation (ship axes)
///
/// Anything that flies is sent this way: ships, missiles. Or as a delta
/// (<see cref="Wire.WriteDelta"/>) from an older snapshot the other end
/// is known to have.
/// </summary>
public struct ShipSnapshot
{
    public const int Bytes = 35, MaxBytes = 40;
    public const byte NoModel = 15;
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
    public float Heat;                      // its hull's heat, 1 at 100% (what a heat seeker sees of it besides the motor)
    public RadarMode Radar;
    public ushort RadarTarget;              // tracking: whom
    public bool RadarGuiding;               // tracking: and guiding a missile at it
    public JamMode Jam;
    public byte JamModel;                   // sending: its place in the session's list of models
    public bool JamAimed;
    public Vector3 JamAim;                  // aimed: where, ship axes (-Z ahead)

    public void Write(BitWriter w) => Encode().Write(w);

    public static ShipSnapshot Read(BitReader r) => Wire.Read(r).Decode();

    public byte[] ToBytes()
    {
        var w = new BitWriter();
        Write(w);
        return w.ToArray();
    }

    /// <summary>The snapshot as it goes on the wire, in whole steps.</summary>
    public Wire Encode()
    {
        var s = new Wire { Tick = Tick, P0 = Fixed(Position.X), P1 = Fixed(Position.Y), P2 = Fixed(Position.Z) };
        // Smallest three: the largest component is left out and rebuilt from
        // the others (the quaternion has length 1); q and -q are the same
        // turn, so it is made positive first. The others then lie within ±1/√2.
        Quaternion q = Orientation.Normalized();
        Span<float> c = stackalloc float[] { q.X, q.Y, q.Z, q.W };
        int largest = 0;
        for (int i = 1; i < 4; i++)
            if (MathF.Abs(c[i]) > MathF.Abs(c[largest]))
                largest = i;
        float sign = c[largest] < 0 ? -1 : 1;
        s.Largest = (byte)largest;
        Span<int> rest = stackalloc int[3];
        for (int i = 0, k = 0; i < 4; i++)
            if (i != largest)
                rest[k++] = Steps(c[i] * sign, QuaternionRange, QuaternionBits);
        (s.Q0, s.Q1, s.Q2) = (rest[0], rest[1], rest[2]);
        s.V0 = Steps(Velocity.X, VelocityRange, VelocityBits);
        s.V1 = Steps(Velocity.Y, VelocityRange, VelocityBits);
        s.V2 = Steps(Velocity.Z, VelocityRange, VelocityBits);
        s.W0 = Steps(AngularVelocity.X, AngularRange, AngularBits);
        s.W1 = Steps(AngularVelocity.Y, AngularRange, AngularBits);
        s.W2 = Steps(AngularVelocity.Z, AngularRange, AngularBits);
        s.Flags = (Destroyed ? 1u : 0) | (uint)Math.Clamp(MathF.Round(Thrust * 15), 0, 15) << 1 | ((uint)Radar & 3) << 5 |
                  (uint)Math.Clamp(MathF.Round(Heat * 10), 0, 15) << 7;
        if (Radar == RadarMode.Track)
            s.Flags |= (uint)RadarTarget << 11 | (RadarGuiding ? 1u : 0) << 27;
        if (Jam != JamMode.Off)
        {
            s.Jam = (uint)Jam & 3 | (uint)Math.Min((int)JamModel, NoModel) << 2 | (JamAimed ? 1u : 0) << 6;
            if (JamAimed)
            {
                Vector3 a = JamAim.LengthSquared() > 1e-6f ? JamAim.Normalized() : Vector3.Forward;
                int azimuth = (int)MathF.Round(MathF.Atan2(a.X, -a.Z) / MathF.Tau * 256) & 255;
                int elevation = Math.Clamp((int)MathF.Round(MathF.Asin(Math.Clamp(a.Y, -1, 1)) / (MathF.PI / 2) * 63), -63, 63) + 64;
                s.Jam |= (uint)azimuth << 7 | (uint)elevation << 15;
            }
        }
        return s;

        static int Fixed(double x) => (int)Math.Round(Math.Clamp(x * PositionScale, int.MinValue, int.MaxValue));
    }

    /// <summary>A value in -range..range, in steps of range / (2^(bits-1) - 1), clamped (as <see cref="BitWriter.WriteScaled"/>).</summary>
    private static int Steps(double value, double range, int bits)
    {
        int steps = (1 << (bits - 1)) - 1;
        return (int)Math.Round(Math.Clamp(value / range, -1, 1) * steps);
    }

    private static double Value(int steps, double range, int bits) => steps * range / ((1 << (bits - 1)) - 1);

    /// <summary>
    /// A snapshot in the whole steps it is sent in: what both ends keep of
    /// it, so that a delta is taken and undone on exactly the same numbers.
    /// </summary>
    public struct Wire
    {
        /// <summary>The most ticks a delta reaches back.</summary>
        public const int MaxAge = 255;

        public uint Tick;
        public int P0, P1, P2;              // 1/1024 m
        public byte Largest;
        public int Q0, Q1, Q2;
        public int V0, V1, V2;
        public int W0, W1, W2;
        public uint Flags;                  // destroyed (bit 0), thrust (1-4), radar (5-6), heat (7-10), tracked (11-26), guiding (27)
        public uint Jam;                    // mode (bits 0-1), model (2-5), aimed (6), azimuth (7-14), elevation + 64 (15-21)

        private readonly RadarMode Radar => (RadarMode)(Flags >> 5 & 3);

        public readonly ShipSnapshot Decode()
        {
            var s = new ShipSnapshot { Tick = Tick };
            s.Position = new SectorPoint(P0 / PositionScale, P1 / PositionScale, P2 / PositionScale);
            Span<float> c = stackalloc float[4];
            Span<int> rest = stackalloc int[] { Q0, Q1, Q2 };
            float sum = 0;
            for (int i = 0, k = 0; i < 4; i++)
                if (i != Largest)
                {
                    c[i] = (float)Value(rest[k++], QuaternionRange, QuaternionBits);
                    sum += c[i] * c[i];
                }
            c[Largest] = MathF.Sqrt(MathF.Max(0, 1 - sum));
            s.Orientation = new Quaternion(c[0], c[1], c[2], c[3]).Normalized();
            s.Velocity = new Vector3((float)Value(V0, VelocityRange, VelocityBits), (float)Value(V1, VelocityRange, VelocityBits), (float)Value(V2, VelocityRange, VelocityBits));
            s.AngularVelocity = new Vector3((float)Value(W0, AngularRange, AngularBits), (float)Value(W1, AngularRange, AngularBits), (float)Value(W2, AngularRange, AngularBits));
            s.Destroyed = (Flags & 1) != 0;
            s.Thrust = (Flags >> 1 & 15) / 15f;
            s.Heat = (Flags >> 7 & 15) / 10f;
            s.Radar = Radar;
            s.RadarTarget = s.Radar == RadarMode.Track ? (ushort)(Flags >> 11) : NoTarget;
            s.RadarGuiding = s.Radar == RadarMode.Track && (Flags >> 27 & 1) != 0;
            s.Jam = (JamMode)(Jam & 3);
            if (s.Jam != JamMode.Off)
            {
                s.JamModel = (byte)(Jam >> 2 & 15);
                s.JamAimed = (Jam >> 6 & 1) != 0;
                if (s.JamAimed)
                {
                    float azimuth = (Jam >> 7 & 255) / 256f * MathF.Tau;
                    float elevation = ((int)(Jam >> 15 & 127) - 64) / 63f * (MathF.PI / 2);
                    float flat = MathF.Cos(elevation);
                    s.JamAim = new Vector3(MathF.Sin(azimuth) * flat, MathF.Sin(elevation), -MathF.Cos(azimuth) * flat);
                }
            }
            return s;
        }

        public readonly void Write(BitWriter w)
        {
            w.Write(Tick, 32);
            w.WriteSigned(P0, 32);
            w.WriteSigned(P1, 32);
            w.WriteSigned(P2, 32);
            WriteOrientation(w);
            WriteSteps(w, V0, V1, V2, VelocityBits);
            WriteSteps(w, W0, W1, W2, AngularBits);
            WriteFlags(w);
        }

        public static Wire Read(BitReader r)
        {
            var s = new Wire { Tick = r.Read(32), P0 = r.ReadSigned(32), P1 = r.ReadSigned(32), P2 = r.ReadSigned(32) };
            s.ReadOrientation(r);
            (s.V0, s.V1, s.V2) = (r.ReadSigned(VelocityBits), r.ReadSigned(VelocityBits), r.ReadSigned(VelocityBits));
            (s.W0, s.W1, s.W2) = (r.ReadSigned(AngularBits), r.ReadSigned(AngularBits), r.ReadSigned(AngularBits));
            s.ReadFlags(r);
            return s;
        }

        /// <summary>
        /// This snapshot as a change from <paramref name="b"/> (older, by 1 to
        /// <see cref="MaxAge"/> ticks):
        ///
        ///   tick                 16 bits: its low half (the receiver's clock
        ///                        is within 9 minutes of it: the rest)
        ///   age                  8 bits: ticks since the base
        ///   position             3 numbers: how far from where the base's
        ///                        velocity takes it in that time
        ///   orientation          1 bit: changed, then as in a full snapshot
        ///   velocity             1 bit: changed, then 3 numbers: the change
        ///   angular velocity     the same
        ///   the rest             1 bit: changed, then as in a full snapshot
        ///
        /// A number is 5 bits (6 for positions) saying how many bits follow,
        /// then those, zigzagged (0, -1, 1, -2...). A ship flying steady takes
        /// 6 bytes; one turning and pushing, about 14.
        /// </summary>
        public readonly void WriteDelta(BitWriter w, in Wire b)
        {
            uint age = Tick - b.Tick;
            w.Write(Tick & 0xFFFF, 16);
            w.Write(age, 8);
            WriteNumber(w, P0 - Predicted(b.P0, b.V0, age), 6);
            WriteNumber(w, P1 - Predicted(b.P1, b.V1, age), 6);
            WriteNumber(w, P2 - Predicted(b.P2, b.V2, age), 6);
            bool turned = Largest != b.Largest || Q0 != b.Q0 || Q1 != b.Q1 || Q2 != b.Q2;
            w.Write(turned ? 1u : 0, 1);
            if (turned)
                WriteOrientation(w);
            WriteChange(w, V0 - b.V0, V1 - b.V1, V2 - b.V2);
            WriteChange(w, W0 - b.W0, W1 - b.W1, W2 - b.W2);
            w.Write(Flags != b.Flags || Jam != b.Jam ? 1u : 0, 1);
            if (Flags != b.Flags || Jam != b.Jam)
                WriteFlags(w);
        }

        /// <summary>A delta's tick and its base's, read first (to find the base); <paramref name="near"/>: the receiver's tick.</summary>
        public static (uint Tick, uint BaseTick) ReadDeltaHeader(BitReader r, uint near)
        {
            uint tick = (uint)(near + (short)(r.Read(16) - (near & 0xFFFF)));
            return (tick, tick - r.Read(8));
        }

        /// <summary>The rest of a delta (after <see cref="ReadDeltaHeader"/>), from its base.</summary>
        public static Wire ReadDelta(BitReader r, uint tick, in Wire b)
        {
            uint age = tick - b.Tick;
            var s = new Wire { Tick = tick };
            s.P0 = (int)(Predicted(b.P0, b.V0, age) + ReadNumber(r, 6));
            s.P1 = (int)(Predicted(b.P1, b.V1, age) + ReadNumber(r, 6));
            s.P2 = (int)(Predicted(b.P2, b.V2, age) + ReadNumber(r, 6));
            if (r.Read(1) != 0)
                s.ReadOrientation(r);
            else
                (s.Largest, s.Q0, s.Q1, s.Q2) = (b.Largest, b.Q0, b.Q1, b.Q2);
            (s.V0, s.V1, s.V2) = (b.V0, b.V1, b.V2);
            if (r.Read(1) != 0)
                (s.V0, s.V1, s.V2) = ((int)(b.V0 + ReadNumber(r, 5)), (int)(b.V1 + ReadNumber(r, 5)), (int)(b.V2 + ReadNumber(r, 5)));
            (s.W0, s.W1, s.W2) = (b.W0, b.W1, b.W2);
            if (r.Read(1) != 0)
                (s.W0, s.W1, s.W2) = ((int)(b.W0 + ReadNumber(r, 5)), (int)(b.W1 + ReadNumber(r, 5)), (int)(b.W2 + ReadNumber(r, 5)));
            if (r.Read(1) != 0)
                s.ReadFlags(r);
            else
                (s.Flags, s.Jam) = (b.Flags, b.Jam);
            return s;
        }

        /// <summary>
        /// Where a velocity takes a position in <paramref name="age"/> ticks
        /// (of 1/60 s), in whole numbers: the same on every machine.
        /// </summary>
        private static long Predicted(int position, int velocity, uint age)
        {
            // velocity in steps of 4096/65535 m/s, position in steps of 1/1024 m
            long moved = (long)velocity * 4096 * 1024 * age;
            const long per = 65535L * 60;
            return position + (moved >= 0 ? (moved + per / 2) / per : (moved - per / 2) / per);
        }

        private readonly void WriteOrientation(BitWriter w)
        {
            w.Write(Largest, 2);
            WriteSteps(w, Q0, Q1, Q2, QuaternionBits);
        }

        private void ReadOrientation(BitReader r)
        {
            Largest = (byte)r.Read(2);
            (Q0, Q1, Q2) = (r.ReadSigned(QuaternionBits), r.ReadSigned(QuaternionBits), r.ReadSigned(QuaternionBits));
        }

        private readonly void WriteFlags(BitWriter w)
        {
            w.Write(Flags & 0x7FF, 11);
            if (Radar == RadarMode.Track)
                w.Write(Flags >> 11 & 0x1FFFF, 17);
            w.Write(Jam & 3, 2);
            if ((Jam & 3) == 0)
                return;
            w.Write(Jam >> 2 & 31, 5);
            if ((Jam >> 6 & 1) != 0)
                w.Write(Jam >> 7 & 0x7FFF, 15);
        }

        private void ReadFlags(BitReader r)
        {
            Flags = r.Read(11);
            if (Radar == RadarMode.Track)
                Flags |= r.Read(17) << 11;
            Jam = r.Read(2);
            if (Jam == 0)
                return;
            Jam |= r.Read(5) << 2;
            if ((Jam >> 6 & 1) != 0)
                Jam |= r.Read(15) << 7;
        }

        private static void WriteSteps(BitWriter w, int a, int b, int c, int bits)
        {
            w.WriteSigned(a, bits);
            w.WriteSigned(b, bits);
            w.WriteSigned(c, bits);
        }

        private static void WriteChange(BitWriter w, int a, int b, int c)
        {
            bool changed = a != 0 || b != 0 || c != 0;
            w.Write(changed ? 1u : 0, 1);
            if (!changed)
                return;
            WriteNumber(w, a, 5);
            WriteNumber(w, b, 5);
            WriteNumber(w, c, 5);
        }

        /// <summary>How many bits, then the zigzagged value in that many (the differences here fit in 34 bits).</summary>
        private static void WriteNumber(BitWriter w, long value, int lengthBits)
        {
            ulong z = (ulong)(value << 1 ^ value >> 63);
            int n = 0;
            while (n < 64 && z >> n != 0)
                n++;
            w.Write((uint)n, lengthBits);
            if (n > 32)
            {
                w.Write((uint)z, 32);
                w.Write((uint)(z >> 32), n - 32);
            }
            else if (n > 0)
                w.Write((uint)z, n);
        }

        private static long ReadNumber(BitReader r, int lengthBits)
        {
            int n = (int)r.Read(lengthBits);
            ulong z = n > 32 ? r.Read(32) | (ulong)r.Read(n - 32) << 32 : n > 0 ? r.Read(n) : 0;
            return (long)(z >> 1) ^ -(long)(z & 1);
        }
    }
}
