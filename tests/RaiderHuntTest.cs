using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The game's fighter on a host, hunting a guest's ship it was never told
/// of. This scene is the host: the raider (scenes/ships/ai_fighter.tscn)
/// and a drone, both on the "raiders" datalink, and the guest's ship as
/// the session shows it here (a RemoteShip, an echo like any other). The
/// guest is plain code over a simulated network, flying across ahead.
///
///   - the raider's radar finds the guest's ship and locks it; the drone's
///     radar finds it too: both go for it, neither for the other;
///   - the raider's rounds hit it, judged by the host on the guest's
///     hitboxes as they were when the raider saw them, and the guest hears
///     of every hit;
///   - a missile it lets go is spawned on the guest's machine;
///   - the raider and the drone never hurt each other.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/raider_hunt_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class RaiderHuntTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer);
    private int _failures;

    /// <summary>The guest's own ship, on the guest's machine.</summary>
    private sealed class Ship : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal => true;
        public RemoteTrack Track => null;
        public Hitbox[] Hitboxes { get; } = { new(Vector3.Zero, new Vector3(5, 3, 10)) };
        public SectorPoint Position;
        public Vector3 Velocity;
        public Quaternion Orientation = Quaternion.Identity;
        public int Hits;
        public float Damage;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Orientation, Velocity = Velocity,
            Thrust = 0.6f, Radar = RadarMode.Search,
        };

        public void TakeHit(HitEvent hit)
        {
            Hits++;
            Damage += hit.Damage;
        }
    }

    public override async void _Ready()
    {
        var origin = new FloatingOrigin { Name = "World" };
        origin.AddChild(new SectorClock());
        origin.AddChild(new Projectiles());
        var hostNode = new SessionNode
        {
            Spawnables = new() { Weapon("missile"), Weapon("ir_missile"), Weapon("chaff"), Weapon("flare") },
            RemoteShipScene = GD.Load<PackedScene>("res://scenes/ships/remote_fighter.tscn"),
        };
        AddChild(origin);

        var raider = GD.Load<PackedScene>("res://scenes/ships/ai_fighter.tscn").Instantiate<ShipBody>();
        raider.Name = "Raider";
        origin.AddChild(raider);
        var drone = new Drone
        {
            Name = "Drone", Attacks = true, Evades = false, Defends = false, Health = 100_000,
            MissileScene = Weapon("ir_missile"),
            Transform = new Transform3D(Basis.Identity, new Vector3(300, 0, 400)),     // behind the raider, its wingman
        };
        drone.AddChild(new NetBody { OwnedBy = NetOwner.Host });
        drone.AddChild(new Datalink { Channel = "raiders" });
        drone.AddChild(new RadarEmitter { Name = "Radar" });
        origin.AddChild(drone);
        origin.AddChild(hostNode);
        await Frames(1);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        hostNode.Start(Session.HostPeer, net.Join(Session.HostPeer));
        var guest = new Session(GuestPeer, net.Join(GuestPeer)) { TickSeconds = Dt };
        var ship = new Ship { NetId = GuestShipId, Position = new SectorPoint(-500, 100, -3000) };
        guest.Add(ship);
        var spawned = new List<byte>();
        guest.SpawnDynamic = (id, scene) =>
        {
            spawned.Add(scene);
            return null;
        };
        var heard = new List<HitEvent>();
        guest.HitConfirmed += heard.Add;

        var pilot = raider.GetNode<FighterAi>("Pilot");
        var radar = raider.GetNode<RadarDevice>("Radar");
        var launcher = raider.GetNode<LauncherDevice>("Launcher");
        var hull = raider.GetNode<ShipHull>("Hull");
        RemoteShip seen = null;
        bool booted = false, raiderLocked = false, droneHunts = false, droneOnFriend = false, raiderOnDrone = false;
        float closest = float.MaxValue;
        for (int frame = 0; frame < 150 * 60 && (ship.Hits < 8 || launcher.Launched == 0); frame++)
        {
            await Frames(1);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            booted |= raider.Computer.ScreenText().Contains("Threat warning resident");
            if (booted)
            {
                ship.Velocity = new Vector3(150, 0, 0);     // across, ahead of the raider
                ship.Position += ship.Velocity * Dt;
            }
            guest.Tick(tick);

            seen ??= origin.GetNodeOrNull<RemoteShip>("Player1");
            if (seen == null)
                continue;
            raiderLocked |= radar.Locked == seen && pilot.Target == seen;
            raiderOnDrone |= pilot.Target == drone;
            droneHunts |= drone.Quarry == seen;
            droneOnFriend |= drone.Quarry == raider.GetNode<ShipSignature>("Signature");
            closest = Math.Min(closest, raider.GlobalPosition.DistanceTo(seen.GlobalPosition));
        }

        Check("the guest's ship shows on the host as a ship of its own", seen != null);
        Check($"the raider's computer is up ({booted})", booted);
        Check($"its radar finds the guest's ship and locks it ({raiderLocked}, closing to {closest:0} m)", raiderLocked && closest < 1500);
        Check($"the drone's radar finds it too and goes for it ({droneHunts})", droneHunts);
        Check($"neither goes for the other (raider on the drone: {raiderOnDrone}, drone on the raider: {droneOnFriend})", !raiderOnDrone && !droneOnFriend);
        int confirmed = heard.Count(h => h.Target == GuestShipId);
        Check($"its rounds hit the guest, judged by the host ({ship.Hits} hits, {ship.Damage:0} damage; {confirmed} heard)",
              ship.Hits >= 8 && confirmed == ship.Hits);
        Check($"a missile it lets go is made on the guest's machine ({launcher.Launched} fired, {spawned.Count} spawned there)",
              launcher.Launched >= 1 && spawned.Count >= 1);
        Check($"the raider and the drone never hurt each other (hull {hull.Left:0} of {hull.Integrity:0}, drone hit {drone.Hits} times)",
              hull.Left == hull.Integrity && drone.Hits == 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static PackedScene Weapon(string name) => GD.Load<PackedScene>($"res://scenes/weapons/{name}.tscn");

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
