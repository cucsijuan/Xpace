using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Xpace.Net;

/// <summary>
/// Packets between machines named by an address (text), whatever carries them: EOS peer-to-peer connections in the game
/// (players by their product user ids), <see cref="MemoryLinks"/> in tests. Channels 0 and 1 carry the session's
/// unreliable and reliable packets, 2 the transport's own word.
/// </summary>
public interface IPeerLink
{
    void Send(string to, byte channel, byte[] data, bool reliable);

    bool Receive(out string from, out byte channel, out byte[] data);

    /// <summary>A peer whose connection closed.</summary>
    bool TakeClosed(out string peer);

    void Close(string peer);
}

/// <summary>
/// The session over an <see cref="IPeerLink"/>, star-shaped like <see cref="EnetTransport"/>: guests say hello to the
/// host, which numbers the ones it admits (for EOS: members of its lobby) 1 to <see cref="MaxGuests"/> and tells each
/// its number; anyone else, and any other version of the game, is turned away. A guest says hello again every second
/// until it is answered (the host may not know it is a member yet), and gives up after a while (<see cref="JoinTimeout"/>
/// by default). A peer silent for <see cref="SilenceTimeout"/> is gone, whatever the link says (its game crashed, or
/// its goodbye never came): the session always has something to say.
/// </summary>
public sealed class StarTransport : INetTransport, IDisposable
{
    public const int MaxGuests = EnetTransport.MaxGuests;
    public const double JoinTimeout = 20;
    public const double SilenceTimeout = 10;

    /// <summary>A guest's number is not given again for this long: the sessions must forget its ship first (3 s).</summary>
    public const double ReuseDelay = 5;
    private const byte Unreliable = 0, Reliable = 1, Control = 2;
    private const byte Hello = 1, Welcome = 2, Refused = 3;

    /// <summary>The packets' version: the same as over ENet.</summary>
    public static byte Version => EnetTransport.Version;

    public bool IsHost { get; }

    /// <summary>This machine's peer: 0 on the host, the host's number on a guest once it has it.</summary>
    public byte? LocalPeer { get; private set; }

    public IReadOnlyList<byte> Peers => _peerList;

    /// <summary>A guest came (on the host), or the host numbered this guest (on a guest).</summary>
    public event Action<byte> PeerJoined;

    /// <summary>A guest left (on the host), or the host is gone or turned this guest away (on a guest: peer 0).</summary>
    public event Action<byte> PeerLeft;

    /// <summary>On a guest: why the host turned it away.</summary>
    public string Refusal { get; private set; }

    private readonly IPeerLink _link;
    private readonly Func<string, bool> _admits;
    private readonly string _host;
    private readonly Dictionary<byte, string> _addresses = new();
    private readonly Dictionary<string, byte> _ids = new();
    private readonly Dictionary<string, double> _heard = new();          // when each numbered peer was last heard
    private readonly Dictionary<byte, double> _released = new();         // when each number was let go
    private readonly List<byte> _peerList = new();
    private readonly Queue<(byte, byte[])> _arrived = new();
    private readonly Func<double> _clock;
    private double _started;
    private double _lastHello = double.NegativeInfinity;
    private bool _gone;

    private readonly double _timeout;

    private StarTransport(IPeerLink link, bool host, Func<string, bool> admits, string hostAddress, Func<double> clock = null,
                          double timeout = JoinTimeout)
    {
        _link = link;
        if (clock == null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }
        _clock = clock;
        _timeout = timeout;
        IsHost = host;
        _admits = admits;
        _host = hostAddress;
    }

    /// <summary>Hosts a session: guests <paramref name="admits"/> lets in are numbered.</summary>
    public static StarTransport Host(IPeerLink link, Func<string, bool> admits, Func<double> clock = null) =>
        new(link, true, admits, null, clock) { LocalPeer = Session.HostPeer };

    /// <summary>
    /// Joins the session hosted at <paramref name="host"/>. <paramref name="clock"/> gives seconds, for saying hello
    /// again and giving up after <paramref name="timeout"/> (real time by default; a test's own time in tests).
    /// </summary>
    public static StarTransport Join(IPeerLink link, string host, Func<double> clock = null, double timeout = JoinTimeout)
    {
        var t = new StarTransport(link, false, a => a == host, host, clock, timeout);
        t._started = t._clock();
        t.SayHello();
        return t;
    }

    public void Send(byte peer, byte[] packet, bool reliable)
    {
        if (_addresses.TryGetValue(peer, out string address))
            _link.Send(address, reliable ? Reliable : Unreliable, packet, reliable);
    }

    public bool Poll(out byte peer, out byte[] packet)
    {
        if (_arrived.Count == 0)
            Service();
        if (_arrived.Count == 0)
        {
            peer = 0;
            packet = null;
            return false;
        }
        (peer, packet) = _arrived.Dequeue();
        return true;
    }

    /// <summary>Takes in what the link brought: comings, goings and packets (queued for <see cref="Poll"/>).</summary>
    public void Service()
    {
        double now = _clock();
        while (_link.Receive(out string from, out byte channel, out byte[] data))
        {
            if (_ids.ContainsKey(from))
                _heard[from] = now;
            if (channel == Control)
                Told(from, data);
            else if (_ids.TryGetValue(from, out byte id))
                _arrived.Enqueue((id, data));
        }
        while (_link.TakeClosed(out string peer))
            Went(peer);
        foreach (var (address, heard) in new List<KeyValuePair<string, double>>(_heard))
            if (now - heard > SilenceTimeout)
                Drop(address);
        if (!IsHost && !LocalPeer.HasValue && !_gone)
        {
            double waited = _clock() - _started;
            if (waited > _timeout)
                GiveUp("the host didn't answer");
            else if (waited - _lastHello >= 1)
                SayHello();
        }
    }

