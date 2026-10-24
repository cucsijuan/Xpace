using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>Anything that flies in a session, of any kind (ships, missiles): one machine owns it, the others show it.</summary>
public interface INetEntity
{
    ushort NetId { get; }

    /// <summary>This machine owns it: it is simulated here and its snapshots go out from here.</summary>
    bool IsLocal { get; }

    /// <summary>Owned here: its state now.</summary>
    ShipSnapshot Snapshot(uint tick);

    /// <summary>Owned elsewhere: where its snapshots go (the session advances it each tick).</summary>
    RemoteTrack Track { get; }

    /// <summary>What rounds and blasts can hurt (none: nothing can).</summary>
    Hitbox[] Hitboxes { get; }

    /// <summary>The host says a round or a blast hit it. Only the owner's copy takes damage.</summary>
    void TakeHit(HitEvent hit);
}

/// <summary>
/// Network ids: the owning peer in the top 4 bits, then 12 bits of its own.
/// 0 is the peer's player ship; the host numbers the ships in its scene
/// from 1 (up to 255); from <see cref="FirstDynamic"/> on are things a
/// machine makes while it plays (missiles), numbered by that machine.
/// </summary>
public static class NetIds
{
    public const int PeerShift = 12;
    public const ushort FirstDynamic = 256, Mask = 0xFFF;

    /// <summary>The ship a peer's player flies.</summary>
    public static ushort PlayerShip(byte peer) => (ushort)(peer << PeerShift);

    public static byte Owner(ushort id) => (byte)(id >> PeerShift);

    public static bool IsPlayerShip(ushort id) => (id & Mask) == 0;

    public static bool IsDynamic(ushort id) => (id & Mask) >= FirstDynamic;
}

/// <summary>
/// One machine's side of a session, star-shaped round the host (peer 0):
///
/// - everything owned here sends a snapshot every
///   <see cref="SnapshotInterval"/> ticks, guests to the host, the host to
///   every guest (relaying the guests' snapshots to the other guests);
/// - rounds fired here (<see cref="Fire"/>) go to the host as fire events,
///   with how late this machine shows every other ship; the host judges them
///   with its <see cref="LagCompensator"/> and relays them for drawing;
///   warheads that go off here (<see cref="Blast"/>) go to the host the same
///   way, and are judged the same way;
/// - the host's verdicts (hit events) go to everyone; the owner of the ship
///   hit takes the damage;
/// - what a machine makes while it plays and owns (missiles: <see cref="Spawn"/>)
///   is announced to everyone, with the scene that shows it, and so is its
///   end (<see cref="Despawn"/>); a guest joining later hears of what is
///   already flying;
/// - what a ship throws out to fly on its own (chaff, flares: <see cref="Drop"/>)
///   is announced once, and every machine flies it;
/// - guests keep their tick on the host's: they ping it every
///   <see cref="PingInterval"/> ticks (every 6 until the first answer), and
///   move their clock by <see cref="AdjustClock"/> when the best of the
///   recent answers says it is two ticks or more off (at once on the first
///   answer: until then a guest sends and takes no snapshots);
/// - something made for another machine's player ship or spawned thing and
///   silent for <see cref="ForgetSeconds"/> is gone (its owner left).
///
/// The host takes from a guest only what that guest owns.
/// Plain code, no nodes: <see cref="SessionNode"/> joins it to the game.
/// </summary>
public sealed class Session
{
    public const byte HostPeer = 0;
    private const byte KindSnapshot = 1, KindFire = 2, KindHit = 3, KindPing = 4, KindPong = 5,
                       KindSpawn = 6, KindDespawn = 7, KindBlast = 8, KindDrop = 9;
    private const int PingSamples = 5;

    public float TickSeconds = 1f / 60;
    public int SnapshotInterval = 3;
    public int PingInterval = 30;
    public float ForgetSeconds = 3;

    public byte LocalPeer { get; }
    public bool IsHost => LocalPeer == HostPeer;

    /// <summary>This machine's tick is the host's (always, on the host).</summary>
    public bool Synced { get; private set; }

    /// <summary>The last ping's round trip, in ticks (guests).</summary>
    public uint RoundTrip { get; private set; }

