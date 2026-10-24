using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Xpace.Net;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Missiles, blasts, chaff, flares and radar locks across a session, from
/// both sides (the other machine is plain code over a simulated network).
///
/// As a guest: this scene fires a real heat seeker at its puppet of the
/// host's ship; the host sees the missile fly, judges its blast and its
/// ship takes the damage; chaff dropped here reaches the host and a flare
/// the host drops is made here; the host's radar, locked on this machine's
/// ship, paints it here and shows on the puppet's emitter.
///
/// As the host: a guest's missile (its snapshots) flies here as a puppet
/// missile; the drone's warner sees its motor, the drone breaks and drops
/// chaff and flares that reach the guest; the guest's radar lock paints the
/// drone; the blast the guest sends hurts it.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/net_missile_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class NetMissileTest : Node
{
    private const float Dt = 1f / 60;
    private const byte GuestPeer = 1;
    private static readonly ushort GuestShipId = NetIds.PlayerShip(GuestPeer), HostShipId = SessionNode.FirstHostId;
    private static readonly Hitbox Box = new(Vector3.Zero, new Vector3(5, 3, 10));
    private static readonly Basis Crossing = new(Vector3.Up, -Mathf.Pi / 2);     // nose along +X
    private int _failures;

    /// <summary>Something one machine has as plain code: its own, or a copy of another's.</summary>
    private sealed class Thing : INetEntity
    {
        public ushort NetId { get; init; }
        public bool IsLocal { get; init; }
        public RemoteTrack Track { get; init; }
        public Hitbox[] Hitboxes { get; init; } = { Box };
        public SectorPoint Position;
        public Vector3 Velocity;
        public Quaternion Orientation = Quaternion.Identity;
        public float Thrust;
        public RadarMode Radar;
        public ushort RadarTarget = ShipSnapshot.NoTarget;
        public float Damage;
        public int Hits;

        public ShipSnapshot Snapshot(uint tick) => new()
        {
            Tick = tick, Position = Position, Orientation = Orientation, Velocity = Velocity,
            Thrust = Thrust, Radar = Radar, RadarTarget = RadarTarget,
        };

        public void TakeHit(HitEvent hit)
        {
            Hits++;
            Damage += hit.Damage;
        }
    }

    /// <summary>The player's ship in the guest scene: counts what hits it and the radars that lock it.</summary>
    private partial class Hull : Node3D, IDamageable, IRadarTarget
    {
        public int Hits, Paints;
        public ushort RadarId => 999;
        public Vector3 Velocity => Vector3.Zero;
        public bool Alive => true;
        public float RadarCrossSection => 4;

        public void Hit(float damage) => Hits++;
        public void Paint() => Paints++;
    }

    public override async void _Ready()
    {
        await AsGuest();
        await AsHost();
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static PackedScene Scene(string name) => GD.Load<PackedScene>($"res://scenes/weapons/{name}.tscn");

    private static (FloatingOrigin, SessionNode) World()
    {
        var origin = new FloatingOrigin { Name = "World" };
        origin.AddChild(new SectorClock());
        origin.AddChild(new Projectiles());
        var session = new SessionNode
        {
            Spawnables = new() { Scene("missile"), Scene("ir_missile"), Scene("chaff"), Scene("flare") },
        };
        return (origin, session);
    }

    private async Task AsGuest()
    {
        (FloatingOrigin origin, SessionNode guest) = World();
        AddChild(origin);
        var drone = new Drone { Name = "Drone", Health = 100_000 };     // the host's ship, as a puppet here
        drone.AddChild(new NetBody { OwnedBy = NetOwner.Host });
        var emitter = new RadarEmitter { Name = "Radar" };
        drone.AddChild(emitter);
        origin.AddChild(drone);
        var hull = new Hull { Name = "Ship" };                       // this machine's player, standing still
        hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        hull.AddChild(new NetBody { OwnedBy = NetOwner.Pilot });
        origin.AddChild(hull);
        origin.AddChild(guest);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        var host = new Session(Session.HostPeer, net.Join(Session.HostPeer)) { TickSeconds = Dt };
        var hostShip = new Thing                                    // flying away, its motor towards this machine
        {
            NetId = HostShipId, IsLocal = true, Position = new SectorPoint(0, 0, -1000), Velocity = new Vector3(0, 0, -150),
        };
        host.Add(hostShip);
        host.Add(new Thing { NetId = GuestShipId, Track = new RemoteTrack { TickSeconds = Dt } });
        var made = new Dictionary<ushort, (byte Scene, Thing Copy)>();
        host.SpawnDynamic = (id, scene) =>
        {
            var copy = new Thing { NetId = id, Track = new RemoteTrack { TickSeconds = Dt }, Hitboxes = Array.Empty<Hitbox>() };
            made[id] = (scene, copy);
            return copy;
        };
        var drops = new List<DropEvent>();
        host.Dropped += drops.Add;
        (INetEntity What, bool Exploded)? ended = null;
        host.Despawned += (e, _, exploded) => ended = (e, exploded);
        guest.Start(GuestPeer, net.Join(GuestPeer));

        Missile missile = null;
        float mostThrust = 0;
        bool locked = false, flareHere = false;
        for (int frame = 0; frame < 900; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            host.Tick(tick);
            hostShip.Position += hostShip.Velocity * Dt;
            if (frame == 60)
            {
                hostShip.Radar = RadarMode.Track;                    // the host's radar locks this machine's ship
                hostShip.RadarTarget = GuestShipId;
            }

            if (frame == 120)                                       // a heat seeker at the puppet
            {
                missile = Scene("ir_missile").Instantiate<Missile>();
                missile.Target = drone;
                origin.AddChild(missile);
                Vector3 to = (drone.GlobalPosition - Vector3.Zero).Normalized();
                missile.LookAt(to, Vector3.Up);
                missile.Velocity = to * 250;                        // as from a ship flying after it
                missile.SeekerLook = to;
                Countermeasures.Drop(origin, Scene("chaff"), new Vector3(0, -5, 0), new Vector3(0, 0, 15));
            }
            if (frame == 200)
                host.Drop(tick, 3, new SectorPoint(0, 50, 2000), new Vector3(0, 0, 20));     // well behind: not in the seeker's view
            foreach ((byte _, Thing copy) in made.Values)
                if (copy.Track.ShownTick.HasValue)
                    mostThrust = Math.Max(mostThrust, copy.Track.Sample().Thrust);
            locked |= emitter.Locked && emitter.Target == hull;
            flareHere |= origin.GetChildren().OfType<Flare>().Any();
        }

        GD.Print($"     the heat seeker came within {missile?.ClosestApproach ?? -1:0.0} m of the puppet");
        (byte scene, Thing seen) = made.Values.FirstOrDefault();
        Check($"the host hears of the missile fired here (scene {scene}: the heat seeker) and sees it fly, motor burning ({mostThrust:0.00})",
              made.Count == 1 && scene == 1 && mostThrust > 0.5f);
        Check($"the missile went off here at the puppet ({missile != null && !IsInstanceValid(missile)}); the host hears it end, blown up ({ended?.Exploded})",
              missile != null && !IsInstanceValid(missile) && ended?.What == seen && ended?.Exploded == true);
        Check($"the host judged the blast: its ship took {hostShip.Hits} hit, {hostShip.Damage:0} damage", hostShip.Hits == 1 && hostShip.Damage > 30);
        Check("the puppet took no damage of its own", drone.Hits == 0);
        Check($"chaff dropped here reaches the host ({drops.Count} drops, scene {drops.FirstOrDefault().Scene})",
              drops.Count == 1 && drops[0].Scene == 2 && drops[0].Dropper == GuestPeer);
        Check("a flare the host drops is made here", flareHere);
        Check($"the host's radar lock paints this machine's ship ({hull.Paints} steps) and shows on the puppet's radar ({locked})",
              hull.Paints > 300 && locked);

        origin.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task AsHost()
    {
        (FloatingOrigin origin, SessionNode hostNode) = World();
        AddChild(origin);
        var drone = new Drone
        {
            Name = "Drone", Straight = true, Evades = false, Speed = 200, Health = 100_000,
            Transform = new Transform3D(Crossing, new Vector3(-600, 0, -1000)),
        };
        drone.AddChild(new NetBody { OwnedBy = NetOwner.Host });
        origin.AddChild(drone);
        origin.AddChild(hostNode);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        var net = new SimulatedNetwork { Latency = 0.06, Jitter = 0.02, Loss = 0.05 };
        hostNode.Start(Session.HostPeer, net.Join(Session.HostPeer));
        var guest = new Session(GuestPeer, net.Join(GuestPeer)) { TickSeconds = Dt };
        var ship = new Thing { NetId = GuestShipId, IsLocal = true, Radar = RadarMode.Search };
        guest.Add(ship);
        var seenDrone = new Thing { NetId = HostShipId, Track = new RemoteTrack { TickSeconds = Dt } };
        guest.Add(seenDrone);
        var drops = new List<DropEvent>();
        guest.Dropped += drops.Add;
        var hits = new List<HitEvent>();
        guest.HitConfirmed += hits.Add;

        Thing missile = null;
        float worst = 0;
        bool puppetSeen = false, burning = false, warned = false, painted = false, gone = false, emitterOn = false;
        var truth = new Dictionary<uint, SectorPoint>();
        for (int frame = 0; frame < 900; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            uint tick = SectorClock.Instance.Tick;
            net.Now = tick * Dt;
            guest.Tick(tick);

            // The guest's radar locks the drone it sees; then it fires
            if (frame == 60)
            {
                ship.Radar = RadarMode.Track;
                ship.RadarTarget = HostShipId;
            }
            if (frame == 90)
            {
                missile = new Thing { NetId = guest.NewDynamicId(), IsLocal = true, Hitboxes = Array.Empty<Hitbox>(), Thrust = 1 };
                guest.Spawn(missile, 0);
            }
            if (missile != null && guest.Entity(missile.NetId) == missile && seenDrone.Track.ShownTick.HasValue)
            {
                // Pure pursuit with lead at 700 m/s, and a proximity fuse on the drone the guest sees
                RemoteTrack.Pose d = seenDrone.Track.Sample();
                Vector3 rel = missile.Position.To(d.Position);
                missile.Velocity = Lead(rel, d.Velocity, 700) * 700;
                Vector3 relV = d.Velocity - missile.Velocity;
                float at = Math.Clamp(-rel.Dot(relV) / relV.LengthSquared(), 0, Dt);
                float miss = (rel + relV * at).Length();
                if (at < Dt && miss < 12)
                {
                    guest.Blast(tick, at / Dt, missile.NetId, missile.Position + missile.Velocity * at, 150, 6, 25);
                    guest.Despawn(missile.NetId, tick, true);
                }
                else
                {
                    truth[tick] = missile.Position;
                    missile.Position += missile.Velocity * Dt;
                }
            }

            // On the host: the puppet missile, where the guest flew it
            Missile puppet = origin.GetChildren().OfType<Missile>().FirstOrDefault();
            if (puppet != null && NetBody.Of(puppet)?.Track?.ShownTick is double shown &&
                truth.TryGetValue((uint)Math.Floor(shown), out SectorPoint a) && truth.TryGetValue((uint)Math.Floor(shown) + 1, out SectorPoint b))
            {
                puppetSeen = true;
                burning |= puppet.Burning;
                Vector3 expected = default(SectorPoint).To(a).Lerp(default(SectorPoint).To(b), (float)(shown - Math.Floor(shown)));
                if (frame > 150)                                    // in flight, past the launch
                    worst = Math.Max(worst, puppet.GlobalPosition.DistanceTo(expected));
            }
            gone |= puppetSeen && puppet == null;
            warned |= drone.MissileWarning;
            painted |= drone.Painted;
            emitterOn |= GetNodeOrNull<RemoteShip>("World/Player1")?.GetNodeOrNull<RadarEmitter>("Radar") is { Locked: true } e && e.Target == drone;
        }

        Check($"the guest's missile flies here as a puppet, motor burning ({burning}), where the guest flew it ({worst:0.00} m off)",
              puppetSeen && burning && worst < 2);
        Check("the drone's warner sees it coming", warned);
        Check($"the guest's radar lock paints the drone here ({painted}) and shows on the guest's ship's radar ({emitterOn})", painted && emitterOn);
        Check($"the drone's chaff and flares reach the guest ({drops.Count(x => x.Scene == 2)} chaff, {drops.Count(x => x.Scene == 3)} flares)",
              drops.Any(x => x.Scene == 2) && drops.Any(x => x.Scene == 3) && drops.All(x => x.Dropper == Session.HostPeer));
        float damage = hits.Where(h => h.Target == HostShipId).Sum(h => h.Damage);
        Check($"the guest's blast hurt the drone ({drone.Hits} hit here; the guest heard {damage:0} damage)", drone.Hits == 1 && damage > 30);
        Check("the puppet missile goes when the guest's ends", gone);

        origin.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static Vector3 Lead(Vector3 p, Vector3 v, float speed)
    {
        float a = v.Dot(v) - speed * speed, b = 2 * p.Dot(v), c = p.Dot(p);
        float t = (-b - MathF.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return (p + v * t).Normalized();
    }

    private void Check(string what, bool ok)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
