using System;
using System.Collections.Generic;
using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>A ship in a session, of any kind: one machine owns it, the others show it.</summary>
public interface INetEntity
{
    byte NetId { get; }

    /// <summary>This machine owns it: it is simulated here and its snapshots go out from here.</summary>
    bool IsLocal { get; }

    /// <summary>Owned here: its state now.</summary>
    ShipSnapshot Snapshot(uint tick);

    /// <summary>Owned elsewhere: where its snapshots go (the session advances it each tick).</summary>
    RemoteTrack Track { get; }

    Hitbox[] Hitboxes { get; }

    /// <summary>The host says a round hit it. Only the owner's copy takes damage.</summary>
    void TakeHit(HitEvent hit);
}

/// <summary>
/// One machine's side of a session, star-shaped round the host (peer 0):
///
/// - every entity owned here sends a snapshot every
///   <see cref="SnapshotInterval"/> ticks, guests to the host, the host to
///   every guest (relaying the guests' snapshots to the other guests);
/// - rounds fired here (<see cref="Fire"/>) go to the host as fire events,
///   with how late this machine shows every other ship; the host judges them
///   with its <see cref="LagCompensator"/> and relays them for drawing;
/// - the host's verdicts (hit events) go to everyone; the owner of the ship
///   hit takes the damage.
///
/// Plain code, no nodes: <see cref="SessionNode"/> joins it to the game.
/// </summary>
public sealed class Session
{
    public const byte HostPeer = 0;
    private const byte KindSnapshot = 1, KindFire = 2, KindHit = 3;

    public float TickSeconds = 1f / 60;
    public int SnapshotInterval = 3;

    public byte LocalPeer { get; }
    public bool IsHost => LocalPeer == HostPeer;

    /// <summary>The host's judge of rounds; null on a guest.</summary>
    public LagCompensator Judge { get; }

    /// <summary>A ship heard of for the first time: make what shows it (or null to ignore it).</summary>
    public Func<byte, INetEntity> SpawnRemote;

    /// <summary>A round someone else fired: fly it for show (it does no damage here).</summary>
    public event Action<FireEvent> RoundFired;

    /// <summary>A round hit something, any machine's round.</summary>
    public event Action<HitEvent> HitConfirmed;

    private readonly INetTransport _transport;
    private readonly Dictionary<byte, INetEntity> _entities = new();
    private readonly Dictionary<byte, RemoteTrack> _histories = new();     // the host's past of its own entities
    private ushort _sequence;

    public Session(byte localPeer, INetTransport transport)
    {
        LocalPeer = localPeer;
        _transport = transport;
        if (IsHost)
        {
            Judge = new LagCompensator();
            Judge.Hit += Verdict;
        }
    }

    public IEnumerable<INetEntity> Entities => _entities.Values;

    public INetEntity Entity(byte id) => _entities.GetValueOrDefault(id);

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

    public void Remove(byte id)
    {
        _entities.Remove(id);
        _histories.Remove(id);
        Judge?.Unregister(id);
    }

    /// <summary>
    /// A round leaves a muzzle here. Returns the event as it was sent,
    /// quantised: fly that one, so this machine's round is the host's.
    /// </summary>
    public FireEvent Fire(uint now, SectorPoint origin, Vector3 velocity, float damage)
    {
        var views = new List<FireEvent.View>();
        foreach (INetEntity e in _entities.Values)
            if (!e.IsLocal && e.Track.ShownTick is double shown && views.Count < FireEvent.MaxViews)
                views.Add(new FireEvent.View { Ship = e.NetId, DelayTicks = Math.Max(0, now - shown) });
        var fired = new FireEvent
        {
            Shooter = LocalPeer, Sequence = _sequence++, Tick = now,
            Origin = origin, Velocity = velocity, Damage = damage, Views = views,
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

    /// <summary>One sector tick: what arrived, the snapshots out, the rounds judged.</summary>
    public void Tick(uint now)
    {
        while (_transport.Poll(out byte peer, out byte[] packet))
            if (packet.Length > 0)
                Arrived(peer, packet, now);

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
            if (now % (uint)SnapshotInterval == 0)
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
    }

    private void Arrived(byte peer, byte[] packet, uint now)
    {
        byte[] body = packet[1..];
        switch (packet[0])
        {
            case KindSnapshot:
                if (body.Length < 1)
                    return;
                byte id = body[0];
                if (IsHost ? id != peer : id == LocalPeer)
                    return;                         // a guest only sends its own ship
                var reader = new BitReader(body[1..]);
                ShipSnapshot s = ShipSnapshot.Read(reader);
                if (reader.Overrun)
                    return;
                INetEntity e = _entities.GetValueOrDefault(id);
                if (e == null && SpawnRemote?.Invoke(id) is INetEntity spawned)
                    Add(e = spawned);
                if (e == null || e.IsLocal)
                    return;
                e.Track.Receive(s, now);
                if (IsHost)
                    Broadcast(KindSnapshot, body, reliable: false, except: peer);
                break;

            case KindFire:
                FireEvent fired = FireEvent.Read(new BitReader(body));
                if (IsHost)
                {
                    if (fired.Shooter != peer)
                        return;                     // a guest only fires its own rounds
                    Judge.Fire(fired);
                    Broadcast(KindFire, body, reliable: true, except: peer);
                }
                if (fired.Shooter != LocalPeer)
                    RoundFired?.Invoke(fired);
                break;

            case KindHit:
                if (!IsHost && peer == HostPeer)
                    Apply(HitEvent.Read(new BitReader(body)));
                break;
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

    private static byte[] Tagged(byte id, byte[] bytes)
    {
        var tagged = new byte[bytes.Length + 1];
        tagged[0] = id;
        bytes.CopyTo(tagged, 1);
        return tagged;
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