    /// <summary>A guest's clock is off the host's: move it by this many ticks.</summary>
    public Action<int> AdjustClock;

    /// <summary>The host's judge of rounds and blasts; null on a guest.</summary>
    public LagCompensator Judge { get; }

    /// <summary>Another player's ship heard of for the first time: make what shows it (or null to ignore it).</summary>
    public Func<ushort, INetEntity> SpawnRemote;

    /// <summary>Another machine made something (id, its scene): make what shows it (or null to ignore it).</summary>
    public Func<ushort, byte, INetEntity> SpawnDynamic;

    /// <summary>A round someone else fired: fly it for show (it does no damage here).</summary>
    public event Action<FireEvent> RoundFired;

    /// <summary>A round or a blast hit something, any machine's.</summary>
    public event Action<HitEvent> HitConfirmed;

    /// <summary>Something spawned elsewhere ended at that tick (blown up, or just gone): show its end, then remove it.</summary>
    public event Action<INetEntity, uint, bool> Despawned;

    /// <summary>Someone else dropped something: make it, already flying.</summary>
    public event Action<DropEvent> Dropped;

    /// <summary>Something made for another machine went silent and was removed: take away what shows it.</summary>
    public event Action<INetEntity> Forgotten;

    private readonly INetTransport _transport;
    private readonly Dictionary<ushort, INetEntity> _entities = new();
    private readonly Dictionary<ushort, RemoteTrack> _histories = new();   // the host's past of its own entities
    private readonly Dictionary<ushort, uint> _heard = new();              // made for others' things: the tick last heard
    private readonly Dictionary<ushort, byte> _spawned = new();            // the host: what flies that was spawned, and its scene
    private readonly HashSet<byte> _known = new();                         // the host: peers already told of it
    private readonly List<(uint Trip, int Offset)> _pings = new();
    private ushort _sequence;
    private ushort _nextDynamic = NetIds.FirstDynamic;

    public Session(byte localPeer, INetTransport transport)
    {
        LocalPeer = localPeer;
        _transport = transport;
        Synced = IsHost;
        if (IsHost)
        {
            Judge = new LagCompensator();
            Judge.Hit += Verdict;
        }
    }

    public IEnumerable<INetEntity> Entities => _entities.Values;

    public INetEntity Entity(ushort id) => _entities.GetValueOrDefault(id);

    public void Add(INetEntity e)
    {
        _entities[e.NetId] = e;
        if (!IsHost)
            return;
        RemoteTrack history = e.Track;
        if (e.IsLocal)                              // a snapshot every tick: 2 s
            _histories[e.NetId] = history = new RemoteTrack { TickSeconds = TickSeconds, Capacity = 120 };
        Judge.Register(e.NetId, history, e.Hitboxes);
    }

    public void Remove(ushort id)
    {
        _entities.Remove(id);
        _histories.Remove(id);
        _heard.Remove(id);
        Judge?.Unregister(id);
    }

    /// <summary>A free id for something this machine makes.</summary>
    public ushort NewDynamicId()
    {
        for (int tries = 0; tries <= NetIds.Mask; tries++)
        {
            ushort n = _nextDynamic;
            _nextDynamic = (ushort)(n >= NetIds.Mask ? NetIds.FirstDynamic : n + 1);
            ushort id = (ushort)(NetIds.PlayerShip(LocalPeer) | n);
            if (!_entities.ContainsKey(id))
                return id;
        }
        throw new InvalidOperationException("no network ids left");
    }

    /// <summary>Something made here joins the session (its id from <see cref="NewDynamicId"/>); the others make <paramref name="scene"/> to show it.</summary>
    public void Spawn(INetEntity e, byte scene)
    {
        Add(e);
        byte[] body = SpawnBody(e.NetId, scene);
        if (IsHost)
        {
            _spawned[e.NetId] = scene;
            Broadcast(KindSpawn, body, reliable: true, except: null);
        }
        else
            Send(HostPeer, KindSpawn, body, reliable: true);
    }

