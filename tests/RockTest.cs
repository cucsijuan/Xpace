using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Rock fields: the same rocks from the same seed (and a reference hash, to
/// run on every machine), the solid spheres found along segments and near
/// points, rocks only where the region puts them (a planet's ring 100 km
/// out included), a mined rock gone for good; ships bouncing off rocks and
/// losing hull by the energy of the crash, slow or fast (no jumping a rock
/// in one step); rounds stopped by rock; radars blind behind it; chunks
/// drawn around the camera; and what the queries cost.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/rock_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RockTest : Node
{
    /// <summary>The hash of the reference field's rocks: must be the same on Linux and Windows.</summary>
    private const ulong ReferenceHash = 0x2C6D394A45FC1B0AUL;

    private static readonly string[] KindNames =
        { "rock_01", "rock_02", "rock_03", "rock_04", "rock_05", "rock_holes_03", "rock_large_01", "rock_large_02", "rock_large_holes_02" };

    private int _failures;

    public override async void _Ready()
    {
        var world = new Node3D { Name = "World" };
        AddChild(world);
        var kinds = new Godot.Collections.Array<RockKind>(KindNames.Select(k => GD.Load<RockKind>($"res://assets/space/rocks/{k}.tres")));
        Check("every rock kind has its spheres fitted", kinds.All(k => k.Spheres.Length > 0));

        // ---------- the same rocks everywhere ----------
        RockField a = Field(world, "A", kinds, 4770, new Vector3(0, 0, -20_000));
        RockField b = Field(world, "B", kinds, 4770, new Vector3(0, 0, -20_000));
        RockField c = Field(world, "C", kinds, 4771, new Vector3(0, 0, -20_000));
        ulong ha = Hash(a), hb = Hash(b), hc = Hash(c);
        GD.Print($"rock hash {ha:X16}");
        Check("the same seed gives the same rocks", ha == hb);
        Check("another seed gives other rocks", ha != hc);
        if (ReferenceHash != 0)
            Check($"the rocks are the reference ones (the same on every machine)", ha == ReferenceHash);
        int count = 0;
        for (int x = -4; x < 4; x++)
            for (int y = -2; y < 2; y++)
                for (int z = -4; z < 4; z++)
                    count += a.RocksIn(x, y, z).Length;
        Check($"a 3.2 x 1.6 x 3.2 km block of the field holds rocks ({count})", count > 300);
        b.QueueFree();
        c.QueueFree();
        await Frames(1);

        // ---------- segments and spheres agree ----------
        var rng = new RandomNumberGenerator { Seed = 7 };
        var near = new List<RockSphere>();
        int hits = 0, wrong = 0;
        for (int i = 0; i < 300; i++)
        {
            Vector3 from = new Vector3(rng.RandfRange(-1500, 1500), rng.RandfRange(-500, 500), rng.RandfRange(-21500, -18500));
            Vector3 to = from + new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)).Normalized() * 800;
            bool hit = a.Segment(from, to, out float t, out Vector3 n, out _);
            // Nothing solid before the hit; the hit point is on a sphere's surface
            int samples = 40;
            for (int k = 0; k < samples; k++)
            {
                float s = t * k / samples;
                near.Clear();
                a.Overlap(from + (to - from) * s, 0, near);
                if (near.Count > 0 && s < t - 0.01f)
                    wrong++;
            }
            if (hit)
            {
                hits++;
                near.Clear();
                Vector3 p = from + (to - from) * t;
                a.Overlap(p, 0.05f, near);
                // (a segment that starts inside a rock stops at once, facing any way)
                if (near.Count == 0 || (t > 0 && n.Dot(to - from) > 0))
                    wrong++;
            }
        }
        Check($"segments stop at the first solid sphere ({hits} of 300 hit rock, {wrong} disagree)", hits > 20 && wrong == 0);

        // ---------- a ring 100 km out ----------
        var ringField = new RockField
        {
            Name = "Ring", Kinds = kinds, Seed = 99, CellSize = 500, RocksPerCell = 8,
            Region = new RockRing { InnerRadius = 80_000, OuterRadius = 120_000, Thickness = 1500, RadialFalloff = 2000, VerticalFalloff = 300 },
        };
        world.AddChild(ringField);
        int inRing = 0, outOfRing = 0;
        for (int x = 150; x < 250; x += 3)
            for (int y = -3; y < 3; y++)
                for (int z = -6; z < 6; z += 2)
                    foreach (RockField.Rock r in ringField.RocksIn(x, y, z))
                    {
                        Vector3D p = ringField.FieldPoint(ringField.Where(r));
                        double radius = Math.Sqrt(p.X * p.X + p.Z * p.Z);
                        bool inside = radius >= 80_000 && radius <= 120_000 && Math.Abs(p.Y) <= 750;
                        if (inside) inRing++; else outOfRing++;
                    }
        Check($"a ring's rocks lie in the ring ({inRing} in, {outOfRing} out)", inRing > 100 && outOfRing == 0);
        // A rock 100 km out with a clear way to it: solid where it is drawn
        RockField.Rock far = default;
        Vector3 farFrom = Vector3.Zero, farHitAt = Vector3.Zero;
        for (int x = 200; far.Scale == 0 && x < 220; x++)
            foreach (RockField.Rock r in ringField.RocksIn(x, 0, 0))
                if (Approach(ringField, r, out farHitAt, out _, out farFrom))
                {
                    far = r;
                    break;
                }
        Vector3 farAt = ringField.Where(far);
        Check($"a rock 100 km out is solid where it is drawn (hit {farHitAt.DistanceTo(farAt):0.0} m from its middle, reach {far.Reach:0.0} m)",
              far.Scale > 0 && farHitAt.DistanceTo(farAt) <= far.Reach && farHitAt.DistanceTo(farAt) > 0.2f * far.Reach);

        // A mined rock is gone, for good
        Check("a rock taken away is gone", ringField.Remove(far.Id) && !ringField.RocksIn(far.Id.X, far.Id.Y, far.Id.Z).Any(r => r.Id == far.Id));
        ringField.Segment(farFrom, farAt, out _, out _, out RockId after);
        Check("and nothing stops at it any more", after != far.Id);
        ringField.QueueFree();
        await Frames(1);

        // ---------- the cost ----------
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++)
        {
            near.Clear();
            a.Overlap(new Vector3(rng.RandfRange(-2000, 2000), rng.RandfRange(-600, 600), rng.RandfRange(-22000, -18000)), 30, near);
        }
        double overlapUs = watch.Elapsed.TotalMilliseconds * 1000 / 2000;
        watch.Restart();
        int blocked = 0;
        for (int i = 0; i < 200; i++)
        {
            Vector3 from = new Vector3(rng.RandfRange(-3000, 3000), rng.RandfRange(-700, 700), -15_000);
            if (RockField.Blocked(from, from + new Vector3(rng.RandfRange(-2000, 2000), 0, -10_000)))
                blocked++;
        }
        double segmentUs = watch.Elapsed.TotalMilliseconds * 1000 / 200;
        watch.Restart();
        for (int i = 0; i < 1000; i++)
        {
            Vector3 radarAt = new Vector3(rng.RandfRange(-3000, 3000), rng.RandfRange(-700, 700), -15_000);
            Vector3 echoAt = new Vector3(rng.RandfRange(-3000, 3000), rng.RandfRange(-700, 700), rng.RandfRange(-23_000, -17_000));
            RockField.ClutterAll(radarAt, new Vector3(0, 0, -200), echoAt, Vector3.Zero, Mathf.DegToRad(2.5f), 15, 8);
        }
        double clutterUs = watch.Elapsed.TotalMilliseconds;
        GD.Print($"cost: a ship's overlap {overlapUs:0.0} us, a 10 km line of sight across the field {segmentUs:0} us ({blocked} of 200 blocked), a target's clutter {clutterUs:0.0} us");
        Check("a target's clutter costs under 100 us", clutterUs < 100);
        Check("a ship's overlap costs under 50 us", overlapUs < 50);
        Check("a 10 km line of sight costs under 3 ms", segmentUs < 3000);
        a.QueueFree();
        await Frames(1);

        // ---------- ships meeting rocks ----------
        RockField big = new RockField
        {
            Name = "Big", Kinds = new Godot.Collections.Array<RockKind> { kinds[6] }, Seed = 3, CellSize = 1000, RocksPerCell = 1,
            MinScale = 6, MaxScale = 6, Region = new RockBox { Size = new Vector3(3000, 3000, 3000), Falloff = 0 },
            Position = new Vector3(0, 0, -50_000),
        };
        world.AddChild(big);
        var rounds = new Projectiles { Name = "Projectiles" };
        world.AddChild(rounds);
        var ship = new ShipBody { Name = "Ship", Position = new Vector3(0, 0, 1000) };
        ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        ship.AddChild(GD.Load<PackedScene>("res://scenes/ships/fighter_03_hitboxes.tscn").Instantiate<Node3D>());
        ship.GetNode<Node3D>("Hitboxes").Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
        var hull = new ShipHull { Name = "Hull", Integrity = 1e7f, Insured = false };
        ship.AddChild(hull);
        world.AddChild(ship);
        await Frames(2);

        // Straight at the rock's face, nose first
        Vector3 rock = Vector3.Zero, start = Vector3.Zero;
        bool found = false;
        for (int x = -2; !found && x < 2; x++)
            for (int y = -2; !found && y < 2; y++)
                for (int z = -2; !found && z < 2; z++)
                    foreach (RockField.Rock r in big.RocksIn(x, y, z))
                        if (Approach(big, r, out rock, out Vector3 face, out _))
                        {
                            start = rock + face * 400;
                            found = true;
                            break;
                        }
        if (!found)
            Check("found a rock to fly at", false);
        else
        {
            Vector3 toRock = (rock - start).Normalized();
            Quaternion noseFirst = Basis.LookingAt(toRock, Mathf.Abs(toRock.Y) > 0.9f ? Vector3.Right : Vector3.Up).GetRotationQuaternion();
            foreach (float speed in new[] { 20f, 400f })
            {
                float before = hull.CrashDamage;
                ship.State = new ShipState { Position = start, Orientation = noseFirst, Velocity = toRock * speed, AngularVelocity = Vector3.Zero };
                int frames = (int)((start.DistanceTo(rock) / speed + 1.5f) * 60);
                bool through = false;
                for (int f = 0; f < frames; f++)
                {
                    await Frames(1);
                    if ((ship.GlobalPosition - rock).Dot(toRock) > 0)
                        through = true;
                }
                float joules = (hull.CrashDamage - before) * hull.CrashJoulesPerPoint;
                float headOn = 0.5f * ship.Spec.Mass * speed * speed * (1 - ship.Spec.Restitution * ship.Spec.Restitution);
                Check($"at {speed} m/s the ship bounces off the rock (now {ship.LinearVelocity.Dot(toRock):0.0} m/s towards it)",
                      !through && ship.LinearVelocity.Dot(toRock) < 0.5f);
                Check($"and it does not set the ship turning ({ship.AngularVelocity.Length():0.000} rad/s)", ship.AngularVelocity.Length() < 1e-3f);
                Check($"and the crash takes {joules / 1e6:0.00} MJ from the hull ({joules / headOn:P0} of what a head-on stop to the bounce would)",
                      joules > 0.2f * headOn && joules < 1.05f * headOn);
            }

            // A round into the rock goes no farther
            ship.State = new ShipState { Position = start + new Vector3(0, 5000, 0), Orientation = Quaternion.Identity };
            rounds.Fire(start, (rock - start).Normalized() * 1200, 10);
            await Frames(2);
            int flying = rounds.Flying;
            await Frames((int)(start.DistanceTo(rock) / 1200 * 60) + 5);
            Check($"a round fired at the rock stops on it ({flying} then {rounds.Flying} flying)", flying == 1 && rounds.Flying == 0);

            // Radars and seekers see nothing through it
            Vector3 beyond = rock + (rock - start);
            Check("a line of sight through the rock is blocked", RockField.Blocked(start, beyond));
            Vector3 side = (rock - start).Cross(Vector3.Up).Normalized() * 2000;
            Check("one well clear of it is not", !RockField.Blocked(start + side, beyond + side));

            // Clutter: a radar 3 km out loses a target sitting still by the
            // rock's face in the rock's echo, but not one moving along its
            // line of sight, nor one sitting still well away from the rock
            Vector3 out_ = (start - rock).Normalized();
            var radar = new RadarDevice { Name = "Radar", Position = rock + out_ * 3000 };
            world.AddChild(radar);
            radar.LookAt(rock, Mathf.Abs(out_.Y) > 0.9f ? Vector3.Right : Vector3.Up);
            var target = new TestEcho { Name = "Echo", Position = rock + out_ * 20 };
            world.AddChild(target);
            await Frames(1);
            bool inView = !RockField.Blocked(radar.GlobalPosition, target.GlobalPosition);
            float still = radar.Snr(target);
            target.Velocity = out_ * 40;
            await Frames(1);
            float moving = radar.Snr(target);
            target.Velocity = Vector3.Zero;
            target.Position = rock + out_ * 20 + side;
            await Frames(1);
            float apart = radar.Snr(target);
            GD.Print($"clutter: SNR still by the rock {still:0.0}, moving {moving:0.0}, still 2 km from it {apart:0.0} (lock {radar.LockSnr}, detect {radar.DetectSnr})");
            Check("a target sitting still by a rock is lost in its echo (in plain view)", inView && still < radar.LockSnr);
            Check("one moving along the line of sight is seen", moving > radar.DetectSnr);
            Check("one sitting still away from rocks is seen", apart > radar.DetectSnr && moving > 0.8f * apart);
            radar.QueueFree();
            target.QueueFree();
        }

        // ---------- drawing ----------
        var camera = new Camera3D { Name = "Camera", Position = new Vector3(0, 0, -48_000), Current = true };
        world.AddChild(camera);
        await Frames(20);
        Check($"chunks are drawn round the camera ({big.ChunksShown})", big.ChunksShown > 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static RockField Field(Node world, string name, Godot.Collections.Array<RockKind> kinds, int seed, Vector3 at)
    {
        var field = new RockField
        {
            Name = name, Kinds = kinds, Seed = seed, CellSize = 400, RocksPerCell = 10, Position = at,
            Region = new RockBox { Size = new Vector3(7000, 2000, 7000), Falloff = 1200 },
        };
        world.AddChild(field);
        return field;
    }

    /// <summary>Every number of every rock in a block of cells, hashed.</summary>
    private static ulong Hash(RockField f)
    {
        ulong h = 1469598103934665603UL;
        void Add(float v)
        {
            h ^= BitConverter.SingleToUInt32Bits(v);
            h *= 1099511628211UL;
        }
        for (int x = -3; x < 3; x++)
            for (int y = -2; y < 2; y++)
                for (int z = -3; z < 3; z++)
                    foreach (RockField.Rock r in f.RocksIn(x, y, z))
                    {
                        Add(r.Offset.X); Add(r.Offset.Y); Add(r.Offset.Z);
                        Add(r.Turn.X); Add(r.Turn.Y); Add(r.Turn.Z); Add(r.Turn.W);
                        Add(r.Scale); Add(r.Kind); Add(r.Reach);
                    }
        return h;
    }

    /// <summary>
    /// A clear way to a rock along one of the axes: where it meets the rock's
    /// face, the face's normal there, and where the way starts (outside the
    /// rock's reach). The face must be square to the way, so a ship flying it
    /// strikes head on.
    /// </summary>
    private static bool Approach(RockField f, in RockField.Rock r, out Vector3 hitAt, out Vector3 face, out Vector3 from)
    {
        Vector3[] ways = { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down };
        Vector3 middle = f.Where(r);
        foreach (Vector3 way in ways)
        {
            from = middle + way * (r.Reach + 100);
            if (!f.Segment(from, middle, out float t, out face, out RockId first) || first != r.Id || t <= 0 || face.Dot(way) < 0.95f)
                continue;
            hitAt = from + (middle - from) * t;
            // Nothing else near the way for a ship's width
            Vector3 side = way.Cross(Mathf.Abs(way.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized() * 8;
            Vector3 up = way.Cross(side).Normalized() * 8;
            Vector3 far = hitAt + way * 400;
            bool clear = true;
            foreach (Vector3 o in new[] { side, -side, up, -up })
                if (f.Segment(far + o, hitAt + o - way * 2, out _, out _, out RockId other) && other != r.Id)
                    clear = false;
            if (clear)
                return true;
        }
        hitAt = face = from = Vector3.Zero;
        return false;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}

/// <summary>A bare radar echo the test can place and move.</summary>
public partial class TestEcho : Node3D, IRadarTarget
{
    private static ushort _ids = 60000;
    public ushort RadarId { get; } = _ids++;
    public Vector3 Velocity { get; set; }
    public bool Alive => true;
    public float RadarCrossSection => 3f;
    public void Paint() { }
}
