using System;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A ship's eyes for rocks, and for the capsules round stations and other
/// big things (<see cref="AvoidCapsule"/>): a lidar looking along the way
/// it flies. Every
/// <see cref="ScanSeconds"/> it sends a ray along its heading and rings of
/// rays round it out to <see cref="ConeDegrees"/>, each as wide as the ship
/// (<see cref="Clearance"/>), each out to <see cref="Range"/>, and learns
/// how far each way is clear (<see cref="RockField.SegmentAll"/>,
/// <see cref="AvoidCapsule.SegmentAll"/>). A pilot going into a station
/// sets <see cref="Entering"/> to it: its capsules are no longer seen.
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

    /// <summary>What the ship means to go into (a station): the capsules below it are not seen.</summary>
    public Node Entering;

    /// <summary>The rays of the last scan (world directions) and how far each was clear.</summary>
    public Vector3[] Rays { get; private set; } = Array.Empty<Vector3>();
    public float[] Clear { get; private set; } = Array.Empty<float>();
    /// <summary>How near a ship it minds (m): farther off, ships go their way and so does it.</summary>
    [Export] public float ShipRange = 400f;
    /// <summary>How far ahead in time it minds ships that close on it (s): a ship coming fast is minded from farther off.</summary>
    [Export] public float ShipSeconds = 5f;

    /// <summary>The last scan saw rocks or capsules, not just ships.</summary>
    public bool RocksNear { get; private set; }

    /// <summary>Steering round a rock, this step.</summary>
    public bool Avoiding { get; private set; }
    /// <summary>Scans so far.</summary>
    public int Scans { get; private set; }

    private double _sinceScan = double.MaxValue;

    /// <summary>
    /// Looks along a heading (world), when a scan is due, at a speed (m/s; none given: the heading's length). Ships are
    /// seen moving: what matters is where they will be when this one gets there.
    /// </summary>
    public void Look(Vector3 heading, double delta, float speed = -1)
    {
        if (speed < 0)
            speed = heading.Length();
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
        _ships.Clear();
        // Another ship as fast as this one, coming at it, closes at twice its speed
        Spatial.Grid(this, Spatial.Ships).Within(from, MathF.Max(ShipRange, 2 * speed * ShipSeconds) + Xpace.Ship.ShipCollision.MostReach, _ships);
        RocksNear = false;
        for (i = 0; i < count; i++)
        {
            Vector3 to = from + Rays[i] * Range;
            float rock = RockField.SegmentAll(GetWorld3D(), from, to, out float t, out _, Clearance) ? t : 1;
            if (AvoidCapsule.SegmentAll(GetWorld3D(), from, to, out float c, Clearance, Entering))
                rock = MathF.Min(rock, c);
            RocksNear |= rock < 0.99f;
            Clear[i] = MathF.Min(rock * Range, ShipAlong(from, Rays[i], speed));
        }
    }

    private readonly System.Collections.Generic.List<Node> _ships = new();

    // How far along a ray it is clear of the ships near, flying it at that speed: each ship where it will be by then,
    // going as it goes. A ship keeping pace alongside is no danger; one it closes on fast is, from far off: what is clear
    // is how far this one goes before they meet
    private float ShipAlong(Vector3 from, Vector3 ray, float speed)
    {
        float clear = Range;
        foreach (Node node in _ships)
        {
            if (node is not Xpace.Ship.ISolidShip ship || !IsInstanceValid(node) || !ship.Solid || node.IsAncestorOf(this))
                continue;
            Vector3 p = ship.GlobalPosition - from, w = ship.SolidVelocity - ray * speed;
            float wide = ship.SolidReach + Clearance;
            float a = w.LengthSquared(), b = 2 * p.Dot(w), c = p.LengthSquared() - wide * wide;
            if (c <= 0)
            {
                if (b < 0 || p.Dot(ray) > 0)            // touching already: only the ways that go further in are shut
                    clear = 0;
                continue;
            }
            float disc = b * b - 4 * a * c;
            if (a < 1e-6f || b >= 0 || disc < 0)
                continue;                               // not closing, or passing wide
            float t = (-b - MathF.Sqrt(disc)) / (2 * a);
            clear = MathF.Min(clear, t * speed);
        }
        return clear;
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