    /// <summary>
    /// <paramref name="address"/> is out: on the host, a guest that may no longer play (it left the lobby) or went
    /// silent; on a guest, the host.
    /// </summary>
    public void Drop(string address)
    {
        if (_ids.ContainsKey(address))
            _link.Close(address);
        Went(address);
    }

    private void SayHello()
    {
        _lastHello = _clock() - _started;
        _link.Send(_host, Control, new[] { Hello, Version }, true);
    }

    private void Told(string from, byte[] data)
    {
        if (data.Length < 2)
            return;
        if (IsHost && data[0] == Hello)
            Came(from, data[1]);
        else if (!IsHost && from == _host && data[0] == Welcome && data.Length == 3 && !LocalPeer.HasValue && !_gone)
        {
            if (data[1] != Version)
            {
                GiveUp($"the host speaks version {data[1]}, this is {Version}");
                return;
            }
            LocalPeer = data[2];
            _addresses[Session.HostPeer] = _host;
            _ids[_host] = Session.HostPeer;
            _heard[_host] = _clock();
            _peerList.Add(Session.HostPeer);
            PeerJoined?.Invoke(data[2]);
        }
        else if (!IsHost && from == _host && data[0] == Refused && !LocalPeer.HasValue)
            GiveUp(data[1] != Version ? $"the host speaks version {data[1]}, this is {Version}" : "the host is full or didn't let this player in");
    }

    /// <summary>The host: a guest said hello; number it if it is let in.</summary>
    private void Came(string address, byte version)
    {
        if (_ids.TryGetValue(address, out byte known))
        {
            Welcomes(address, known);           // it missed the first answer
            return;
        }
        if (!_admits(address))
            return;                             // not one of ours (yet): silence, it says hello again
        byte id = 1;
        while (_addresses.ContainsKey(id) || _released.TryGetValue(id, out double let) && _clock() - let < ReuseDelay)
            id++;
        if (version != Version || id > MaxGuests)
        {
            _link.Send(address, Control, new[] { Refused, Version }, true);
            return;
        }
        _addresses[id] = address;
        _ids[address] = id;
        _heard[address] = _clock();
        _peerList.Add(id);
        Welcomes(address, id);
        PeerJoined?.Invoke(id);
    }

    private void Welcomes(string address, byte id) => _link.Send(address, Control, new[] { Welcome, Version, id }, true);

    private void GiveUp(string why)
    {
        Refusal = why;
        _gone = true;
        _link.Close(_host);
        PeerLeft?.Invoke(Session.HostPeer);
    }

    private void Went(string address)
    {
        _heard.Remove(address);
        if (!_ids.Remove(address, out byte id))
        {
            if (!IsHost && address == _host && !_gone)
                GiveUp("the host is gone");     // never got in
            return;
        }
        _addresses.Remove(id);
        _peerList.Remove(id);
        _released[id] = _clock();
        if (!IsHost)
            _gone = true;
        PeerLeft?.Invoke(id);
    }

    public void Dispose()
    {
        foreach (string address in _addresses.Values)
            _link.Close(address);
        _addresses.Clear();
        _ids.Clear();
        _heard.Clear();
        _peerList.Clear();
        (_link as IDisposable)?.Dispose();
    }
}

/// <summary>
/// <see cref="IPeerLink"/>s in one process, for tests: packets arrive at the receiver's next look, in order; a closed
/// link is heard as closed by everyone who talked with it.
/// </summary>
public sealed class MemoryLinks
{
    private readonly Dictionary<string, Link> _links = new();

    public IPeerLink Open(string address) => _links[address] = new Link(this, address);

    private sealed class Link : IPeerLink, IDisposable
    {
        private readonly MemoryLinks _net;
        private readonly string _self;
        public readonly Queue<(string, byte, byte[])> Inbox = new();
        public readonly Queue<string> Closed = new();
        public readonly HashSet<string> Talked = new();

        public Link(MemoryLinks net, string self) { _net = net; _self = self; }

        public void Send(string to, byte channel, byte[] data, bool reliable)
        {
            if (!_net._links.TryGetValue(to, out Link other))
                return;
            Talked.Add(to);
            other.Talked.Add(_self);
            other.Inbox.Enqueue((_self, channel, data));
        }

        public bool Receive(out string from, out byte channel, out byte[] data)
        {
            if (Inbox.TryDequeue(out var p))
            {
                (from, channel, data) = p;
                return true;
            }
            (from, channel, data) = (null, 0, null);
            return false;
        }

        public bool TakeClosed(out string peer) => Closed.TryDequeue(out peer);

        public void Close(string peer)
        {
            if (Talked.Remove(peer) && _net._links.TryGetValue(peer, out Link other) && other.Talked.Remove(_self))
                other.Closed.Enqueue(_self);
        }

        public void Dispose()
        {
            foreach (string peer in new List<string>(Talked))
                Close(peer);
            _net._links.Remove(_self);
        }
    }
}