    /// <summary>Something made here is gone, at <paramref name="now"/>; <paramref name="exploded"/>: it blew up.</summary>
    public void Despawn(ushort id, uint now, bool exploded)
    {
        Remove(id);
        byte[] body = DespawnBody(id, now, exploded);
        if (IsHost)
        {
            _spawned.Remove(id);
            Broadcast(KindDespawn, body, reliable: true, except: null);
        }
        else
            Send(HostPeer, KindDespawn, body, reliable: true);
    }

    /// <summary>The views this machine has of everything that can be hit: how late it shows each.</summary>
    private List<FireEvent.View> Views(uint now)
    {
        var views = new List<FireEvent.View>();
        foreach (INetEntity e in _entities.Values)
            if (!e.IsLocal && e.Hitboxes.Length > 0 && e.Track.ShownTick is double shown && views.Count < FireEvent.MaxViews)
                views.Add(new FireEvent.View { Ship = e.NetId, DelayTicks = Math.Max(0, now - shown) });
        return views;
    }

    /// <summary>
    /// A round leaves a muzzle of <paramref name="ship"/> here. Returns the
    /// event as it was sent, quantised: fly that one, so this machine's
    /// round is the host's.
    /// </summary>
    public FireEvent Fire(uint now, ushort ship, SectorPoint origin, Vector3 velocity, float damage)
    {
        var fired = new FireEvent
        {
            Shooter = LocalPeer, Ship = ship, Sequence = _sequence++, Tick = now,
            Origin = origin, Velocity = velocity, Damage = damage, Views = Views(now),
        };
        byte[] bytes = fired.ToBytes();
        FireEvent sent = FireEvent.Read(new BitReader(bytes));
        if (IsHost)
        {
            Judge.Fire(sent);
            Broadcast(KindFire, bytes, reliable: true, except: null);
        }
        else
            Send(HostPeer, KindFire, bytes, reliable: true);
        return sent;
    }

    /// <summary>The warhead of <paramref name="missile"/>, flown here, went off: the host hurts what it reached.</summary>
    public void Blast(uint now, float fraction, ushort missile, SectorPoint at, float damage, float lethalRadius, float blastRadius)
    {
        var blast = new BlastEvent
        {
            Shooter = LocalPeer, Sequence = _sequence++, Tick = now, TickFraction = fraction, Position = at,
            Damage = damage, LethalRadius = lethalRadius, BlastRadius = blastRadius, Missile = missile, Views = Views(now),
        };
        byte[] bytes = blast.ToBytes();
        if (IsHost)
            Judge.Blast(BlastEvent.Read(new BitReader(bytes)));
        else
            Send(HostPeer, KindBlast, bytes, reliable: true);
    }

    /// <summary>A ship here threw out something that flies on its own (<paramref name="scene"/>); everyone else makes it.</summary>
    public void Drop(uint now, byte scene, SectorPoint at, Vector3 velocity)
    {
        byte[] bytes = new DropEvent { Dropper = LocalPeer, Scene = scene, Tick = now, Position = at, Velocity = velocity }.ToBytes();
        if (IsHost)
            Broadcast(KindDrop, bytes, reliable: true, except: null);
        else
            Send(HostPeer, KindDrop, bytes, reliable: true);
    }

    /// <summary>One sector tick: what arrived, the snapshots out, the rounds judged.</summary>
    public void Tick(uint now)
    {
        while (_transport.Poll(out byte peer, out byte[] packet))
            if (packet.Length > 0)
                Arrived(peer, packet, now);
        now = Resync(now);
        Forget(now);
        if (IsHost)
            Welcome();

        foreach (INetEntity e in _entities.Values)
        {
            if (!e.IsLocal)
            {
                e.Track.Advance(now);
                continue;
            }
            ShipSnapshot s = e.Snapshot(now);
            if (IsHost)
                _histories[e.NetId].Receive(s, now);
            if (Synced && now % (uint)SnapshotInterval == 0)
            {
                byte[] bytes = Tagged(e.NetId, s.ToBytes());
                if (IsHost)
                    Broadcast(KindSnapshot, bytes, reliable: false, except: null);
                else
                    Send(HostPeer, KindSnapshot, bytes, reliable: false);
            }
        }
        if (Judge != null)
        {
            Judge.TickSeconds = TickSeconds;
            Judge.Step(now);
        }
        if (!IsHost && now % (uint)(Synced ? PingInterval : 6) == 0)
            Send(HostPeer, KindPing, BitConverter.GetBytes(now), reliable: false);
        _transport.Flush();
    }

