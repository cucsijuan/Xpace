using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A ship's eyes for rocks: a lidar looking along the way it flies. Every
/// <see cref="ScanSeconds"/> it sends a ray along its heading and rings of
/// rays round it out to <see cref="ConeDegrees"/>, each as wide as the ship
/// (<see cref="Clearance"/>), each out to <see cref="Range"/>, and learns
/// how far each way is clear (<see cref="RockField.SegmentAll"/>).
///
/// <see cref="Steer"/> turns that into a way to go: the way its pilot
/// wants while that is clear far enough, else the clear ray nearest it,
/// else the ray with the most room. What it does not see it cannot dodge:
/// a rock beyond its range, or slipping between two rays (fewer rays, wider
/// gaps), or come up since the last scan. A longer range, more rays and
/// faster scans dodge better; so does flying slower.
///
/// Put it under any ship; the ship's brain calls <see cref="Look"/> and
/// <see cref="Steer"/> every step. No sensor, no dodging.
/// </summary>
public partial class RockSensor : Node3D
{
    /// <summary>How far it sees (m).</summary>
    [Export] public float Range = 1500f;
    /// <summary>How far off the heading its outer ring looks.</summary>
    [Export] public float ConeDegrees = 30f;
    [Export] public int Rings = 2;
    [Export] public int RaysPerRing = 8;
    [Export] public double ScanSeconds = 0.2;
    /// <summary>The ship's half width and then some: a ray is clear only if something this far round it gets through (m).</summary>
    [Export] public float Clearance = 12f;

    /// <summary>The rays of the last scan (world directions) and how far each was clear.</summary>
    public Vector3[] Rays { get; private set; } = Array.Empty<Vector3>();
    public float[] Clear { get; private set; } = Array.Empty<float>();
    /// <summary>Steering round a rock, this step.</summary>
    public bool Avoiding { get; private set; }
    /// <summary>Scans so far.</summary>
    public int Scans { get; private set; }

    private double _sinceScan = double.MaxValue;

    /// <summary>Looks along a heading (world), when a scan is due.</summary>
    public void Look(Vector3 heading, double delta)
    {
        _sinceScan += delta;
        if (_sinceScan < ScanSeconds || heading.LengthSquared() < 1e-6f)
            return;
        _sinceScan = 0;
        Scans++;
        heading = heading.Normalized();
        int count = 1 + Math.Max(0, Rings) * Math.Max(1, RaysPerRing);
        if (Rays.Length != count)
        {
            Rays = new Vector3[count];
            Clear = new float[count];
        }
        Vector3 side = heading.Cross(Mathf.Abs(heading.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        Vector3 up = side.Cross(heading);
        Vector3 from = GlobalPosition;
        int i = 0;
        Rays[i++] = heading;
        for (int ring = 1; ring <= Rings; ring++)
        {
            float off = Mathf.DegToRad(ConeDegrees) * ring / Rings;
            for (int k = 0; k < RaysPerRing; k++)
            {
                float round = Mathf.Tau * (k + (ring % 2) * 0.5f) / RaysPerRing;   // rings staggered
                Rays[i++] = (heading * MathF.Cos(off) + (side * MathF.Cos(round) + up * MathF.Sin(round)) * MathF.Sin(off)).Normalized();
            }
        }
        for (i = 0; i < count; i++)
            Clear[i] = RockField.SegmentAll(from, from + Rays[i] * Range, out float t, out _, Clearance) ? t * Range : Range;
    }

    /// <summary>How far the last scan saw clear along the ray nearest a direction (its range before any scan).</summary>
    public float ClearAlong(Vector3 direction)
    {
        if (Rays.Length == 0)
            return Range;
        int nearest = 0;
        for (int i = 1; i < Rays.Length; i++)
            if (Rays[i].Dot(direction) > Rays[nearest].Dot(direction))
                nearest = i;
        return Clear[nearest];
    }

    /// <summary>
    /// The way to go (world direction): <paramref name="wanted"/> while the
    /// way ahead and the ray nearest it are clear for <paramref name="needed"/>
    /// metres (what the ship needs to turn away in time), else the clear
    /// ray nearest it, else the ray with the most room. A sensor that sees
    /// less far than that sees the rock too late to turn in time.
    /// </summary>
    public Vector3 Steer(Vector3 wanted, float needed)
    {
        Avoiding = false;
        if (Rays.Length == 0 || wanted.LengthSquared() < 1e-6f)
            return wanted;
        wanted = wanted.Normalized();
        needed = MathF.Min(needed, Range * 0.95f);       // it cannot ask for more than it sees
        int nearest = 0;
        for (int i = 1; i < Rays.Length; i++)
            if (Rays[i].Dot(wanted) > Rays[nearest].Dot(wanted))
                nearest = i;
        if (Clear[0] >= needed && Clear[nearest] >= needed)
            return wanted;

        Avoiding = true;
        int best = -1, roomiest = 0;
        for (int i = 0; i < Rays.Length; i++)
        {
            if (Clear[i] > Clear[roomiest])
                roomiest = i;
            if (Clear[i] >= needed && (best < 0 || Rays[i].Dot(wanted) > Rays[best].Dot(wanted)))
                best = i;
        }
        return Rays[best >= 0 ? best : roomiest];
    }
}
