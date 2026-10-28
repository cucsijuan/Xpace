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
///   every guest (relaying the guests' snapshots to the other guests); each
///   goes as a delta from the newest one the receiver said it has (acks,
///   once a tick), or whole when there is none;
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
/// - the host speaks for the stations: what its radios say to a guest's
///   ship goes to that guest (<see cref="Say"/>), a guest's requests go to
///   it (<see cref="Request"/>) and its answers come back
///   (<see cref="Cleared"/>), and every machine opens and shuts the docks
///   the host does (<see cref="Dock"/>, told to late joiners too);
/// - each ship's radio, as its owner has it (callsign, on or off:
///   <see cref="SetRadio"/>), is known everywhere, late joiners included;
/// - a player on foot (<see cref="Walk"/>) is sent with the snapshots, guests
///   to the host and the host to everyone (relaying the guests'), and once
///   when they stop being on foot; silent for <see cref="ForgetSeconds"/>,
///   they are gone too;
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
                       KindSpawn = 6, KindDespawn = 7, KindBlast = 8, KindDrop = 9, KindDelta = 10, KindAck = 11, KindRadio = 12,
                       KindRadioState = 13, KindRequest = 14, KindCleared = 15, KindDock = 16, KindPilot = 17, KindDocument = 18,
                       KindAsk = 19, KindWalker = 20;

    /// <summary>A document for a guest goes in parts of at most this many bytes (EOS carries 1170 a packet).</summary>
    public const int PartBytes = 1000;
    private const uint AckLost = uint.MaxValue;     // an ack saying: the base of that delta isn't here, send it whole
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

    /// <summary>A ship of this machine hears the host's radio: the ship, who speaks, the words, the speech (<see cref="RadioVoicePack"/>).</summary>
    public event Action<INetEntity, string, string, string> RadioHeard;

    /// <summary>A ship's radio, as its owner has it (the ship, callsign, on).</summary>
    public event Action<ushort, string, bool> RadioChanged;

    /// <summary>The host: another machine's ship asks a station (the ship, the station's callsign, what).</summary>
    public event Action<ushort, string, byte> Requested;

    /// <summary>The host's station cleared a ship of this machine to a dock (the ship, the station, the dock; 0: over).</summary>
    public event Action<INetEntity, string, int, int> DockCleared;

    /// <summary>The host opened or shut a station's dock (the station, the dock, open).</summary>
    public event Action<string, int, bool> DockChanged;

    /// <summary>The host: a guest says who flies its ship (the guest, the pilot's id, the pilot's name).</summary>
    public event Action<byte, string, string> PilotIntroduced;

    /// <summary>A guest: the host sent a document on a topic (the topic, the document), whole once all its parts came.</summary>
    public event Action<string, string> DocumentArrived;

    /// <summary>The host: a guest asks something of it (the guest, the topic, what it asks).</summary>
    public event Action<byte, string, string> Asked;

    /// <summary>Someone else dropped something: make it, already flying.</summary>
    public event Action<DropEvent> Dropped;

    /// <summary>Something made for another machine went silent and was removed: take away what shows it.</summary>
    public event Action<INetEntity> Forgotten;

    /// <summary>Another machine's player is on foot, or moved (their peer, how they are now).</summary>
    public event Action<byte, WalkerState> WalkerMoved;

    /// <summary>Another machine's player is no longer on foot (or left).</summary>
    public event Action<byte> WalkerGone;

    /// <summary>Snapshots sent whole and as deltas, and the bytes of both (their packets' bodies).</summary>
    public int WholeSent { get; private set; }
    public int DeltasSent { get; private set; }
    public long SnapshotBytes { get; private set; }

    /// <summary>Deltas that came with a base no longer here (thrown away; the sender is told).</summary>
    public int DeltasLost { get; private set; }

    private readonly INetTransport _transport;
    private readonly Dictionary<ushort, INetEntity> _entities = new();
    private readonly Dictionary<ushort, RemoteTrack> _histories = new();   // the host's past of its own entities
    private readonly Dictionary<ushort, uint> _heard = new();              // made for others' things: the tick last heard
    private readonly Dictionary<ushort, byte> _spawned = new();            // the host: what flies that was spawned, and its scene
    private readonly HashSet<byte> _known = new();                         // the host: peers already told of it
    private readonly List<(uint Trip, int Offset)> _pings = new();
    private readonly Dictionary<ushort, List<ShipSnapshot.Wire>> _sent = new();       // what goes out of each, newest last
    private readonly Dictionary<ushort, List<ShipSnapshot.Wire>> _got = new();        // what came in of each, newest last
    private readonly Dictionary<(byte Peer, ushort Id), uint> _acked = new();         // the newest each peer has of each
    private readonly Dictionary<byte, Dictionary<ushort, uint>> _toAck = new();       // to tell each peer: what came of theirs
    private readonly Dictionary<ushort, byte[]> _radios = new();           // the host: each ship's radio state, as sent
    private readonly Dictionary<(string Station, int Dock), bool> _docks = new();   // the host: docks open
    private readonly Dictionary<ushort, byte[][]> _parts = new();          // a guest: documents still coming, by number
    private readonly Dictionary<byte, WalkerState> _walkers = new();      // others on foot, by peer
    private readonly Dictionary<byte, uint> _walkerTicks = new();           // the newest of each peer's, by their tick
    private readonly Dictionary<byte, uint> _walkerHeard = new();           // when each was last heard, by this one's
    private WalkerState? _walking;
    private bool _walked;
    private ushort _sequence, _documents;
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

    /// <summary>The other machines in the session (the host: its guests; a guest: the host).</summary>
    public IReadOnlyList<byte> Peers => _transport.Peers;

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
        _radios.Remove(id);
        _histories.Remove(id);
        _heard.Remove(id);
        _sent.Remove(id);
        _got.Remove(id);
        foreach ((byte, ushort Id) key in new List<(byte, ushort)>(_acked.Keys))
            if (key.Id == id)
                _acked.Remove(key);
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
    public FireEvent Fire(uint now, ushort ship, SectorPoint origin, Vector3 velocity, float damage, byte kind = 0)
    {
        var fired = new FireEvent
        {
            Shooter = LocalPeer, Ship = ship, Sequence = _sequence++, Tick = now,
            Origin = origin, Velocity = velocity, Damage = damage, Kind = kind, Views = Views(now),
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

    /// <summary>
    /// The warhead of <paramref name="missile"/>, flown here and launched by
    /// <paramref name="ship"/> (this machine's player ship if not given),
    /// went off: the host hurts what it reached.
    /// </summary>
    public void Blast(uint now, float fraction, ushort missile, SectorPoint at, float damage, float lethalRadius, float blastRadius,
                      ushort? ship = null)
    {
        var blast = new BlastEvent
        {
            Shooter = LocalPeer, Sequence = _sequence++, Tick = now, TickFraction = fraction, Position = at,
            Damage = damage, LethalRadius = lethalRadius, BlastRadius = blastRadius, Missile = missile,
            Ship = ship ?? NetIds.PlayerShip(LocalPeer), Views = Views(now),
        };
        byte[] bytes = blast.ToBytes();
        if (IsHost)
            Judge.Blast(BlastEvent.Read(new BitReader(bytes)));
        else
            Send(HostPeer, KindBlast, bytes, reliable: true);
    }

    /// <summary>
    /// The host: a radio here says something to <paramref name="ship"/>, of
    /// another machine; that machine's ship hears it
    /// (<see cref="RadioHeard"/> there): the words and how they are spoken
    /// (at most 255 characters). Both are plain ASCII.
    /// </summary>
    public void Say(ushort ship, string from, string text, string speech = "")
    {
        byte owner = NetIds.Owner(ship);
        if (!IsHost || owner == LocalPeer)
            return;
        var body = new List<byte>(BitConverter.GetBytes(ship));
        body.AddRange(Sign(from));
        byte[] spoken = Ascii(speech);
        body.Add((byte)Math.Min(spoken.Length, 255));
        body.AddRange(spoken[..Math.Min(spoken.Length, 255)]);
        body.AddRange(Ascii(text));
        Send(owner, KindRadio, body.ToArray(), reliable: true);
    }

    /// <summary>A ship owned here: its radio's callsign and whether it is on (send when either changes).</summary>
    public void SetRadio(ushort ship, string callsign, bool on)
    {
        var body = new List<byte>(BitConverter.GetBytes(ship)) { (byte)(on ? 1 : 0) };
        body.AddRange(Sign(callsign));
        byte[] bytes = body.ToArray();
        if (IsHost)
        {
            _radios[ship] = bytes;
            Broadcast(KindRadioState, bytes, reliable: true, except: null);
        }
        else
            Send(HostPeer, KindRadioState, bytes, reliable: true);
    }

    /// <summary>This machine's player on foot as they are now (each tick), or null: not on foot.</summary>
    public void Walk(WalkerState? state) => _walking = state;

    /// <summary>Another machine's player on foot, as last heard; null: not on foot.</summary>
    public WalkerState? WalkerOf(byte peer) => _walkers.TryGetValue(peer, out WalkerState s) ? s : null;

    /// <summary>The other machines' players on foot, by peer.</summary>
    public IReadOnlyDictionary<byte, WalkerState> Walkers => _walkers;

    /// <summary>A guest: its ship asks a station (by callsign) for something; the host's station answers.</summary>
    public void Request(ushort ship, string station, byte what)
    {
        if (IsHost)
            return;
        var body = new List<byte>(BitConverter.GetBytes(ship));
        body.AddRange(Sign(station));
        body.Add(what);
        Send(HostPeer, KindRequest, body.ToArray(), reliable: true);
    }

    /// <summary>The host: a station cleared another machine's ship to a dock and a pad in it (dock 0: the clearance is over).</summary>
    public void Cleared(ushort ship, string station, int dock, int pad = 0)
    {
        byte owner = NetIds.Owner(ship);
        if (!IsHost || owner == LocalPeer)
            return;
        var body = new List<byte>(BitConverter.GetBytes(ship));
        body.AddRange(Sign(station));
        body.Add((byte)Math.Clamp(dock, 0, 255));
        body.Add((byte)Math.Clamp(pad, 0, 255));
        Send(owner, KindCleared, body.ToArray(), reliable: true);
    }

    /// <summary>The host: a station's dock opens or shuts, on every machine.</summary>
    public void Dock(string station, int dock, bool open)
    {
        if (!IsHost)
            return;
        if (open)
            _docks[(station, dock)] = true;
        else
            _docks.Remove((station, dock));
        Broadcast(KindDock, DockBody(station, dock, open), reliable: true, except: null);
    }

    private static byte[] DockBody(string station, int dock, bool open)
    {
        var body = new List<byte>(Sign(station)) { (byte)Math.Clamp(dock, 0, 255), (byte)(open ? 1 : 0) };
        return body.ToArray();
    }

    private static byte[] Sign(string callsign) => Ascii((callsign ?? "").PadRight(Radio.CallsignLength)[..Radio.CallsignLength]);

    private static byte[] Ascii(string text)
    {
        var bytes = new byte[(text ?? "").Length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = text[i] is >= ' ' and <= '~' ? (byte)text[i] : (byte)'?';
        return bytes;
    }

    private static string Text(byte[] bytes, int from, int to) =>
        System.Text.Encoding.ASCII.GetString(bytes, from, Math.Min(to, bytes.Length) - from);

    /// <summary>A guest: tells the host who flies its ship (send until the host answers with the pilot's record).</summary>
    public void Introduce(string pilot, string name)
    {
        if (IsHost)
            return;
        var body = new List<byte>();
        Counted(body, pilot);
        body.AddRange(Utf8(name, PartBytes / 2));
        Send(HostPeer, KindPilot, body.ToArray(), reliable: true);
    }

    /// <summary>The host: a document on <paramref name="topic"/> for a guest, any length (it goes in parts).</summary>
    public void SendDocument(byte peer, string topic, string text)
    {
        if (!IsHost)
            return;
        byte[] all = System.Text.Encoding.UTF8.GetBytes(text ?? "");
        int count = Math.Max(1, (all.Length + PartBytes - 1) / PartBytes);
        if (count > 255)
            throw new ArgumentException($"a document of {all.Length} bytes is too long to send");
        ushort number = _documents++;
        for (int part = 0; part < count; part++)
        {
            var body = new List<byte>(BitConverter.GetBytes(number)) { (byte)part, (byte)count };
            Counted(body, topic);
            body.AddRange(all[(part * PartBytes)..Math.Min(all.Length, (part + 1) * PartBytes)]);
            Send(peer, KindDocument, body.ToArray(), reliable: true);
        }
    }

    /// <summary>A guest: asks the host something on <paramref name="topic"/> (at most <see cref="PartBytes"/> bytes).</summary>
    public void Ask(string topic, string text)
    {
        if (IsHost)
            return;
        var body = new List<byte>();
        Counted(body, topic);
        body.AddRange(Utf8(text, PartBytes));
        Send(HostPeer, KindAsk, body.ToArray(), reliable: true);
    }

    // A string with its length before it, in a byte
    private static void Counted(List<byte> body, string text)
    {
        byte[] bytes = Utf8(text, 255);
        body.Add((byte)bytes.Length);
        body.AddRange(bytes);
    }

    private static string Counted(byte[] body, ref int at)
    {
        int length = at < body.Length ? body[at] : 0;
        if (at + 1 + length > body.Length)
            throw new FormatException("a string runs past the message");
        string text = System.Text.Encoding.UTF8.GetString(body, at + 1, length);
        at += 1 + length;
        return text;
    }

    private static byte[] Utf8(string text, int most)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? "");
        return bytes.Length <= most ? bytes : bytes[..most];
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
                try
                {
                    Arrived(peer, packet, now);
                }
                catch (Exception e) when (e is FormatException or ArgumentException)
                {
                    // a malformed packet: dropped
                }
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
                ShipSnapshot.Wire wire = s.Encode();
                Keep(_sent, e.NetId, wire);
                if (IsHost)
                    foreach (byte peer in _transport.Peers)
                        SendSnapshot(peer, e.NetId, wire);
                else
                    SendSnapshot(HostPeer, e.NetId, wire);
            }
        }
        SendWalker(now);
        SendAcks();
        if (Judge != null)
        {
            Judge.TickSeconds = TickSeconds;
            Judge.Step(now);
        }
        if (!IsHost && now % (uint)(Synced ? PingInterval : 6) == 0)
            Send(HostPeer, KindPing, BitConverter.GetBytes(now), reliable: false);
        _transport.Flush();
    }

    /// <summary>This machine's player on foot, with the snapshots; once, reliably, when they are no longer.</summary>
    private void SendWalker(uint now)
    {
        if (!Synced)
            return;
        byte[] body;
        bool reliable = false;
        if (_walking is WalkerState state)
        {
            if (now % (uint)SnapshotInterval != 0)
                return;
            state.Tick = now;
            var b = new List<byte> { LocalPeer, 1 };
            b.AddRange(state.ToBytes());
            body = b.ToArray();
            _walked = true;
        }
        else if (_walked)
        {
            var b = new List<byte> { LocalPeer, 0 };
            b.AddRange(BitConverter.GetBytes(now));
            body = b.ToArray();
            reliable = true;
            _walked = false;
        }
        else
            return;
        if (IsHost)
            Broadcast(KindWalker, body, reliable, except: null);
        else
            Send(HostPeer, KindWalker, body, reliable);
    }

    /// <summary>The host: a guest that has just come hears of everything spawned that still flies.</summary>
    private void Welcome()
    {
        _known.IntersectWith(_transport.Peers);
        foreach (byte peer in _transport.Peers)
            if (_known.Add(peer))
            {
                foreach ((byte Peer, ushort) key in new List<(byte, ushort)>(_acked.Keys))
                    if (key.Peer == peer)
                        _acked.Remove(key);         // a new guest with an old one's number has none of it
                foreach ((ushort id, byte scene) in _spawned)
                    if (NetIds.Owner(id) != peer)
                        Send(peer, KindSpawn, SpawnBody(id, scene), reliable: true);
                foreach ((ushort id, byte[] radio) in _radios)
                    if (NetIds.Owner(id) != peer)
                        Send(peer, KindRadioState, radio, reliable: true);
                foreach ((string station, int dock) in _docks.Keys)
                    Send(peer, KindDock, DockBody(station, dock, true), reliable: true);
            }
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
        var silent = new List<byte>();
        foreach ((byte peer, uint heard) in _walkerHeard)
            if ((int)(now - heard) > quiet)
                silent.Add(peer);
        foreach (byte peer in silent)
        {
            _walkers.Remove(peer);
            _walkerHeard.Remove(peer);
            _walkerTicks.Remove(peer);
            WalkerGone?.Invoke(peer);
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
            case KindDelta:
            {
                if (body.Length < 2 || !Synced)
                    return;
                ushort id = BitConverter.ToUInt16(body, 0);
                if (!theirs(id) || NetIds.Owner(id) == LocalPeer)
                    return;
                var reader = new BitReader(body[2..]);
                ShipSnapshot.Wire wire;
                if (packet[0] == KindSnapshot)
                    wire = ShipSnapshot.Wire.Read(reader);
                else
                {
                    (uint tick, uint baseTick) = ShipSnapshot.Wire.ReadDeltaHeader(reader, now);
                    if (Find(_got, id, baseTick) is not ShipSnapshot.Wire b)
                    {
                        DeltasLost++;
                        Ack(peer, id, AckLost);
                        return;
                    }
                    wire = ShipSnapshot.Wire.ReadDelta(reader, tick, b);
                }
                if (reader.Overrun)
                    return;
                if (Keep(_got, id, wire))
                    Ack(peer, id, wire.Tick);
                ShipSnapshot s = wire.Decode();
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
                {
                    Keep(_sent, id, wire);
                    foreach (byte other in _transport.Peers)
                        if (other != peer)
                            SendSnapshot(other, id, wire);
                }
                break;
            }

            case KindAck:
                for (int at = 0; at + 6 <= body.Length; at += 6)
                {
                    ushort id = BitConverter.ToUInt16(body, at);
                    uint tick = BitConverter.ToUInt32(body, at + 2);
                    if (tick == AckLost)
                        _acked.Remove((peer, id));
                    else if (!_acked.TryGetValue((peer, id), out uint had) || (int)(tick - had) > 0)
                        _acked[(peer, id)] = tick;
                }
                break;

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
                    if (blast.Shooter == peer && theirs(blast.Missile) && theirs(blast.Ship))
                        Judge.Blast(blast);
                }
                break;

            case KindRadio:
                if (!IsHost && peer == HostPeer && body.Length >= 3 + Radio.CallsignLength &&
                    body.Length >= 3 + Radio.CallsignLength + body[2 + Radio.CallsignLength])
                {
                    ushort id = BitConverter.ToUInt16(body, 0);
                    int spoken = 3 + Radio.CallsignLength, text = spoken + body[spoken - 1];
                    if (_entities.GetValueOrDefault(id) is INetEntity { IsLocal: true } e)
                        RadioHeard?.Invoke(e, Text(body, 2, 2 + Radio.CallsignLength).TrimEnd(), Text(body, text, body.Length), Text(body, spoken, text));
                }
                break;

            case KindRadioState:
                if (body.Length == 3 + Radio.CallsignLength)
                {
                    ushort id = BitConverter.ToUInt16(body, 0);
                    if (!theirs(id) || NetIds.Owner(id) == LocalPeer)
                        return;
                    if (IsHost)
                    {
                        _radios[id] = body;
                        Broadcast(KindRadioState, body, reliable: true, except: peer);
                    }
                    RadioChanged?.Invoke(id, Text(body, 3, body.Length).TrimEnd(), body[2] != 0);
                }
                break;

            case KindRequest:
                if (IsHost && body.Length == 3 + Radio.CallsignLength)
                {
                    ushort id = BitConverter.ToUInt16(body, 0);
                    if (theirs(id))
                        Requested?.Invoke(id, Text(body, 2, 2 + Radio.CallsignLength).TrimEnd(), body[^1]);
                }
                break;

            case KindCleared:
                if (!IsHost && peer == HostPeer && body.Length == 4 + Radio.CallsignLength &&
                    _entities.GetValueOrDefault(BitConverter.ToUInt16(body, 0)) is INetEntity { IsLocal: true } cleared)
                    DockCleared?.Invoke(cleared, Text(body, 2, 2 + Radio.CallsignLength).TrimEnd(), body[^2], body[^1]);
                break;

            case KindDock:
                if (!IsHost && peer == HostPeer && body.Length == 2 + Radio.CallsignLength)
                    DockChanged?.Invoke(Text(body, 0, Radio.CallsignLength).TrimEnd(), body[^2], body[^1] != 0);
                break;

            case KindPilot:
                if (IsHost)
                {
                    int at = 0;
                    string pilot = Counted(body, ref at);
                    if (pilot.Length > 0)
                        PilotIntroduced?.Invoke(peer, pilot, System.Text.Encoding.UTF8.GetString(body, at, body.Length - at));
                }
                break;

            case KindDocument:
                if (!IsHost && peer == HostPeer && body.Length >= 5)
                {
                    ushort number = BitConverter.ToUInt16(body, 0);
                    int part = body[2], count = body[3], at = 4;
                    string topic = Counted(body, ref at);
                    if (count == 0 || part >= count)
                        return;
                    if (!_parts.TryGetValue(number, out byte[][] parts) || parts.Length != count)
                        _parts[number] = parts = new byte[count][];
                    parts[part] = body[at..];
                    if (Array.IndexOf(parts, null) >= 0)
                        return;
                    _parts.Remove(number);
                    var whole = new List<byte>();
                    foreach (byte[] p in parts)
                        whole.AddRange(p);
                    DocumentArrived?.Invoke(topic, System.Text.Encoding.UTF8.GetString(whole.ToArray()));
                }
                break;

            case KindAsk:
                if (IsHost)
                {
                    int at = 0;
                    string topic = Counted(body, ref at);
                    Asked?.Invoke(peer, topic, System.Text.Encoding.UTF8.GetString(body, at, body.Length - at));
                }
                break;

            case KindWalker:
            {
                if (body.Length < 2)
                    return;
                byte owner = body[0];
                if (owner == LocalPeer || IsHost && owner != peer || !IsHost && peer != HostPeer)
                    return;
                WalkerState? state = body[1] != 0 ? WalkerState.Read(body, 2) : null;
                if (body[1] != 0 ? state == null : body.Length != 6)
                    return;
                uint tick = state?.Tick ?? BitConverter.ToUInt32(body, 2);
                if (_walkerTicks.TryGetValue(owner, out uint newest) && (int)(tick - newest) <= 0)
                    return;                             // older than what came already (unreliable ones come in any order)
                _walkerTicks[owner] = tick;
                if (IsHost)
                    Broadcast(KindWalker, body, reliable: state == null, except: peer);
                if (state is WalkerState s)
                {
                    _walkers[owner] = s;
                    _walkerHeard[owner] = now;
                    WalkerMoved?.Invoke(owner, s);
                }
                else if (_walkers.Remove(owner))
                {
                    _walkerHeard.Remove(owner);
                    WalkerGone?.Invoke(owner);
                }
                break;
            }

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

    /// <summary>A snapshot of <paramref name="id"/> to <paramref name="peer"/>: a delta from the newest it has, or whole.</summary>
    private void SendSnapshot(byte peer, ushort id, in ShipSnapshot.Wire wire)
    {
        var w = new BitWriter();
        w.Write(id, 16);
        byte kind = KindSnapshot;
        if (_acked.TryGetValue((peer, id), out uint acked) && (int)(wire.Tick - acked) is > 0 and <= ShipSnapshot.Wire.MaxAge &&
            Find(_sent, id, acked) is ShipSnapshot.Wire b)
        {
            wire.WriteDelta(w, b);
            kind = KindDelta;
            DeltasSent++;
        }
        else
        {
            wire.Write(w);
            WholeSent++;
        }
        byte[] body = w.ToArray();
        SnapshotBytes += body.Length;
        Send(peer, kind, body, reliable: false);
    }

    /// <summary>Keeps a snapshot of <paramref name="id"/> (not if it is older than the newest), dropping those too old to be a delta's base.</summary>
    private static bool Keep(Dictionary<ushort, List<ShipSnapshot.Wire>> kept, ushort id, in ShipSnapshot.Wire wire)
    {
        if (!kept.TryGetValue(id, out List<ShipSnapshot.Wire> list))
            kept[id] = list = new List<ShipSnapshot.Wire>();
        if (list.Count > 0 && (int)(wire.Tick - list[^1].Tick) <= 0)
            return false;                           // late or repeated: the newer ones are the bases
        list.Add(wire);
        uint newest = wire.Tick;
        list.RemoveAll(k => (int)(newest - k.Tick) > ShipSnapshot.Wire.MaxAge);
        return true;
    }

    private static ShipSnapshot.Wire? Find(Dictionary<ushort, List<ShipSnapshot.Wire>> kept, ushort id, uint tick)
    {
        if (kept.TryGetValue(id, out List<ShipSnapshot.Wire> list))
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Tick == tick)
                    return list[i];
        return null;
    }

    private void Ack(byte peer, ushort id, uint tick)
    {
        if (!_toAck.TryGetValue(peer, out Dictionary<ushort, uint> acks))
            _toAck[peer] = acks = new Dictionary<ushort, uint>();
        if (tick == AckLost || !acks.TryGetValue(id, out uint had) || had == AckLost || (int)(tick - had) > 0)
            acks[id] = tick;
    }

    /// <summary>Once a tick: each peer hears the newest snapshot that came of each of its things (6 bytes each).</summary>
    private void SendAcks()
    {
        foreach ((byte peer, Dictionary<ushort, uint> acks) in _toAck)
        {
            if (acks.Count == 0)
                continue;
            var body = new byte[acks.Count * 6];
            int at = 0;
            foreach ((ushort id, uint tick) in acks)
            {
                BitConverter.GetBytes(id).CopyTo(body, at);
                BitConverter.GetBytes(tick).CopyTo(body, at + 2);
                at += 6;
            }
            acks.Clear();
            Send(peer, KindAck, body, reliable: false);
        }
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
