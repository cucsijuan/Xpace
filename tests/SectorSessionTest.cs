using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A session over two sectors. This scene is the host: the game's shell
/// (<see cref="SectorHost"/>, two small sectors) and its SessionNode; a
/// guest runs beside it as plain code over a simulated network and goes
/// from sector to sector. The host runs the sector the guest is in, with
/// the guest's ship in that sector's world and the sector's origin
/// following it (300 km out, its ship stays near the origin here), and
/// lets go of a sector nobody is in; each machine sees only the ships of
/// its own sector.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/sector_session_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SectorSessionTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort HostShip = NetIds.PlayerShip(Session.HostPeer), GuestShip = NetIds.PlayerShip(GuestPeer);
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private int _failures;

    private sealed class CodeShip : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes => new[] { Box };
        public SectorPoint Position;
        public Vector3 Velocity;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Quaternion.Identity, Velocity = Velocity,
        };

        public void TakeHit(HitEvent hit) { }
    }

    private SimulatedNetwork _net;
    private Session _guest;
    private CodeShip _ship;
    private SectorHost _host;

    public override async void _Ready()
    {
        var map = new SectorMap { Start = "a" };
        map.Sectors.Add(new Sector { Id = "a", Title = "A", Scene = "res://tests/fixtures/sectors/a.tscn" });
        map.Sectors.Add(new Sector { Id = "b", Title = "B", Scene = "res://tests/fixtures/sectors/b.tscn" });
        _host = new SectorHost { Map = map, ShipScene = GD.Load<PackedScene>("res://scenes/ships/player_ship.tscn") };
        AddChild(_host);
        var node = new SessionNode { Name = "SessionNode" };
        _host.AddChild(node);
        await Frames(2);

        _net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        node.Start(Session.HostPeer, _net.Join(Session.HostPeer));
        _guest = new Session(GuestPeer, _net.Join(GuestPeer)) { TickSeconds = Dt };
        var seen = new Dictionary<ushort, CodeShip>();
        _guest.SpawnRemote = id => seen[id] = new CodeShip { NetId = id, Track = new RemoteTrack { TickSeconds = Dt } };
        _guest.Left += e => seen.Remove(e.NetId);
        _ship = new CodeShip { NetId = GuestShip, IsLocal = true, Position = new SectorPoint(200, 0, -300), Velocity = new Vector3(0, 0, -20) };
        _guest.Move(0);
        _guest.Add(_ship);

        // Both in sector A
        await Run(180);
        Check("the guest's ship is in sector A here", Puppet("a") != null);
        Check("the guest sees the host's ship", seen.ContainsKey(HostShip) && _guest.Entity(HostShip) != null);

        // The guest goes to sector B, 300 km out: the host runs B for it
        _guest.Move(1);
        _ship.Position = new SectorPoint(300_000, 0, 0);
        await Run(180);
        RemoteShip inB = Puppet("b");
        Check("the host runs sector B for the guest", _host.Root("b") != null && _host.Here.Id == "a");
        Check("the guest's ship is in sector B's world, not A's", inB != null && Puppet("a") == null && inB.GetWorld3D() != _host.Ship.GetWorld3D());
        FloatingOrigin b = _host.Root("b");
        Check($"sector B's origin follows the guest's ship ({(inB?.GlobalPosition.Length() ?? -1):0} m from it, origin x {b?.Origin.X:0})",
              inB != null && b.Ship == inB && inB.GlobalPosition.Length() < 1100 && Math.Abs(b.Origin.X - 300_000) < 1100);
        Check("the guest no longer sees the host's ship, in another sector", !seen.ContainsKey(HostShip) && _guest.Entity(HostShip) == null);

        // The host goes to sector B too, beside the guest: nobody is left in A
        Vector3 beside = node.Scene(new SectorPoint(300_200, 0, 0), 1);
        _host.Jump("b", new Transform3D(Basis.Identity, beside));
        await Run(180);
        Check("sector A is let go, nobody being there", _host.Root("a") == null && _host.Here.Id == "b");
        Check("the guest sees the host's ship again", seen.ContainsKey(HostShip));
        Check($"and where it is (x {(seen.TryGetValue(HostShip, out CodeShip h) && h.Track.ShownTick.HasValue ? h.Track.Sample().Position.X : 0):0})",
              h != null && h.Track.ShownTick.HasValue && Math.Abs(h.Track.Sample().Position.X - 300_200) < 50);

        // The guest goes back to A: the host runs it again, and B stays (the host is there)
        _guest.Move(0);
        _ship.Position = new SectorPoint(0, 0, -500);
        await Run(180);
        Check("the host runs sector A again for the guest", _host.Root("a") != null && _host.Root("b") != null && Puppet("a") != null);
        Check("the guest's ship left sector B here", Puppet("b") == null);
        Check("the guest does not see the host's ship", !seen.ContainsKey(HostShip));

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private RemoteShip Puppet(string sector) => _host.Root(sector)?.GetNodeOrNull<RemoteShip>($"Player{GuestPeer}");

    // The guest's machine runs beside the host's for so many ticks; its ship flies straight
    private async Task Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            _net.Now = tick * Dt;
            _ship.Position += _ship.Velocity * Dt;
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