    /// <summary>The host: a guest that has just come hears of everything spawned that still flies.</summary>
    private void Welcome()
    {
        _known.IntersectWith(_transport.Peers);
        foreach (byte peer in _transport.Peers)
            if (_known.Add(peer))
                foreach ((ushort id, byte scene) in _spawned)
                    if (NetIds.Owner(id) != peer)
                        Send(peer, KindSpawn, SpawnBody(id, scene), reliable: true);
    }

    /// <summary>A guest: the clock moved to the host's, if it is off.</summary>
    private uint Resync(uint now)
    {
        if (IsHost || _pings.Count == 0 || Synced && _pings.Count < PingSamples)
            return now;
        (uint Trip, int Offset) best = _pings[0];
        foreach ((uint Trip, int Offset) p in _pings)
            if (p.Trip < best.Trip)
                best = p;
        _pings.Clear();
        if (!Synced || Math.Abs(best.Offset) >= 2)
        {
            AdjustClock?.Invoke(best.Offset);
            now = (uint)(now + best.Offset);
            Synced = true;
        }
        return now;
    }

    /// <summary>What was made for others' things and has gone quiet.</summary>
    private void Forget(uint now)
    {
        uint quiet = (uint)(ForgetSeconds / TickSeconds);
        var gone = new List<ushort>();
        foreach ((ushort id, uint heard) in _heard)
            if ((int)(now - heard) > quiet)
                gone.Add(id);
        foreach (ushort id in gone)
        {
            INetEntity e = _entities[id];
            Remove(id);
            _spawned.Remove(id);
            Forgotten?.Invoke(e);
        }
    }

