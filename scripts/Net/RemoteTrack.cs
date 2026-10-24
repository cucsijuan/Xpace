using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// A ship someone else owns, as seen here: the snapshots that came in,
/// and the state to show at the tick being shown (snapshot interpolation).
///
/// The ship is shown a little in the past, <see cref="Delay"/> ticks behind
/// the sector's tick: the latest lateness seen plus one sending period, just
/// late enough that the next snapshot has usually arrived, so the state can
/// be interpolated between two real ones. The
/// delay follows the packets: it jumps up when one comes later than any
/// before, and creeps back down while they come early. The shown tick
/// never jumps: it runs a few percent fast or slow to catch up.
///
/// Between two snapshots the position follows a Hermite curve through both
/// positions and velocities (in space, with no drag, nearly the true path),
/// the orientation a slerp. Past the newest snapshot (a packet lost, or
/// late) the ship coasts on its last velocities for up to
/// <see cref="MaxExtrapolation"/> seconds, then stops where it is.
/// </summary>
public sealed class RemoteTrack
{
    public struct Pose
    {
        public SectorPoint Position;
        public Quaternion Orientation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public bool Destroyed;
        public float Thrust;
        public RadarMode Radar;
        public ushort RadarTarget;
        public bool RadarGuiding;
        public bool Extrapolated;
    }

    public float TickSeconds = 1f / 60;
    public float MaxExtrapolation = 0.25f;

    /// <summary>Ticks kept beyond the lateness and the sending period, for the jitter not seen yet.</summary>
    public double SafetyTicks = 1;

    /// <summary>How fast the delay comes back down while packets come early, per tick.</summary>
    public double DelayDecay = 0.002;

    /// <summary>The most the shown tick runs fast or slow to catch up, as a fraction.</summary>
    public double MaxWarp = 0.05;

    /// <summary>The sector tick being shown; null until the first snapshot.</summary>
    public double? ShownTick { get; private set; }

    /// <summary>How far behind the sector's tick the ship is shown, in ticks.</summary>
    public double Delay => _lateness + _period + SafetyTicks;

    public int Received { get; private set; }

    /// <summary>Snapshots kept: 64 is 3 s at 20 a second.</summary>
    public int Capacity = 64;

    /// <summary>The tick of the newest snapshot here; null before the first.</summary>
    public uint? NewestTick => _snapshots.Count > 0 ? _snapshots[^1].Tick : null;

    private readonly List<ShipSnapshot> _snapshots = new();
    private double _lateness;
    private uint _period = uint.MaxValue;

    /// <summary>A snapshot arrived at the sector tick <paramref name="now"/>. Old and repeated ones are dropped.</summary>
    public void Receive(ShipSnapshot s, double now)
    {
        int i = _snapshots.Count;
        while (i > 0 && _snapshots[i - 1].Tick >= s.Tick)
        {
            if (_snapshots[i - 1].Tick == s.Tick)
                return;
            i--;
        }
        if (ShownTick.HasValue && s.Tick < ShownTick.Value - Capacity)
            return;
        if (i > 0)
            _period = Math.Min(_period, s.Tick - _snapshots[i - 1].Tick);
        if (i < _snapshots.Count)
            _period = Math.Min(_period, _snapshots[i].Tick - s.Tick);
        _snapshots.Insert(i, s);
        if (_snapshots.Count > Capacity)
            _snapshots.RemoveAt(0);
        Received++;

        double late = Math.Max(0, now - s.Tick);
        if (Received == 1 || late > _lateness)
            _lateness = late;
        if (_period == uint.MaxValue)
            return;                                 // one snapshot: the period is not known yet
        ShownTick ??= now - Delay;
    }

    /// <summary>One tick has passed; <paramref name="now"/> is the sector tick.</summary>
    public void Advance(double now)
    {
        if (!ShownTick.HasValue)
            return;
        double newest = now - _snapshots[^1].Tick;
        if (newest < _lateness)
            _lateness = Math.Max(newest, _lateness - DelayDecay);

        double target = now - Delay;
        double error = target - (ShownTick.Value + 1);
        ShownTick = Math.Abs(error) > 30 ? target : ShownTick.Value + 1 + Math.Clamp(error * 0.1, -MaxWarp, MaxWarp);
    }

    public Pose Sample() => ShownTick.HasValue ? Sample(ShownTick.Value) : default;

    public Pose Sample(double tick)
    {
        int next = _snapshots.FindIndex(s => s.Tick > tick);
        if (next == 0)
            return At(_snapshots[0]);
        if (next < 0)
            return Coast(_snapshots[^1], (float)((tick - _snapshots[^1].Tick) * TickSeconds));

        ShipSnapshot a = _snapshots[next - 1], b = _snapshots[next];
        float span = (b.Tick - a.Tick) * TickSeconds;
        float u = (float)((tick - a.Tick) / (b.Tick - a.Tick));
        float u2 = u * u, u3 = u2 * u;
        float h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
        Vector3 offset = h10 * span * a.Velocity + h01 * a.Position.To(b.Position) + h11 * span * b.Velocity;
        return new Pose
        {
            Position = a.Position + offset,
            Orientation = a.Orientation.Slerp(b.Orientation, u),
            Velocity = a.Velocity.Lerp(b.Velocity, u),
            AngularVelocity = a.AngularVelocity.Lerp(b.AngularVelocity, u),
            Destroyed = a.Destroyed || b.Destroyed,
            Thrust = u < 0.5f ? a.Thrust : b.Thrust,
            Radar = u < 0.5f ? a.Radar : b.Radar,
            RadarTarget = u < 0.5f ? a.RadarTarget : b.RadarTarget,
            RadarGuiding = u < 0.5f ? a.RadarGuiding : b.RadarGuiding,
        };
    }

    private Pose Coast(ShipSnapshot s, float seconds)
    {
        seconds = Math.Min(seconds, MaxExtrapolation);
        Pose p = At(s);
        p.Position += s.Velocity * seconds;
        float rate = s.AngularVelocity.Length();
        if (rate > 0)
            p.Orientation = (new Quaternion(s.AngularVelocity / rate, rate * seconds) * s.Orientation).Normalized();
        p.Extrapolated = seconds > 0;
        return p;
    }

    private static Pose At(ShipSnapshot s) => new()
    {
        Position = s.Position,
        Orientation = s.Orientation,
        Velocity = s.Velocity,
        AngularVelocity = s.AngularVelocity,
        Destroyed = s.Destroyed,
        Thrust = s.Thrust,
        Radar = s.Radar,
        RadarTarget = s.RadarTarget,
        RadarGuiding = s.RadarGuiding,
    };
}
