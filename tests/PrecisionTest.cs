using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Far out in a sector, ships near a guest do not shake on its screen. The
/// host runs the sector the guest is in and flies a drone near it, turning
/// at 2 g at 200 m/s; the guest shows it from the snapshots. Its
/// jitter is how far what the guest shows strays, tick to tick, from a
/// smooth curve through it (a quadratic over 21 ticks): what an eye sees as
/// shaking. Single-precision physics far from the origin rounds every step
/// of a turning ship differently. Measured with the guest near the sector's
/// centre, 300 km out (the host's origin there following the guest), and
/// 300 km out with the origin left at the centre, which is what the
/// following prevents.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/precision_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class PrecisionTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShip = NetIds.PlayerShip(GuestPeer);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class CodeShip : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;

        public ShipSnapshot Snapshot(uint tick) => new() { Tick = tick, Position = Position, Orientation = Quaternion.Identity };

        public void TakeHit(HitEvent hit) { }
    }

    private SimulatedNetwork _net;
    private Session _guest;
    private CodeShip _ship;
    private SectorHost _host;
    private SessionNode _node;
    private readonly Dictionary<ushort, CodeShip> _seen = new();

    public override async void _Ready()
    {
        var map = new SectorMap { Start = "a" };
        map.Sectors.Add(new Sector { Id = "a", Title = "A", Scene = "res://tests/fixtures/sectors/a.tscn" });
        map.Sectors.Add(new Sector { Id = "b", Title = "B", Scene = "res://tests/fixtures/sectors/b.tscn" });
        _host = new SectorHost { Map = map, ShipScene = GD.Load<PackedScene>("res://scenes/ships/player_ship.tscn") };
        AddChild(_host);
        var drone = GD.Load<PackedScene>("res://tests/fixtures/turning_drone.tscn");
        _node = new SessionNode { Name = "SessionNode" };
        _node.Spawnables.Add(drone);
        _host.AddChild(_node);
        await Frames(2);

        _net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        _node.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _guest = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        _guest.SpawnDynamic = (id, _) => _seen[id] = new CodeShip { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
        _guest.Left += e => _seen.Remove(e.NetId);
        _ship = new CodeShip { NetId = GuestShip, IsLocal = true, Position = new SectorPoint(0, 0, 0) };
        _guest.Move(1);
        _guest.Add(_ship);

        float near = await Jitter(drone, new SectorPoint(0, 0, 0), follow: true);
        float far = await Jitter(drone, new SectorPoint(300_000, 0, 0), follow: true);
        float unfollowed = await Jitter(drone, new SectorPoint(300_000, 0, 0), follow: false);
        GD.Print($"jitter (mm RMS off a smooth curve, as the guest shows it): near the centre {near:0.00}, " +
                 $"300 km out {far:0.00}, 300 km out with the origin left at the centre {unfollowed:0.00}");
        Check($"300 km out, the drone shows as steady as near the centre ({far:0.00} vs {near:0.00} mm)", far <= near * 1.5f + 1);
        Check($"the measure sees the shaking the following prevents ({unfollowed:0.00} mm)", unfollowed > far * 3 && unfollowed > 3);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // The guest waits at a point of sector B; the host flies a drone straight past it; what the guest shows of it
    private async Task<float> Jitter(PackedScene scene, SectorPoint at, bool follow)
    {
        _ship.Position = at;
        _node.FollowsPlayers = follow;
        await Run(90);                                  // the host runs B, with the guest's ship in it
        FloatingOrigin b = _host.Root("b");
        if (!follow)
        {
            b.Ship = null;
            b.Shift(-new Vector3((float)b.Origin.X, (float)b.Origin.Y, (float)b.Origin.Z));    // back to the centre
        }
        var drone = scene.Instantiate<Drone>();
        drone.GetNode<NetBody>("NetBody").Spawned = true;
        drone.Transform = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), _node.Scene(at + new Vector3(-300, 50, -200), 1));
        b.AddChild(drone);
        await Run(60);                                  // the guest hears of it and its track fills

        CodeShip shown = null;
        foreach (CodeShip c in _seen.Values)
            shown = c;
        var positions = new List<SectorPoint>();
        for (int i = 0; i < 180 && shown != null; i++)
        {
            await Run(1);
            if (shown.Track.ShownTick.HasValue)
                positions.Add(shown.Track.Sample().Position);
        }
        drone.QueueFree();
        await Run(30);
        if (positions.Count < 100)
        {
            Check($"the guest shows the drone ({positions.Count} ticks)", false);
            return float.MaxValue;
        }
        // Savitzky-Golay: the value at the middle of a quadratic fitted to 2m+1 points, as weights
        const int m = 10;
        var weights = new double[2 * m + 1];
        double norm = (2 * m + 3) * (2 * m + 1) * (2 * m - 1);
        for (int k = -m; k <= m; k++)
            weights[k + m] = (3 * (3 * m * m + 3 * m - 1) - 15 * k * k) / norm;
        double sum = 0;
        int count = 0;
        for (int i = m; i + m < positions.Count; i++)
        {
            double x = 0, y = 0, z = 0;
            for (int k = -m; k <= m; k++)
            {
                SectorPoint p = positions[i + k];
                x += weights[k + m] * p.X;
                y += weights[k + m] * p.Y;
                z += weights[k + m] * p.Z;
            }
            double dx = positions[i].X - x, dy = positions[i].Y - y, dz = positions[i].Z - z;
            sum += dx * dx + dy * dy + dz * dz;
            count++;
        }
        return (float)(Math.Sqrt(sum / count) * 1000);
    }

    private async Task Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            _net.Now = tick * Dt;
            _guest.Tick(tick);
        }
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