    private void Arrived(byte peer, byte[] packet, uint now)
    {
        byte[] body = packet[1..];
        // The host takes from a guest only what is the guest's
        bool theirs(ushort id) => !IsHost || NetIds.Owner(id) == peer;
        switch (packet[0])
        {
            case KindSnapshot:
            {
                if (body.Length < 2 || !Synced)
                    return;
                ushort id = BitConverter.ToUInt16(body, 0);
                if (!theirs(id) || NetIds.Owner(id) == LocalPeer)
                    return;
                var reader = new BitReader(body[2..]);
                ShipSnapshot s = ShipSnapshot.Read(reader);
                if (reader.Overrun)
                    return;
                INetEntity e = _entities.GetValueOrDefault(id);
                if (e == null && NetIds.IsPlayerShip(id) && SpawnRemote?.Invoke(id) is INetEntity spawned)
                {
                    Add(e = spawned);
                    _heard[id] = now;
                }
                if (e == null || e.IsLocal)
                    return;
                e.Track.Receive(s, now);
                if (_heard.ContainsKey(id))
                    _heard[id] = now;
                if (IsHost)
                    Broadcast(KindSnapshot, body, reliable: false, except: peer);
                break;
            }

            case KindFire:
            {
                FireEvent fired = FireEvent.Read(new BitReader(body));
                if (IsHost)
                {
                    if (fired.Shooter != peer || !theirs(fired.Ship))
                        return;                     // a guest only fires its own rounds
                    Judge.Fire(fired);
                    Broadcast(KindFire, body, reliable: true, except: peer);
                }
                if (fired.Shooter != LocalPeer)
                    RoundFired?.Invoke(fired);
                break;
            }

            case KindHit:
                if (!IsHost && peer == HostPeer)
                    Apply(HitEvent.Read(new BitReader(body)));
                break;

            case KindPing:
                if (IsHost && body.Length == 4)
                {
                    var pong = new byte[8];
                    body.CopyTo(pong, 0);
                    BitConverter.GetBytes(now).CopyTo(pong, 4);
                    Send(peer, KindPong, pong, reliable: false);
                }
                break;

            case KindPong:
                if (!IsHost && peer == HostPeer && body.Length == 8)
                {
                    uint sent = BitConverter.ToUInt32(body, 0), hostNow = BitConverter.ToUInt32(body, 4);
                    uint trip = now - sent;
                    if (trip > 2 / TickSeconds)
                        return;                     // from before the clock moved back
                    RoundTrip = trip;
                    // the host's tick now: what it said, plus the way back (half the round trip)
                    _pings.Add((trip, (int)(hostNow + (trip + 1) / 2 - now)));
                }
                break;

            case KindSpawn:
            {
                if (body.Length != 3)
                    return;
                ushort id = BitConverter.ToUInt16(body, 0);
                byte scene = body[2];
                if (!theirs(id) || !NetIds.IsDynamic(id) || NetIds.Owner(id) == LocalPeer || _entities.ContainsKey(id))
                    return;
                if (IsHost)
                {
                    _spawned[id] = scene;
                    Broadcast(KindSpawn, body, reliable: true, except: peer);
                }
                if (SpawnDynamic?.Invoke(id, scene) is INetEntity made)
                {
                    Add(made);
                    _heard[id] = now;
                }
                break;
            }

            case KindDespawn:
            {
                if (body.Length != 7)
                    return;
                ushort id = BitConverter.ToUInt16(body, 0);
                if (!theirs(id) || NetIds.Owner(id) == LocalPeer)
                    return;
                if (IsHost)
                {
                    _spawned.Remove(id);
                    Broadcast(KindDespawn, body, reliable: true, except: peer);
                }
                if (_entities.GetValueOrDefault(id) is INetEntity e)
                {
                    _heard.Remove(id);              // it said goodbye: not forgotten, ended
                    Despawned?.Invoke(e, BitConverter.ToUInt32(body, 2), body[6] != 0);
                }
                break;
            }

            case KindBlast:
                if (IsHost)
                {
                    BlastEvent blast = BlastEvent.Read(new BitReader(body));
                    if (blast.Shooter == peer && theirs(blast.Missile))
                        Judge.Blast(blast);
                }
                break;

            case KindDrop:
            {
                DropEvent drop = DropEvent.Read(new BitReader(body));
                if (IsHost)
                {
                    if (drop.Dropper != peer)
                        return;
                    Broadcast(KindDrop, body, reliable: true, except: peer);
                }
                if (drop.Dropper != LocalPeer)
                    Dropped?.Invoke(drop);
                break;
            }
        }
    }

    /// <summary>The host's judge has a hit: everyone hears it, the owner takes it.</summary>
    private void Verdict(HitEvent hit)
    {
        Broadcast(KindHit, hit.ToBytes(), reliable: true, except: null);
        Apply(hit);
    }

    private void Apply(HitEvent hit)
    {
        if (_entities.GetValueOrDefault(hit.Target) is INetEntity e && e.IsLocal)
            e.TakeHit(hit);
        HitConfirmed?.Invoke(hit);
    }

    private static byte[] Tagged(ushort id, byte[] bytes)
    {
        var tagged = new byte[bytes.Length + 2];
        BitConverter.GetBytes(id).CopyTo(tagged, 0);
        bytes.CopyTo(tagged, 2);
        return tagged;
    }

    private static byte[] SpawnBody(ushort id, byte scene)
    {
        var body = new byte[3];
        BitConverter.GetBytes(id).CopyTo(body, 0);
        body[2] = scene;
        return body;
    }

    private static byte[] DespawnBody(ushort id, uint tick, bool exploded)
    {
        var body = new byte[7];
        BitConverter.GetBytes(id).CopyTo(body, 0);
        BitConverter.GetBytes(tick).CopyTo(body, 2);
        body[6] = (byte)(exploded ? 1 : 0);
        return body;
    }

    private void Send(byte peer, byte kind, byte[] body, bool reliable)
    {
        var packet = new byte[body.Length + 1];
        packet[0] = kind;
        body.CopyTo(packet, 1);
        _transport.Send(peer, packet, reliable);
    }

    private void Broadcast(byte kind, byte[] body, bool reliable, byte? except)
    {
        foreach (byte peer in _transport.Peers)
            if (peer != except)
                Send(peer, kind, body, reliable);
    }
}
